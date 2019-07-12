
namespace UB3RB0T
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text.RegularExpressions;
    using System.Threading;
    using System.Threading.Tasks;
    using Discord;
    using Discord.WebSocket;
    using Flurl.Http;
    using Newtonsoft.Json;
    using UB3RB0T.Commands;
    using Serilog;

    public partial class DiscordBot : Bot
    {
        private AudioManager audioManager = new AudioManager();
        private Timer statsTimer;
        private Dictionary<string, IDiscordCommand> discordCommands;

        private List<IModule> modules;

        private BlockingCollection<DiscordEvent> eventQueue = new BlockingCollection<DiscordEvent>();
        private BlockingCollection<DiscordEvent> voiceEventQueue = new BlockingCollection<DiscordEvent>();

        private SemaphoreSlim eventProcessLock;
        private SemaphoreSlim voiceEventProcessLock;

        public DiscordBot(int shard, int totalShards) : base(shard, totalShards)
        {
            // TODO: Add these via reflection processing or config instead of this nonsense
            // order matters
            this.modules = new List<IModule>
            {
                new WordCensorModule(),
                new BotlessModule(),
                new FaqModule(),
                new OcrModule(),
            };
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
                LogLevel = LogSeverity.Verbose,
                MessageCacheSize = 25,
                ExclusiveBulkDelete = false
            });

            this.Client.Ready += Client_Ready;
            this.Client.Log += Discord_Log;
            this.Client.Disconnected += (ex) => this.HandleEvent(DiscordEventType.Disconnected, ex);

            this.Client.JoinedGuild += (guild) => this.HandleEvent(DiscordEventType.JoinedGuild, guild);
            this.Client.LeftGuild += (guild) => this.HandleEvent(DiscordEventType.LeftGuild, guild);

            this.Client.UserJoined += (user) => this.HandleEvent(DiscordEventType.UserJoined, user);
            this.Client.UserLeft += (user) => this.HandleEvent(DiscordEventType.UserLeft, user);
            this.Client.UserVoiceStateUpdated += (user, beforeState, afterState) => this.HandleEvent(DiscordEventType.UserVoiceStateUpdated, user, beforeState, afterState);
            // This fires *a lot* and acted upon infrequently;
            // TOOD: re-evaluate this approach of generalizing event handling to Task.Run() everything...
            this.Client.GuildMemberUpdated += (userBefore, userAfter) => this.HandleGuildMemberUpdated(userBefore, userAfter);
            this.Client.UserBanned += (user, guild) => this.HandleEvent(DiscordEventType.UserBanned, user, guild);

            this.Client.MessageReceived += (message) => this.HandleEvent(DiscordEventType.MessageReceived, message);
            this.Client.MessageUpdated += (messageBefore, messageAfter, channel) => this.HandleEvent(DiscordEventType.MessageUpdated, messageBefore, messageAfter, channel);
            this.Client.MessageDeleted += (message, channel) => this.HandleEvent(DiscordEventType.MessageDeleted, message, channel);
            this.Client.ReactionAdded += (message, channel, reaction) => this.HandleEvent(DiscordEventType.ReactionAdded, message, channel, reaction);

            // TODO: Add these via reflection processing or config instead of this nonsense
            this.discordCommands = new Dictionary<string, IDiscordCommand>(StringComparer.OrdinalIgnoreCase)
            {
                { "debug", new DebugCommand() },
                { "seen", new SeenCommand() },
                { "remove", new RemoveCommand() },
                { "clear", new ClearCommand() },
                { "status", new StatusCommand() },
                { "voice", new VoiceJoinCommand() },
                { "dvoice", new VoiceLeaveCommand() },
                { "devoice", new VoiceLeaveCommand() },
                { "captain_planet", new CaptainCommand() },
                { "jpeg", new JpegCommand() },
                { "userinfo", new UserInfoCommand() },
                { "serverinfo", new ServerInfoCommand() },
                { "roles", new RolesCommand() },
                { "admin", new AdminCommand() },
                { "eval", new EvalCommand() },
                { "quickpoll", new QuickPollCommand() },
                { "qp", new QuickPollCommand() },
                { "role", new RoleCommand(true) },
                { "derole", new RoleCommand(false) },
                { "fr", new FeedbackCommand() },
            };

            // Token validation bug; suppress validation for now. Fixed in upcoming discord.net build
            await this.Client.LoginAsync(TokenType.Bot, this.Config.Discord.Token, validateToken: false);
            await this.Client.StartAsync();

            this.statsTimer = new Timer(StatsTimerAsync, null, 3600000 + this.Shard * 120000, 7200000);
            this.StartBatchMessageProcessing();

            this.eventProcessLock = new SemaphoreSlim(this.Config.Discord.EventQueueSize, this.Config.Discord.EventQueueSize);
            this.voiceEventProcessLock = new SemaphoreSlim(this.Config.Discord.VoiceEventQueueSize, this.Config.Discord.VoiceEventQueueSize);
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

        public override void Dispose(bool disposing)
        {
            base.Dispose(disposing);

            this.statsTimer?.Dispose();
            Log.Debug("disposing of client");
            this.Client?.Dispose();
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

                channelToUse = await guild.GetDefaultChannelAsync() as ITextChannel;

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
                if (notification.Embed != null && settings.HasFlag(notification.Type))
                {
                    // TODO: discord handles twitter embeds nicely; should adjust the notification data accordingly so we don't need this explicit check here
                    if (notification.Type == NotificationType.Twitter)
                    {
                        await channelToUse.SendMessageAsync(notification.Embed.Url);
                    }
                    else
                    {
                        var customText = settings.NotificationText.FirstOrDefault(n => n.Type == notification.Type)?.Text;
                        var messageText = string.IsNullOrEmpty(notification.Embed.Url) ? string.Empty : $"<{notification.Embed.Url}>";
                        if (!string.IsNullOrEmpty(customText))
                        {
                            messageText += $" {customText}{extraText}";
                        }

                        await channelToUse.SendMessageAsync(messageText, false, notification.Embed.CreateEmbedBuilder().Build());
                    }
                }
                else
                {
                    var messageText = $"{notification.Text}{extraText}";
                    await channelToUse.SendMessageAsync(messageText.SubstringUpTo(Discord.DiscordConfig.MaxMessageSize));
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
                UserCount = this.Client.Guilds.Sum(x => x.Users.Count),
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
                        await endpoint.WithTimeout(5)
                            .WithHeader("Authorization", botData.Key)
                            .PostJsonAsync((object)botData.Payload);
                    }
                    catch (Exception ex)
                    {
                        Log.Warning(ex, $"Failed to update {botData.Name} stats");
                    }
                }
            }
        }

        private void BatchSendMessageAsync(ITextChannel channel, string text)
        {
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
                                        await channel.SendMessageAsync(messageToSend);
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
                                await channel.SendMessageAsync(messageToSend);
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

        private bool IsAuthorOwner(SocketUserMessage message)
        {
            return message.Author.Id == this.Config.Discord?.OwnerId;
        }

        private bool IsAuthorPatron(ulong userId)
        {
            return this.Config.Discord.Patrons.Contains(userId);
        }
    }
}
