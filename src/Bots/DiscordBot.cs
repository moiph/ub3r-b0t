
namespace UB3RB0T
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Linq;
    using System.Reflection;
    using System.Text.RegularExpressions;
    using System.Threading;
    using System.Threading.Channels;
    using System.Threading.Tasks;
    using Discord;
    using Discord.WebSocket;
    using Flurl.Http;
    using Newtonsoft.Json;
    using Serilog;
    using UB3RB0T.Commands;
    using UB3RB0T.Modules;

    public partial class DiscordBot : Bot
    {
        private const int TWELVE_HOURS = 1000 * 60 * 60 * 12;
        private const int FIVE_MINUTES = 1000 * 60 * 5;
        private readonly AudioManager audioManager = new AudioManager();
        private Timer statsTimer;
        private Dictionary<string, IDiscordCommand> discordCommands;

        private readonly Dictionary<IModule, TypeInfo> preProcessModules = new Dictionary<IModule, TypeInfo>();
        private readonly Dictionary<IModule, TypeInfo> postProcessModules = new Dictionary<IModule, TypeInfo>();

        private Channel<DiscordEvent> eventChannel;
        private Channel<DiscordEvent> voiceEventChannel;

        public DiscordBot(int shard, int totalShards) : base(shard, totalShards)
        {
            foreach (var module in this.Config.Discord.PreProcessModuleTypes)
            {
                var moduleType = Type.GetType(module);
                var instance = Activator.CreateInstance(moduleType) as IModule;
                this.preProcessModules.Add(instance, moduleType.GetTypeInfo());
            }

            foreach (var module in this.Config.Discord.PostProcessModuleTypes)
            {
                var moduleType = Type.GetType(module);
                var instance = Activator.CreateInstance(moduleType) as IModule;
                this.postProcessModules.Add(instance, moduleType.GetTypeInfo());
            }
        }

        protected override string UserId => this.Client.CurrentUser.Id.ToString();

        public DiscordSocketClient Client { get; private set; }

        public readonly ConcurrentDictionary<string, Attachment> ImageUrls = new ConcurrentDictionary<string, Attachment>();
        private readonly ConcurrentDictionary<ITextChannel, List<string>> messageBatch = new ConcurrentDictionary<ITextChannel, List<string>>();

        public override BotType BotType => BotType.Discord;

        protected override async Task StartAsyncInternal()
        {
            if (string.IsNullOrEmpty(this.Config.Discord.Token))
            {
                throw new InvalidConfigException("Discord auth token is missing.");
            }

            this.Client = new DiscordSocketClient(new DiscordSocketConfig
            {
                ShardId = this.Shard,
                TotalShards = this.TotalShards,
                LogLevel = LogSeverity.Debug,
                MessageCacheSize = this.Config.Discord.MessageCacheSize,
                GatewayIntents =
                    GatewayIntents.Guilds |
                    GatewayIntents.GuildMembers |
                    GatewayIntents.GuildBans |
                    GatewayIntents.GuildEmojis |
                    GatewayIntents.GuildVoiceStates |
                    GatewayIntents.GuildMessages |
                    GatewayIntents.GuildMessageReactions |
                    GatewayIntents.DirectMessages
            });

            this.Client.Ready += Client_Ready;
            this.Client.Log += Discord_Log;
            this.Client.Disconnected += (ex) => this.HandleEvent(DiscordEventType.Disconnected, ex);

            this.Client.JoinedGuild += (guild) => this.HandleEvent(DiscordEventType.JoinedGuild, guild);
            this.Client.LeftGuild += (guild) => this.HandleEvent(DiscordEventType.LeftGuild, guild);

            this.Client.UserJoined += (user) => this.HandleEvent(DiscordEventType.UserJoined, user);
            this.Client.UserLeft += (guild, user) => this.HandleEvent(DiscordEventType.UserLeft, guild, user);
            this.Client.UserVoiceStateUpdated += (user, beforeState, afterState) => this.HandleEvent(DiscordEventType.UserVoiceStateUpdated, user, beforeState, afterState);
            this.Client.GuildMemberUpdated += (userBefore, userAfter) => this.HandleEvent(DiscordEventType.GuildMemberUpdated, userBefore, userAfter);
            this.Client.UserBanned += (user, guild) => this.HandleEvent(DiscordEventType.UserBanned, user, guild);

            this.Client.MessageReceived += (message) => this.HandleEvent(DiscordEventType.MessageReceived, message);
            this.Client.MessageUpdated += (messageBefore, messageAfter, channel) => this.HandleEvent(DiscordEventType.MessageUpdated, messageBefore, messageAfter, channel);
            this.Client.MessageDeleted += (message, channel) => this.HandleEvent(DiscordEventType.MessageDeleted, message, channel);
            this.Client.ReactionAdded += (message, channel, reaction) => this.HandleEvent(DiscordEventType.ReactionAdded, message, channel, reaction);

            // Interactions
            this.Client.SlashCommandExecuted += (SocketSlashCommand command) => this.HandleEvent(DiscordEventType.SlashCommand, command);
            this.Client.UserCommandExecuted += (SocketUserCommand command) => this.HandleEvent(DiscordEventType.UserCommand, command);
            this.Client.MessageCommandExecuted += (SocketMessageCommand command) => this.HandleEvent(DiscordEventType.MessageCommand, command);
            this.Client.SelectMenuExecuted += (SocketMessageComponent component) => this.HandleEvent(DiscordEventType.MessageComponent, component);


            this.discordCommands = new Dictionary<string, IDiscordCommand>(StringComparer.OrdinalIgnoreCase);
            foreach (var (command, type) in this.Config.Discord.CommandTypes)
            {
                var commandType = Type.GetType(type);
                var instance = Activator.CreateInstance(commandType) as IDiscordCommand;
                this.discordCommands.Add(command, instance);
            }

            await this.Client.LoginAsync(TokenType.Bot, this.Config.Discord.Token);
            await this.Client.StartAsync();

            this.statsTimer = new Timer(StatsTimerAsync, null, TWELVE_HOURS + this.Shard * FIVE_MINUTES, TWELVE_HOURS * 2);
            this.StartBatchMessageProcessing();

            this.eventChannel = Channel.CreateBounded<DiscordEvent>(new BoundedChannelOptions(this.Config.Discord.EventQueueSize)
            {
                FullMode = BoundedChannelFullMode.Wait
            });

            this.voiceEventChannel = Channel.CreateBounded<DiscordEvent>(new BoundedChannelOptions(this.Config.Discord.VoiceEventQueueSize)
            {
                FullMode = BoundedChannelFullMode.Wait
            });

            Task.Run(() => this.ProcessEvents()).Forget();
        }

        protected override async Task StopAsyncInternal(bool unexpected)
        {
            // explicitly leave all audio channels so that we can say goodbye
            if (!unexpected && this.audioManager != null)
            {
                var audioTask = this.audioManager.LeaveAllAudioAsync();
                var timeoutTask = Task.Delay(20000);
                await Task.WhenAny(audioTask, timeoutTask);
            }

            this.Client.StopAsync().Forget(); // TODO: awaiting this likes to hang -- investigate w/ library
            await Task.Delay(5000);
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);

            this.statsTimer?.Dispose();
            Log.Debug("disposing of client");
            // TODO: Library bug -- investigate hang in DiscordSocketClient.Dispose
            // this.Client?.Dispose();
            Log.Debug("disposing of audio manager");
            this.audioManager?.Dispose();
        }

        /// <inheritdoc />
        protected override async Task<bool> SendNotification(NotificationData notification)
        {
            // if the guild wasn't found, it belongs to another shard.
            if (!(this.Client.GetGuild(Convert.ToUInt64(notification.Server)) is IGuild guild))
            {
                return false;
            }

            Log.Information($"Sending {notification.Type} notification to {notification.Channel} on guild {notification.Server}");

            string extraText = string.Empty;
            var channelToUse = this.Client.GetChannel(Convert.ToUInt64(notification.Channel)) as ITextChannel;

            // if the channel doesn't exist or we don't have send permissions, try to get the default channel instead.
            if (!channelToUse?.GetCurrentUserPermissions().SendMessages ?? true)
            {
                // add some hint text about the misconfigured channel
                string hint = "fix it in the admin panel";
                if (notification.Type == NotificationType.Reminder)
                {
                    hint = "server owner: `.remove timer ###` and recreate it";
                }

                if (channelToUse == null)
                {
                    extraText = $" [Note: the channel configured is missing; {hint}]";
                }
                else
                {
                    extraText = $" [Note: missing permissions for the channel configured; adjust permissions or {hint}]";
                }

                channelToUse = await guild.GetDefaultChannelAsync();

                // if the default channel couldn't be found or we don't have permissions, then we're SOL.
                // flag as processed.
                if (channelToUse == null || !channelToUse.GetCurrentUserPermissions().SendMessages)
                {
                    return true;
                }
            }

            var settings = SettingsConfig.GetSettings(notification.Server);
            // adjust the notification text to disable discord link parsing, if configured to do so
            if (settings.DisableLinkParsing && notification.Type != NotificationType.Reminder)
            {
                notification.Text = Consts.UrlRegex.Replace(notification.Text, new MatchEvaluator((Match urlMatch) =>
                {
                    return $"<{urlMatch.Captures[0]}>";
                }));
            }

            try
            {
                var customText = settings.NotificationText.FirstOrDefault(n => n.Type == notification.Type)?.Text;
                var allowedMentions = notification.AllowMentions ?
                    new AllowedMentions { MentionRepliedUser = false, AllowedTypes = AllowedMentionTypes.Roles | AllowedMentionTypes.Users | AllowedMentionTypes.Everyone } :
                    AllowedMentions.None;

                MessageReference messageReference = null;
                if (!string.IsNullOrEmpty(notification.MessageId) && ulong.TryParse(notification.MessageId, out var messageId))
                {
                    messageReference = new MessageReference(messageId, failIfNotExists: false);
                }

                if (notification.Embed != null && settings.HasFlag(notification.Type))
                {
                    // TODO: discord handles twitter embeds nicely; should adjust the notification data accordingly so we don't need this explicit check here
                    if (notification.Type == NotificationType.Twitter)
                    {
                        var messageText = $"{notification.Embed.Title} {notification.Embed.Url} {customText}{extraText}".Trim();
                        await channelToUse.SendMessageAsync(messageText, allowedMentions: allowedMentions, messageReference: messageReference);
                    }
                    else
                    {
                        var messageText = string.IsNullOrEmpty(notification.Embed.Url) ? string.Empty : $"<{notification.Embed.Url}>";
                        messageText += $" {customText}{extraText}".TrimEnd();
                        await channelToUse.SendMessageAsync(messageText, false, notification.Embed.CreateEmbedBuilder().Build(), allowedMentions: allowedMentions, messageReference: messageReference);
                    }
                }
                else
                {
                    var messageText = $"{notification.Text} {customText}{extraText}".TrimEnd();
                    var sentMesage = await channelToUse.SendMessageAsync(messageText.SubstringUpTo(Discord.DiscordConfig.MaxMessageSize), allowedMentions: allowedMentions, messageReference: messageReference);

                    // update feedback messages to include the message ID
                    if (notification.Type == NotificationType.Feedback && notification.SubType != SubType.Reply)
                    {
                        await sentMesage.ModifyAsync(m => m.Content = $"{sentMesage.Content} (mid: {sentMesage.Id})");
                    }
                }

                var props = new Dictionary<string, string> {
                        { "server", notification.Server },
                        { "channel", notification.Channel },
                        { "notificationType", notification.Type.ToString() },
                };
                this.TrackEvent("notificationSent", props);
            }
            catch (Exception ex)
            {
                // TODO:
                // Add retry support.
                string extraData = null;
                if (notification.Embed != null)
                {
                    extraData = JsonConvert.SerializeObject(notification.Embed);
                }
                Log.Error(ex, $"Failed to send notification {extraData}");
            }

            return true;
        }

        protected override HeartbeatData GetHeartbeatData()
        {
            return new HeartbeatData
            {
                ServerCount = this.Client.Guilds.Count(),
                VoiceChannelCount = this.Client.Guilds.Select(g => g.CurrentUser?.VoiceChannel).Where(v => v != null).Count(),
                UserCount = this.Client.Guilds.Sum(x => x.MemberCount),
                ChannelCount = this.Client.Guilds.Sum(x => x.TextChannels.Count),
            };
        }

        /// <summary>
        /// Timer callback to update various bot stats sites.
        /// </summary>
        /// <param name="state"></param>
        private async void StatsTimerAsync(object state)
        {
            var shardCount = this.TotalShards;
            var guildCount = this.Client.Guilds.Count();
            var shardId = this.Client.ShardId;

            foreach (var botData in this.Config.Discord.BotStats)
            {
                if (botData.Enabled)
                {
                    string endpoint = botData.Endpoint.Replace("{{userId}}", this.UserId);

                    // run token replacements on the known payload's properties
                    foreach (var token in botData.PayloadProps)
                    {
                        switch (token.Key)
                        {
                            case "key":
                                botData.Payload[token.Value] = botData.Key;
                                break;
                            case "shardId":
                                botData.Payload[token.Value] = shardId;
                                break;
                            case "shardCount":
                                botData.Payload[token.Value] = shardCount;
                                break;
                            case "guildCount":
                                botData.Payload[token.Value] = guildCount;
                                break;
                        }
                    }

                    try
                    {
                        var flurlRequest = endpoint.WithTimeout(5).WithHeader("Authorization", botData.Key);
                        
                        if (!string.IsNullOrEmpty(botData.UserAgent))
                        {
                            flurlRequest = flurlRequest.WithHeader("User-Agent", botData.UserAgent);
                        }

                        await flurlRequest.PostJsonAsync((object)botData.Payload);
                    }
                    catch (Exception ex)
                    {
                        Log.Warning(ex, $"Failed to update {botData.Name} stats");
                    }
                }
            }
        }

        private void BatchSendMessageAsync(ITextChannel channel, string text, ModOptions modlogType)
        {
            Log.Information($"Added batch message for type: {modlogType} on channel {channel.Id}");
            messageBatch.AddOrUpdate(channel, new List<string> { text }, (ITextChannel key, List<string> val) =>
            {
                val.Add(text);
                return val;
            });
        }

        private void StartBatchMessageProcessing()
        {
            Task.Run(async () =>
            {
                while (true)
                {
                    try
                    {
                        // copy the current channels
                        ITextChannel[] channels = messageBatch.Keys.ToArray();

                        foreach (var channel in channels)
                        {
                            messageBatch.TryRemove(channel, out var messages);

                            string messageToSend = string.Empty;
                            foreach (var message in messages)
                            {
                                if (messageToSend.Length + message.Length + 1 < Consts.MaxMessageLength)
                                {
                                    messageToSend += message + "\n";
                                }
                                else
                                {
                                    if (!string.IsNullOrEmpty(messageToSend))
                                    {
                                        await channel.SendMessageAsync(messageToSend, allowedMentions: AllowedMentions.None);
                                    }

                                    if (message.Length == Consts.MaxMessageLength)
                                    {
                                        messageToSend = message;
                                    }
                                    else
                                    {
                                        messageToSend = message + "\n";
                                    }
                                }
                            }

                            if (!string.IsNullOrWhiteSpace(messageToSend))
                            {
                                await channel.SendMessageAsync(messageToSend, allowedMentions: AllowedMentions.None);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Error(ex, "Batch processing error");
                    }

                    await Task.Delay(10000);
                }
            }).Forget();
        }

        private bool IsAuthorOwner(IUserMessage message)
        {
            return IsAuthorOwner(message.Author);
        }

        private bool IsAuthorOwner(IUser author)
        {
            return author.Id == this.Config.Discord?.OwnerId;
        }

        private bool IsAuthorPatron(ulong userId)
        {
            return this.Config.Discord.Patrons.Contains(userId);
        }
    }
}
