using Fluxer.Net;
using Fluxer.Net.Rest;
using Serilog;
using Serilog.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using UB3RB0T.Commands;
using UB3RB0T.Modules;
using UB3RB0T.Modules.FluxerModules;

namespace UB3RB0T
{
    public class FluxerBot : Bot
    {
        public readonly FluxerClient client;
        private readonly MessageCache<ulong, ulong> botResponsesCache = new();

        private readonly Timer heartbeatTimer;
        private DateTime lastHeartbeatAck = DateTime.MinValue;

        private Dictionary<string, IFluxerCommand> fluxerCommands;
        private readonly Dictionary<IModule, TypeInfo> preProcessModules = [];
        private FluxerBotConfig fluxerConfig => this.Config.Fluxer!;

        public FluxerBot(int shard, int totalShards) : base(shard, totalShards)
        {
            var fluxerConfig = new FluxerConfig
            {
                RestSerilog = Log.Logger as Logger,
                GatewaySerilog = Log.Logger as Logger,
            };

            if (this.Config.Fluxer == null)
            {
                throw new InvalidConfigException("Fluxer configuration is missing.");
            }

            if (string.IsNullOrEmpty(this.fluxerConfig.Token))
            {
                throw new InvalidConfigException("Fluxer auth token is missing.");
            }

            this.client = new FluxerClient(this.fluxerConfig.Token, fluxerConfig);

            this.client.Gateway.MessageCreated += async data => await this.HandleMessageCreated(data);
            this.client.Gateway.MessageUpdated += async data => await this.HandleMessageUpdated(data);
            this.client.Gateway.MessageDeleted += async (channel, userId, messageId, content) => await this.HandleMessageDeleted(channel, userId, messageId, content);
            this.client.Gateway.MessageReactionAdded += async (channel, message, reaction) => await this.HandleReactionAdded(channel, message, reaction);
            this.client.Gateway.MemberJoined += async data => await this.HandleMemberJoined(data);
            this.client.Gateway.MemberLeft += async (guild, data) => await this.HandleMemberLeft(guild, data);

            this.client.Gateway.HeartbeatAck += this.HeartbeatAck;

            this.heartbeatTimer = new Timer(HeartBeatTimerAsync, null, 60000, 60000 * 5);

            // TODO: config driven
            this.fluxerCommands = new Dictionary<string, IFluxerCommand>(StringComparer.OrdinalIgnoreCase)
            {
                { "admin", new FluxerAdminCommand() },
                { "debug", new FluxerDebugCommand() },
            };

            // TODO: config driven
            this.preProcessModules = new Dictionary<IModule, TypeInfo>
            {
                { new FluxerWordCensorModule(), typeof(FluxerWordCensorModule).GetTypeInfo() },
            };
        }

        public override BotType BotType => BotType.Fluxer;
        public override bool SupportsSettings => true;
        protected override string UserId => this.fluxerConfig.BotId.ToString();

        protected override async Task<HeartbeatData> GetHeartbeatData()
        {
            List<Guild>? guilds = null;
            try
            {
                guilds = (await this.client.Rest.GetCurrentUserGuildsAsync()).ToList();
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to fetch guilds for heartbeat data");
            }

            var heartbeatData = new HeartbeatData
            {
                ServerCount = guilds?.Count ?? 0,
                ChannelCount = 0,
            };

            return heartbeatData;
        }

        protected override async Task RespondAsync(BotMessageData messageData, string text)
        {
            if (messageData.FluxerMessageData != null)
            {
                await this.RespondAsync(messageData.FluxerMessageData, text);
            }
        }

        private async Task HandleMemberJoined(SocketGuildMember data)
        {
            var guildId = data.GuildId;
            var userId = data.User.Id;
            var settings = SettingsConfig.GetSettings(guildId);

            if (settings.Greetings.Count > 0 && settings.GreetingId != 0)
            {
                var greeting = settings.Greetings.Random().Replace("%user%", data.Mention);
                greeting = greeting.Replace("%username%", $"{data}");

                greeting = Consts.ChannelRegex.Replace(greeting, new MatchEvaluator((Match chanMatch) =>
                {
                    string channelName = chanMatch.Groups[1].Value;
                    var channel = data.Guild.Channels.FirstOrDefault(c => c.Value.Type is ChannelType.GuildText && channelName.Equals(c.Value.Name, StringComparison.OrdinalIgnoreCase)).Value;
                    return channel?.Mention ?? $"#{channelName}";
                }));

                var greetingChannel = this.client.Gateway.GetChannel(settings.GreetingId) ?? data.Guild.SystemChannel;
                if (greetingChannel != null && data.Guild.CurrentMember.GetPermissions(greetingChannel).SendMessages)
                {
                    await greetingChannel.SendMessageAsync(greeting);
                }
                else
                {
                    if (settings.DebugMode)
                    {
                        Log.Verbose($"[DBGM] [guild: {guildId}] User joined, missing permissions to send greeting");
                    }
                }
            }

            if (settings.JoinRoleId != 0 && data.Guild.CurrentMember.GuildPermissions.ManageRoles)
            {
                var role = data.Guild.GetRole(settings.JoinRoleId);
                if (role != null)
                {
                    try
                    {
                        await data.AddRoleAsync(role);
                    }
                    catch (Exception ex)
                    {
                        Log.Error(ex, $"[guild: {guildId}] Failed to add join role {settings.JoinRoleId} to user {userId}");
                    }
                }
                else
                {
                    Log.Verbose($"[guild: {guildId}] User joined, role id {settings.JoinRoleId} not found");
                }
            }

            // mod log
            if (settings.Mod_LogId != 0 && settings.HasFlag(ModOptions.Mod_LogUserJoin))
            {
                string joinText = $"{data.Mention} ({data}) joined.";
                var channel = data.Guild.GetChannel(settings.Mod_LogId);
                if (channel != null)
                {
                    var canSendMessages = data.Guild.CurrentMember.GetPermissions(channel).SendMessages;
                    if (canSendMessages)
                    {
                        await channel.SendMessageAsync(joinText);
                    }
                    else
                    {
                        Log.Verbose($"[guild: {guildId}] User joined, missing permissions to send mod log");
                    }
                }
                else
                {
                    Log.Verbose($"[guild: {guildId}] User joined, mod log channel id {settings.Mod_LogId} not found");
                }
            }
        }

        private async Task HandleMemberLeft(SocketGuild guild, Cacheable<SocketGuildMember> member)
        {
            if (guild != null)
            {
                var settings = SettingsConfig.GetSettings(guild.Id);

                if (settings.Farewells.Count > 0 && settings.FarewellId != 0)
                {
                    var farewell = settings.Farewells.Random().Replace("%user%", member.Value?.Mention ?? member.Id.ToString());
                    farewell = farewell.Replace("%username%", $"{member.Value?.GetCurrentName() ?? member.Id.ToString()}");

                    farewell = Consts.ChannelRegex.Replace(farewell, new MatchEvaluator((Match chanMatch) =>
                    {
                        string channelName = chanMatch.Groups[1].Value;
                        var channel = guild.Channels.FirstOrDefault(c => c.Value.Type is ChannelType.GuildText && channelName.Equals(c.Value.Name, StringComparison.OrdinalIgnoreCase)).Value;
                        return channel?.Mention ?? $"#{channelName}";
                    }));

                    var farewellChannel = this.client.Gateway.GetChannel(settings.FarewellId) ?? guild.SystemChannel;
                    if (farewellChannel != null && guild.CurrentMember.GetPermissions(farewellChannel).SendMessages)
                    {
                        await farewellChannel.SendMessageAsync(farewell);
                    }
                    else
                    {
                        if (settings.DebugMode)
                        {
                            Log.Verbose($"[DBGM] [guild: {guild.Id}] User left, missing permissions to send farewell");
                        }
                    }
                }

                var guildId = guild.Id;

                // mod log
                if (settings.Mod_LogId != 0 && settings.HasFlag(ModOptions.Mod_LogUserLeave))
                {
                    string leaveText = $"{member.Value?.Mention ?? member.Id.ToString()} ({member.Value?.GetCurrentName() ?? member.Id.ToString()}) left.";
                    var channel = guild.GetChannel(settings.Mod_LogId);
                    if (channel != null)
                    {
                        var canSendMessages = guild.CurrentMember.GetPermissions(channel).SendMessages;
                        if (canSendMessages)
                        {
                            await channel.SendMessageAsync(leaveText);
                        }
                        else
                        {
                            Log.Verbose($"[guild: {guildId}] User left, missing permissions to send mod log");
                        }
                    }
                    else
                    {
                        Log.Verbose($"[guild: {guildId}] User left, mod log channel id {settings.Mod_LogId} not found");
                    }
                }
            }
        }

        private async Task HandleReactionAdded(Channel channel, Cacheable<Message> message, SocketReaction reaction)
        {
            if (reaction.UserId == this.fluxerConfig.BotId)
            {
                return;
            }

            if (channel.GuildId == null)
            {
                return;
            }

            var settings = SettingsConfig.GetSettings(channel.GuildId.Value);

            var reactionEmote = reaction.Emoji.Name;

            if (reactionEmote == "💬" || reactionEmote == "🗨️")
            {
                var reactionMessage = await this.client.Rest.GetMessageAsync(channel.Id, message.Id);
                // if the reaction already exists, don't re-process.
                if (reactionMessage == null || reactionMessage.Author.IsBot || reactionMessage.Reactions?.Any(r => (r.Emoji.Name == "💬" || r.Emoji.Name == "🗨️") && r.Count > 1) == true)
                {
                    return;
                }

                if (!string.IsNullOrEmpty(reactionMessage.Content))
                {
                    var reactionUser = await this.client.Rest.GetUserAsync(reaction.UserId);
                    await this.HandleMessageCreated(reactionMessage, reactionEmote, reactionUser);
                }
            }
            else if (reactionEmote == "🏅")
            {
                var reactionMessage = await this.client.Rest.GetMessageAsync(channel.Id, message.Id);

                if (reactionMessage == null || reactionMessage.Author.IsBot)
                {
                    return;
                }

                if (!string.IsNullOrEmpty(reactionMessage.Content))
                {
                    var reactionUser = await this.client.Rest.GetUserAsync(reaction.UserId);
                    await this.HandleMessageCreated(reactionMessage, reactionEmote, reactionUser);
                }
            }
            else if (channel.Id == settings.SelfRolesChannelId)
            {
                var mappedEmote = reaction.Emoji.Id == 0 ? reaction.Emoji.Name : reaction.Emoji.Id.ToString();
                ulong selfRoleId = settings.SelfRoles.FirstOrDefault(kvp => kvp.Value == mappedEmote).Key;
                bool roleChanged = false;

                if (selfRoleId != 0 && reaction.Member != null)
                {

                    if (reaction.Member.RoleIds.Contains(selfRoleId))
                    {
                        roleChanged = true;
                        await reaction.Member.RemoveRoleAsync(selfRoleId);
                    }
                    else
                    {
                        roleChanged = true;
                        await this.client.Rest.AddMemberRoleAsync(channel.GuildId.Value, reaction.Member.Id, selfRoleId);
                    }   

                    if (roleChanged)
                    {
                        var reactionMessage = await this.client.Rest.GetMessageAsync(channel.Id, message.Id);
                        if (reactionMessage != null && reaction.Emoji is Emoji emoji)
                        {
                            await reactionMessage.RemoveUserReactionAsync(emoji, reaction.Member.User);
                        }
                    }
                }
            }
        }

        private async Task HandleMessageCreated(Message message, string? reactionEmote = null, User? reactionUser = null)
        {
            try
            {
                if (message.Author.Id == this.fluxerConfig.BotId)
                {
                    return;
                }

                ulong? guildId = 0;
                if (message is SocketMessage socketMessage)
                {
                    guildId = socketMessage.Guild?.Id;
                }
                else
                {
                    guildId = this.client.Gateway.GetChannel(message.ChannelId)?.GuildId;
                }

                if (this.Throttler.IsThrottled(message.Author.Id.ToString(), ThrottleType.User))
                {
                    Log.Debug($"messaging throttle from user: {message.Author} on chan {message.ChannelId} server {guildId}");
                    return;
                }

                if (guildId != null && this.Throttler.IsThrottled($"{guildId}", ThrottleType.Guild))
                {
                    Log.Debug($"messaging throttle from guild: {message.Author} on chan {message.ChannelId} server {guildId}");
                    return;
                }

                Log.Debug($"Received message from {message.Author} in channel {message.ChannelId} on server {guildId}");

                var settings = new Settings
                {
                    FunResponsesEnabled = true,
                    AutoTitlesEnabled = true,
                    PreferEmbeds = true,
                };

                if (guildId != null)
                {
                    settings = SettingsConfig.GetSettings(guildId.Value);
                }

                var messageData = BotMessageData.Create(message, settings, guildId);
                var context = new FluxerBotContext(this.client, messageData)
                {
                    ReactionUser = reactionUser
                };

                foreach (var (module, typeInfo) in this.preProcessModules)
                {
                    var permissionChecksPassed = await this.CheckPermissions(context, typeInfo);

                    if (!permissionChecksPassed)
                    {
                        continue;
                    }

                    var result = await module.Process(context);
                    if (result == ModuleResult.Stop)
                    {
                        return;
                    }
                }

                if (reactionEmote == "💬" || reactionEmote == "🗨️")
                {
                    var quote = messageData.Content?.ReplaceMulti(["\"", "”", "“"], "&quot;");
                    messageData.Content = $"{settings.Prefix}quote add \"{quote}\" - userid:{message.Author.Id} {message.Author.Username}";
                    await message.AddReactionAsync("💬");
                }
                else if (reactionEmote == "🏅")
                {
                    messageData.Content = $"{settings.Prefix}rep add {message.Author.Id}";
                    messageData.UserId = reactionUser?.Id.ToString() ?? string.Empty;
                    messageData.TargetUserId = message.Author.Id.ToString();
                }

                await this.PreProcessMessage(messageData, settings);

                string command = messageData.Command ?? string.Empty;

                var commandHandled = false;
                if (this.fluxerCommands.TryGetValue(command, out IFluxerCommand? fluxerCommand))
                {
                    var typeInfo = fluxerCommand.GetType().GetTypeInfo();
                    var permissionChecksPassed = await this.CheckPermissions(context, typeInfo);

                    if (permissionChecksPassed)
                    {
                        var response = await fluxerCommand.Process(context);
                        if (!string.IsNullOrEmpty(response?.Text) || response?.Embed != null)
                        {
                            await this.RespondAsync(messageData, response.Text);
                            commandHandled = true;
                        }
                    }
                }

                if (!commandHandled)
                {
                    BotResponseData responseData = await this.ProcessMessageAsync(messageData, settings);

                    if (responseData.Embed != null)
                    {
                        await this.RespondAsync(message, string.Empty, embed: responseData.Embed.CreateFluxerEmbed());
                    }
                    else
                    {
                        foreach (string response in responseData.Responses)
                        {
                            await this.RespondAsync(message, response, bypassEdit: responseData.Responses.Count > 1);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Warning(ex, $"Error in HandleMessageCreated");
            }
        }
        private async Task<bool> CheckPermissions(IFluxerBotContext context, TypeInfo typeInfo)
        {
            var attributes = typeInfo.GetCustomAttributes().Where(a => a is FluxerPermissionsAttribute);
            var attributeChecksPassed = true;
            foreach (FluxerPermissionsAttribute attr in attributes)
            {   
                if (!await attr.CheckPermissions(context))
                {
                    if (!string.IsNullOrEmpty(attr.FailureString) && context.Settings.GetString(attr.FailureString) is string failureMessage)
                    {
                        await this.RespondAsync(context.MessageData, failureMessage);
                    }

                    attributeChecksPassed = false;
                }
            }

            return attributeChecksPassed;
        }

        private async Task HandleMessageUpdated(SocketMessage message)
        {
            try
            {
                if (DateTimeOffset.UtcNow.Subtract(message.CreatedAt) < TimeSpan.FromHours(1) && !string.IsNullOrEmpty(message.Content))
                {
                    await this.HandleMessageCreated(message);
                }
            }
            catch (Exception ex)
            {
                Log.Warning(ex, $"Error in HandleMessageUpdated");
            }
        }

        private async Task HandleMessageDeleted(Channel channel, ulong? userId, ulong messageId, string? content)
        {
            try
            {
                if (messageId == 0 || channel == null)
                {
                    return;
                }

                var botMessageId = this.botResponsesCache.Remove(messageId);
                if (botMessageId != 0)
                {
                    await this.client.Rest.DeleteMessageAsync(channel.Id, botMessageId);
                }
            }
            catch (Exception ex)
            {
                Log.Warning(ex, $"Error in HandleMessageDeleted");
            }
        }

        private async Task RespondAsync(IMessage messageData, string text, bool bypassEdit = false, EmbedRequest? embed = null)
        {
            this.TrackEvent("messageSent");
            List<EmbedRequest>? embeds = null;
            if (embed != null)
            {
                embeds = [embed];
            }

            var message = new MessageRequest
            {
                MessageReference = new MessageReferenceRequest
                {
                    MessageId = messageData.Id,
                    ChannelId = messageData.ChannelId,
                },
                Content = text,
            };

            if (!bypassEdit && this.botResponsesCache.Get(messageData.Id) is ulong oldMessageId && oldMessageId != 0)
            {
                try
                {
                    var oldMessage = await this.client.Rest.GetMessageAsync(messageData.ChannelId, oldMessageId);

                    if (oldMessage != null)
                    {
                        List<EmbedRequest>? editEmbeds = null;
                        if (embed != null)
                        {
                            var embedRequest = new EmbedRequest
                            {
                                Title = embed?.Title,
                                Description = embed?.Description,
                                Url = embed?.Url,
                                Color = embed?.Color,
                                Author = embed?.Author != null ? new EmbedAuthorRequest
                                {
                                    Name = embed.Author.Name,
                                    Url = embed.Author.Url,
                                    IconUrl = embed.Author.IconUrl,
                                } : null,
                                Image = embed?.Image != null ? new EmbedMediaRequest
                                {
                                    Url = embed.Image.Url,
                                } : null,
                                Thumbnail = embed?.Thumbnail != null ? new EmbedMediaRequest
                                {
                                    Url = embed.Thumbnail.Url,
                                } : null,
                                Footer = embed?.Footer != null ? new EmbedFooterRequest
                                {
                                    Text = embed.Footer.Text,
                                    IconUrl = embed.Footer.IconUrl,
                                } : null,
                                Fields = embed?.Fields?.Select(f => new EmbedFieldRequest
                                {
                                    Name = f.Name,
                                    Value = f.Value,
                                    IsInline = f.IsInline,
                                }).ToArray(),
                            };

                            editEmbeds = [embedRequest];
                        }

                        await this.client.Rest.EditMessageAsync(oldMessage.ChannelId, oldMessageId, message.Content, editEmbeds);
                        return;
                    }
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, $"Failed to fetch old message for edit, it may have already been deleted");
                }
            }

            var sentMessage = await this.client.Rest.SendMessageAsync(messageData.ChannelId, message.Content, embeds, message.MessageReference);

            this.botResponsesCache.Add(messageData.Id, sentMessage.Id);
        }

        protected override async Task<bool> SendNotification(NotificationData notification)
        {
            Message? originalMessage = null;
            if (!string.IsNullOrEmpty(notification.MessageId) && ulong.TryParse(notification.Channel, out var channelId) && ulong.TryParse(notification.MessageId, out var messageId))
            {
                try
                {
                    originalMessage = await this.client.Rest.GetMessageAsync(channelId, messageId);
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Failed to fetch notification original message");
                }
            }

            var message = new MessageRequest
            {
                Content = notification.Text,
                MessageReference = originalMessage != null ? new MessageReferenceRequest
                {
                    MessageId = originalMessage.Id,
                    ChannelId = originalMessage.ChannelId,
                } : null,
            };
            
            if (notification.Channel != null && ulong.TryParse(notification.Channel, out channelId))
            {
                await this.client.Rest.SendMessageAsync(channelId, message.Content, reference: message.MessageReference);
            }

            return true;
        }

        protected override async Task StartAsyncInternal()
        {
            var gatewayConfig = new FluxerGatewayConfig
            {
                IgnoredEvents = ["PRESENCE_UPDATE", "TYPING_START"]
            };

            await this.client.Gateway.ConnectAsync(gatewayConfig);
        }

        protected override Task StopAsyncInternal(bool unexpected)
        {
            this.client.Gateway.Dispose();
            return Task.CompletedTask;
        }

        private void HeartbeatAck()
        {
            Log.Debug("Received heartbeat ACK from Fluxer");
            lastHeartbeatAck = DateTime.UtcNow;
        }

        private async void HeartBeatTimerAsync(object? state)
        {
            if (DateTime.UtcNow.Subtract(lastHeartbeatAck) > TimeSpan.FromMinutes(10))
            {
                Log.Warning("No heartbeat ACK received from Fluxer in the last 10 minutes, reconnecting...");

                try
                {
                    if (this.Config.AlertEndpoint != null)
                    {
                        string messageContent = $"\U0001F501 {this.BotType} triggered automatic restart due to inactivity";
                        try
                        {
                            await this.Config.AlertEndpoint.PostJsonAsync(new { content = messageContent });
                        }
                        catch (Exception ex)
                        {
                            Log.Error(ex, "Failed to send alert message to endpoint");
                        }
                    }

                    lastHeartbeatAck = DateTime.UtcNow;
                    await this.client.Gateway.ConnectAsync();
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Error while reconnecting to Fluxer");
                }
            }
            else
            {
                Log.Debug("Heartbeat ACK received recently, no need to reconnect");
            }
        }
    }
}
