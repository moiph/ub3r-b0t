
namespace UB3RB0T
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Net;
    using System.Reflection;
    using System.Text.RegularExpressions;
    using System.Threading.Tasks;
    using Discord;
    using Discord.Net;
    using Discord.WebSocket;
    using Serilog;
    using UB3RB0T.Commands;

    public partial class DiscordBot
    {
        private readonly MessageCache botResponsesCache = new MessageCache();
        private bool isReady;

        /// <summary>
        /// Wraps the actual event handler callbacks with a task.run
        /// </summary>
        /// <param name="eventType">The event being handled.</param>
        /// <param name="args">The event arguments.</param>
        /// <returns></returns>
        private Task HandleEvent(DiscordEventType eventType, params object[] args)
        {
            var discordEvent = new DiscordEvent
            {
                EventType = eventType,
                Args = args,
            };

            if (eventType == DiscordEventType.UserVoiceStateUpdated)
            {
                this.voiceEventQueue.Add(discordEvent);
            }
            else
            {
                this.eventQueue.Add(discordEvent);
            }

            return Task.CompletedTask;
        }

        /// <summary>
        /// Processes events in the queue.
        /// </summary>
        public async Task ProcessEvents()
        {
            // Kick off voice event processing separately
            Task.Run(() => this.ProcessVoiceEvents()).Forget();

            foreach (var eventToProcess in this.eventQueue.GetConsumingEnumerable())
            {
                await this.eventProcessLock.WaitAsync();

                Task.Run(async () =>
                {
                    var eventType = eventToProcess.EventType;
                    var args = eventToProcess.Args;
                    try
                    {
                        switch (eventType)
                        {
                            case DiscordEventType.Disconnected:
                                await this.HandleDisconnected(args[0] as Exception);
                                break;
                            case DiscordEventType.JoinedGuild:
                                await this.HandleJoinedGuildAsync((SocketGuild)args[0]);
                                break;
                            case DiscordEventType.LeftGuild:
                                await this.HandleLeftGuildAsync((SocketGuild)args[0]);
                                break;
                            case DiscordEventType.UserJoined:
                                await this.HandleUserJoinedAsync((SocketGuildUser)args[0]);
                                break;
                            case DiscordEventType.UserLeft:
                                await this.HandleUserLeftAsync((SocketGuildUser)args[0]);
                                break;
                            case DiscordEventType.GuildMemberUpdated:
                                await this.HandleGuildMemberUpdated(args[0] as SocketGuildUser, args[1] as SocketGuildUser);
                                break;
                            case DiscordEventType.UserBanned:
                                await this.HandleUserBanned(args[0] as SocketGuildUser, (SocketGuild)args[1]);
                                break;
                            case DiscordEventType.MessageReceived:
                                await this.HandleMessageReceivedAsync((SocketMessage)args[0]);
                                break;
                            case DiscordEventType.MessageUpdated:
                                await this.HandleMessageUpdated((Cacheable<IMessage, ulong>)args[0], (SocketMessage)args[1], (ISocketMessageChannel)args[2]);
                                break;
                            case DiscordEventType.MessageDeleted:
                                await this.HandleMessageDeleted((Cacheable<IMessage, ulong>)args[0], (ISocketMessageChannel)args[1]);
                                break;
                            case DiscordEventType.ReactionAdded:
                                await this.HandleReactionAdded((Cacheable<IUserMessage, ulong>)args[0], (ISocketMessageChannel)args[1], (SocketReaction)args[2]);
                                break;
                            default:
                                throw new ArgumentException("Unrecognized event type");
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Warning(ex, $"Error in {eventType} handler");
                        this.AppInsights?.TrackException(ex);
                    }
                    finally
                    {
                        this.eventProcessLock.Release();
                    }
                }).Forget();
            }
        }

        /// <summary>
        /// Processes voice events in the queue.
        /// </summary>
        private async Task ProcessVoiceEvents()
        {
            foreach (var eventToProcess in this.voiceEventQueue.GetConsumingEnumerable())
            {
                await this.voiceEventProcessLock.WaitAsync();

                Task.Run(async () =>
                {
                    var args = eventToProcess.Args;
                    try
                    {
                        await this.HandleUserVoiceStateUpdatedAsync((SocketUser)args[0], (SocketVoiceState)args[1], (SocketVoiceState)args[2]);
                    }
                    catch (Exception ex)
                    {
                        Log.Warning(ex, $"Error in {eventToProcess.EventType} handler");
                        this.AppInsights?.TrackException(ex);
                    }
                    finally
                    {
                        this.voiceEventProcessLock.Release();
                    }
                }).Forget();
            }
        }

        //
        // Event handler core logic
        //

        private Task Client_Ready()
        {
            this.isReady = true;
            this.Client.SetGameAsync(this.Config.Discord.Status);
            return Task.CompletedTask;
        }

        private Task Discord_Log(LogMessage logMessage)
        {
            // TODO: Temporary filter for audio warnings; remove with future Discord.NET update
            if (logMessage.Message != null && logMessage.Message.Contains("Unknown OpCode") || 
                (logMessage.Source != null && logMessage.Source.Contains("Audio") && logMessage.Message != null && logMessage.Message.Contains("Latency = ")))
            {
                return Task.CompletedTask;
            }

            var logMessageText = logMessage.ToString(prependTimestamp: false);

            switch (logMessage.Severity)
            {
                case LogSeverity.Critical:
                    Log.Fatal(logMessageText);
                    break;
                case LogSeverity.Error:
                    Log.Error(logMessageText);
                    break;
                case LogSeverity.Warning:
                    Log.Warning(logMessageText);
                    break;
                case LogSeverity.Info:
                    Log.Information(logMessageText);
                    break;
                case LogSeverity.Debug:
                    Log.Debug(logMessageText);
                    break;
                case LogSeverity.Verbose:
                    Log.Verbose(logMessageText);
                    break;
            }

            if (logMessage.Exception != null)
            {
                this.AppInsights?.TrackException(logMessage.Exception);
            }

            return Task.CompletedTask;
        }

        private Task HandleDisconnected(Exception ex)
        {
            Log.Warning(ex, "Disconnected");
            return Task.CompletedTask;
        }

        /// <summary>
        /// Handles joining a guild and announcing such.
        /// </summary>
        private async Task HandleJoinedGuildAsync(SocketGuild guild)
        {
            if (this.isReady)
            {
                this.TrackEvent("serverJoin");

                // if it's a bot farm, bail out.
                await guild.DownloadUsersAsync();

                var botCount = guild.Users.Count(u => u.IsBot);
                var botRatio = (double)botCount / guild.Users.Count;
                if (botCount > 30 && botRatio > .5)
                {
                    Log.Warning($"Auto bailed on a bot farm: {guild.Name} (#{guild.Id})");
                    await guild.LeaveAsync();
                    return;
                }

                var defaultChannel = guild.DefaultChannel;
                var owner = guild.Owner;
                if (defaultChannel != null && guild.CurrentUser.GetPermissions(defaultChannel).SendMessages)
                {
                    await defaultChannel.SendMessageAsync($"(HELLO, I AM UB3R-B0T! .halp for info. {owner.Mention} you're the kickass owner-- you can use .admin to configure some stuff. By using me you agree to these terms: https://ub3r-b0t.com/terms)");
                }

                if (this.BotApi != null)
                {
                    try
                    {
                        await this.BotApi.IssueRequestAsync(new BotMessageData(BotType.Discord) { Content = ".prune restore", Prefix = ".", Server = guild.Id.ToString() });
                    }
                    catch (Exception ex)
                    {
                        Log.Error(ex, $"Error calling prune restore command");
                    }
                }
            }
        }

        /// <summary>
        /// Handles leaving a guild. Calls the prune endpoint to clear out settings.
        /// </summary>
        private async Task HandleLeftGuildAsync(SocketGuild guild)
        {
            this.TrackEvent("serverLeave");

            SettingsConfig.RemoveSettings(guild.Id.ToString());

            if (this.BotApi != null)
            {
                try
                { 
                    await this.BotApi.IssueRequestAsync(new BotMessageData(BotType.Discord) { Content = ".prune", Prefix = ".", Server = guild.Id.ToString() });
                }
                catch (Exception ex)
                {
                    Log.Error(ex, $"Error calling prune command");
                }
            }

            await audioManager.LeaveAudioAsync(guild.Id);
        }

        /// <summary>
        /// Sends greetings and mod log messages, and sets an auto role, if configured.
        /// </summary>
        private async Task HandleUserJoinedAsync(SocketGuildUser guildUser)
        {
            var settings = SettingsConfig.GetSettings(guildUser.Guild.Id);

            if (!string.IsNullOrEmpty(settings.Greeting) && settings.GreetingId != 0)
            {
                var greeting = settings.Greeting.Replace("%user%", guildUser.Mention);
                greeting = greeting.Replace("%username%", $"{guildUser}");

                greeting = Consts.ChannelRegex.Replace(greeting, new MatchEvaluator((Match chanMatch) =>
                {
                    string channelName = chanMatch.Groups[1].Value;
                    var channel = guildUser.Guild.Channels.Where(c => c is ITextChannel && c.Name.Equals(channelName, StringComparison.OrdinalIgnoreCase)).FirstOrDefault();
                    return (channel as ITextChannel)?.Mention ?? $"#{channelName}";
                }));

                var greetingChannel = this.Client.GetChannel(settings.GreetingId) as ITextChannel ?? guildUser.Guild.DefaultChannel;
                if (greetingChannel.GetCurrentUserPermissions().SendMessages)
                {
                    await greetingChannel.SendMessageAsync(greeting);
                }
            }

            if (settings.JoinRoleId != 0 && guildUser.Guild.CurrentUser.GuildPermissions.ManageRoles)
            {
                var role = guildUser.Guild.GetRole(settings.JoinRoleId);
                if (role != null)
                {
                    try
                    {
                        await guildUser.AddRolesAsync(new[] { role });
                    }
                    catch (HttpException ex) when (ex.HttpCode == HttpStatusCode.Forbidden)
                    {
                        await guildUser.Guild.SendOwnerDMAsync($"Permissions error detected for {guildUser.Guild.Name}: Auto role add on user joined failed, role `{role.Name}` is higher in order than my role");
                    }
                    catch (HttpException ex) when (ex.HttpCode == HttpStatusCode.NotFound)
                    {
                        await guildUser.Guild.SendOwnerDMAsync($"Error detected for {guildUser.Guild.Name}: Auto role add on user joined failed, role `{role.Name}` does not exist");
                    }
                }
            }

            // mod log
            if (settings.Mod_LogId != 0 && settings.HasFlag(ModOptions.Mod_LogUserJoin))
            {
                string joinText = $"{guildUser.Mention} ({guildUser}) joined.";
                if (this.Client.GetChannel(settings.Mod_LogId) is ITextChannel modLogChannel && modLogChannel.GetCurrentUserPermissions().SendMessages)
                {
                    this.BatchSendMessageAsync(modLogChannel, joinText);
                }
            }
        }

        /// <summary>
        /// Sends farewells and mod log messages, if configured.
        /// </summary>
        private async Task HandleUserLeftAsync(SocketGuildUser guildUser)
        {
            var settings = SettingsConfig.GetSettings(guildUser.Guild.Id);

            if (!string.IsNullOrEmpty(settings.Farewell) && settings.FarewellId != 0)
            {
                var farewell = settings.Farewell.Replace("%user%", guildUser.Mention);
                farewell = farewell.Replace("%username%", $"{guildUser}");

                farewell = Consts.ChannelRegex.Replace(farewell, new MatchEvaluator((Match chanMatch) =>
                {
                    string channelName = chanMatch.Groups[1].Value;
                    var channel = guildUser.Guild.Channels.Where(c => c is ITextChannel && c.Name.Equals(channelName, StringComparison.OrdinalIgnoreCase)).FirstOrDefault();
                    return (channel as ITextChannel)?.Mention ?? $"#{channelName}";
                }));

                var farewellChannel = this.Client.GetChannel(settings.FarewellId) as ITextChannel ?? guildUser.Guild.DefaultChannel;
                if (farewellChannel.GetCurrentUserPermissions().SendMessages)
                {
                    await farewellChannel.SendMessageAsync(farewell);
                }
            }

            // mod log
            if (settings.HasFlag(ModOptions.Mod_LogUserLeave) && this.Client.GetChannel(settings.Mod_LogId) is ITextChannel modLogChannel && modLogChannel.GetCurrentUserPermissions().SendMessages)
            {
                this.BatchSendMessageAsync(modLogChannel, $"{guildUser.Mention} ({guildUser}) left.");
            }
            
            var messageData = BotMessageData.Create(guildUser, settings);
            messageData.Content = ".timer clear";
            messageData.Prefix = ".";
            await this.BotApi.IssueRequestAsync(messageData);
        }

        /// <summary>
        /// Announces user voice join/leave and sends mod log messages, if configured.
        /// </summary>
        private async Task HandleUserVoiceStateUpdatedAsync(SocketUser user, SocketVoiceState beforeState, SocketVoiceState afterState)
        {
            // voice state detection
            var guildUser = (user as SocketGuildUser);
            var botGuildUser = guildUser.Guild.CurrentUser;
            if (guildUser.Id != botGuildUser.Id) // ignore joins/leaves from the bot
            {
                if (beforeState.VoiceChannel != afterState.VoiceChannel && afterState.VoiceChannel == botGuildUser.VoiceChannel)
                {
                    // if they are connecting for the first time, wait a moment to account for possible conncetion delay. otherwise play immediately.
                    if (beforeState.VoiceChannel == null)
                    {
                        await Task.Delay(1000);
                    }

                    await this.audioManager.SendAudioAsync(guildUser, afterState.VoiceChannel, VoicePhraseType.UserJoin);
                }
                else if (beforeState.VoiceChannel != afterState.VoiceChannel && beforeState.VoiceChannel == botGuildUser.VoiceChannel)
                {
                    await this.audioManager.SendAudioAsync(guildUser, beforeState.VoiceChannel, VoicePhraseType.UserLeave);
                }
            }

            // mod logging
            var settings = SettingsConfig.GetSettings(guildUser.Guild.Id);
            if (settings.Mod_LogId != 0)
            {
                if (this.Client.GetChannel(settings.Mod_LogId) is ITextChannel modLogChannel && modLogChannel.GetCurrentUserPermissions().SendMessages)
                {
                    var msg = string.Empty;

                    if (settings.HasFlag(ModOptions.Mod_LogUserLeaveVoice) && beforeState.VoiceChannel != null && beforeState.VoiceChannel.Id != afterState.VoiceChannel?.Id)
                    {
                        msg = $"{guildUser.Mention} ({guildUser}) left voice channel { beforeState.VoiceChannel.Name}";
                    }

                    if (settings.HasFlag(ModOptions.Mod_LogUserJoinVoice) && afterState.VoiceChannel != null && afterState.VoiceChannel.Id != beforeState.VoiceChannel?.Id)
                    {
                        if (string.IsNullOrEmpty(msg))
                        {
                            msg = $"{guildUser.Mention} ({guildUser}) joined voice channel {afterState.VoiceChannel.Name}";
                        }
                        else
                        {
                            msg += $" and joined voice channel {afterState.VoiceChannel.Name}";
                        }
                    }

                    if (!string.IsNullOrEmpty(msg))
                    {
                        this.BatchSendMessageAsync(modLogChannel, msg);
                    }
                }
            }
        }

        /// <summary>
        /// Sends mod log messages for role and nickname changes, if configured.
        /// </summary>
        private Task HandleGuildMemberUpdated(SocketGuildUser guildUserBefore, SocketGuildUser guildUserAfter)
        {
            // Mod log
            var settings = SettingsConfig.GetSettings(guildUserBefore.Guild.Id);
            if (this.Client.GetChannel(settings.Mod_LogId) is ITextChannel modLogChannel && (modLogChannel.GetCurrentUserPermissions().SendMessages))
            {
                if (settings.HasFlag(ModOptions.Mod_LogUserRole))
                {
                    var rolesAdded = (from role in guildUserAfter.Roles
                                      where guildUserBefore.Roles.All(r => r.Id != role.Id)
                                      select guildUserAfter.Guild.Roles.First(g => g.Id == role.Id).Name.TrimStart('@')).ToList();

                    var rolesRemoved = (from role in guildUserBefore.Roles
                                      where guildUserAfter.Roles.All(r => r.Id != role.Id)
                                      select guildUserBefore.Guild.Roles.First(g => g.Id == role.Id).Name.TrimStart('@')).ToList();

                    if (rolesAdded.Count > 0)
                    {
                        string roleText = $"**{guildUserAfter.Mention} ({guildUserAfter})** had these roles added: `{string.Join(",", rolesAdded)}`";
                        this.BatchSendMessageAsync(modLogChannel, roleText);
                    }

                    if (rolesRemoved.Count > 0)
                    {
                        string roleText = $"**{guildUserAfter.Mention} ({guildUserAfter})** had these roles removed: `{string.Join(",", rolesRemoved)}`";
                        this.BatchSendMessageAsync(modLogChannel, roleText);
                    }
                }

                if (settings.HasFlag(ModOptions.Mod_LogUserNick) && guildUserAfter.Nickname != guildUserBefore.Nickname)
                {
                    string nickText = null;
                    if (string.IsNullOrEmpty(guildUserAfter.Nickname))
                    {
                        nickText = $"{guildUserAfter.Mention} ({guildUserAfter}) removed their nickname (was {guildUserBefore.Nickname})";
                    }
                    else if (string.IsNullOrEmpty(guildUserBefore.Nickname))
                    {
                        nickText = $"{guildUserAfter.Mention} ({guildUserAfter}) set a new nickname to {guildUserAfter.Nickname}";
                    }
                    else
                    {
                        nickText = $"{guildUserAfter.Mention} ({guildUserAfter}) changed their nickname from {guildUserBefore.Nickname} to {guildUserAfter.Nickname}";
                    }

                    this.BatchSendMessageAsync(modLogChannel, nickText);
                }
            }

            return Task.CompletedTask;
        }

        /// <summary>
        /// Sends mod log messages for user bans, if configured.
        /// </summary>
        private Task HandleUserBanned(SocketUser user, SocketGuild guild)
        {
            // mod log
            var settings = SettingsConfig.GetSettings(guild.Id);
            if (settings.HasFlag(ModOptions.Mod_LogUserBan) && this.Client.GetChannel(settings.Mod_LogId) is ITextChannel modLogChannel && modLogChannel.GetCurrentUserPermissions().SendMessages)
            {
                string userIdentifier = user != null ? $"{user}" : "Unknown user";
                this.BatchSendMessageAsync(modLogChannel, $"{userIdentifier} was banned.");
            }

            return Task.CompletedTask;
        }

        /// <summary>
        /// Handles responses for messages.
        /// TODO: This method is way too huge.
        /// TODO: read prior todo, IT'S GETTING WORSe, STAHP
        /// </summary>
        private async Task HandleMessageReceivedAsync(SocketMessage socketMessage, string reactionType = null, IUser reactionUser = null)
        {
            var props = new Dictionary<string, string> {
                { "server", (socketMessage.Channel as SocketGuildChannel)?.Guild.Id.ToString() ?? "private" },
                { "channel", socketMessage.Channel.Id.ToString() },
            };

            this.TrackEvent("messageReceived", props);

            // Ignore system and our own messages.
            var message = socketMessage as SocketUserMessage;
            bool isOutbound = false;

            // replicate to webhook, if configured
            this.CallOutgoingWebhookAsync(message).Forget();

            if (message == null || (isOutbound = message.Author.Id == this.Client.CurrentUser.Id))
            {
                if (isOutbound && this.Config.LogOutgoing)
                {
                    var logMessage = message.Embeds?.Count > 0 ? $"Sending [embed content] to {message.Channel.Name}" : $"Sending to {message.Channel.Name}: {message.Content}";
                    Log.Verbose($"{{Outgoing}} {logMessage}", ">>>");
                }

                return;
            }

            // Ignore other bots unless it's an allowed webhook
            // Always ignore bot reactions
            var webhookUser = message.Author as IWebhookUser;
            if (message.Author.IsBot && !this.Config.Discord.AllowedWebhooks.Contains(webhookUser?.WebhookId ?? 0) || reactionUser?.IsBot == true)
            {
                return;
            }

            // grab the settings for this server
            var botGuildUser = (message.Channel as SocketGuildChannel)?.Guild.CurrentUser;
            var guildUser = message.Author as SocketGuildUser;
            var guildId = webhookUser?.GuildId ?? guildUser?.Guild.Id;
            var settings = SettingsConfig.GetSettings(guildId?.ToString());

            // if it's a globally blocked server, ignore it unless it's the owner
            if (message.Author.Id != this.Config.Discord.OwnerId && guildId != null && this.Config.Discord.BlockedServers.Contains(guildId.Value) && !this.Config.OcrAutoIds.Contains(message.Channel.Id))
            {
                return;
            }

            // if it's a globally blocked user, ignore them
            if (this.Config.Discord.BlockedUsers.Contains(message.Author.Id))
            {
                return;
            }

            if (this.Throttler.IsThrottled(message.Author.Id.ToString(), ThrottleType.User))
            {
                Log.Debug($"messaging throttle from user: {message.Author.Id} on chan {message.Channel.Id} server {guildId}");
                return;
            }

            // Bail out with help info if it's a PM
            if (message.Channel is IDMChannel)
            {
                await this.RespondAsync(message, "Info and commands can be found at: https://ub3r-b0t.com [Commands don't work in direct messages]");
                return;
            }

            if (this.Throttler.IsThrottled(guildId.ToString(), ThrottleType.Guild))
            {
                Log.Debug($"messaging throttle from guild: {message.Author.Id} on chan {message.Channel.Id} server {guildId}");
                return;
            }

            var botContext = new DiscordBotContext(this.Client, message)
            {
                Reaction = reactionType,
                ReactionUser = reactionUser,
                BotApi = this.BotApi,
                AudioManager = this.audioManager,
                Bot = this,
            };

            foreach (var module in this.modules)
            {
                var typeInfo = module.GetType().GetTypeInfo();
                var permissionChecksPassed = await this.CheckPermissions(botContext, typeInfo);

                if (!permissionChecksPassed)
                {
                    continue;
                }

                var result = await module.Process(botContext);
                if (result == ModuleResult.Stop)
                {
                    return;
                }
            }

            var textChannel = message.Channel as ITextChannel;
            if (botGuildUser != null && !botGuildUser.GetPermissions(textChannel).SendMessages)
            {
                return;
            }
            
            // If it's a command, match that before anything else.
            await this.PreProcessMessage(botContext.MessageData, settings);
            string command = botContext.MessageData.Command;

            if (message.Attachments.FirstOrDefault() is Attachment attachment)
            {
                this.ImageUrls[botContext.MessageData.Channel] = attachment;
            }

            // if it's a blocked command, bail
            if (settings.IsCommandDisabled(CommandsConfig.Instance, command) && !IsAuthorOwner(message))
            {
                return;
            }

            if (this.discordCommands.TryGetValue(command, out IDiscordCommand discordCommand))
            {
                var commandProps = new Dictionary<string, string> {
                        { "command",  command.ToLowerInvariant() },
                        { "server", botContext.MessageData.Server },
                        { "channel", botContext.MessageData.Channel }
                    };
                this.TrackEvent("commandProcessed", commandProps);

                var typeInfo = discordCommand.GetType().GetTypeInfo();
                var permissionChecksPassed = await this.CheckPermissions(botContext, typeInfo);

                if (permissionChecksPassed)
                {
                    CommandResponse response;

                    using (this.DogStats?.StartTimer("commandDuration", tags: new[] { $"shard:{this.Shard}", $"command:{command.ToLowerInvariant()}", $"{this.BotType}" }))
                    {
                        response = await discordCommand.Process(botContext);
                    }

                    if (response?.Attachment != null)
                    {
                        var sentMessage = await message.Channel.SendFileAsync(response.Attachment.Stream, response.Attachment.Name, response.Text);
                        this.botResponsesCache.Add(message.Id, sentMessage);
                    }
                    else if (response?.MultiText != null)
                    {
                        foreach (var messageText in response.MultiText)
                        {
                            var sentMessage = await this.RespondAsync(message, messageText, response.Embed, bypassEdit: true);
                            this.botResponsesCache.Add(message.Id, sentMessage);
                        }
                    }
                    else if (!string.IsNullOrEmpty(response?.Text) || response?.Embed != null)
                    {
                        var sentMessage = await this.RespondAsync(message, response.Text, response.Embed);
                        this.botResponsesCache.Add(message.Id, sentMessage);
                    }
                }
            }
            else
            {
                // Enter typing state for valid commands; skip if throttled
                IDisposable typingState = null;
                var commandKey = command + botContext.MessageData.Server;
                if (this.Config.Discord.TriggerTypingOnCommands &&
                    CommandsConfig.Instance.Commands.ContainsKey(command) &&
                    !this.Throttler.IsThrottled(commandKey, ThrottleType.Command))
                {
                    // possible bug with typing state
                    Log.Debug($"typing triggered by {command}");
                    typingState = message.Channel.EnterTypingState();
                }

                if (botContext.MessageData.Command == "quote" && reactionUser != null)
                {
                    botContext.MessageData.UserName = reactionUser.Username;
                }

                try
                {
                    BotResponseData responseData = await this.ProcessMessageAsync(botContext.MessageData, settings);

                    if (Uri.TryCreate(responseData.AttachmentUrl, UriKind.Absolute, out Uri attachmentUri))
                    {
                        Stream fileStream = await attachmentUri.GetStreamAsync();

                        var sentMessage = await message.Channel.SendFileAsync(fileStream, Path.GetFileName(attachmentUri.AbsolutePath));
                        this.botResponsesCache.Add(message.Id, sentMessage);
                    }
                    else if (responseData.Embed != null)
                    {
                        var sentMessage = await this.RespondAsync(message, string.Empty, responseData.Embed.CreateEmbedBuilder().Build(), bypassEdit: false, rateLimitChecked: botContext.MessageData.RateLimitChecked);
                        this.botResponsesCache.Add(message.Id, sentMessage);
                    }
                    else
                    {
                        foreach (string response in responseData.Responses)
                        {
                            if (!string.IsNullOrWhiteSpace(response))
                            {
                                // if sending a multi part message, skip the edit optimization.
                                var sentMessage = await this.RespondAsync(message, response, embedResponse: null, bypassEdit: responseData.Responses.Count > 1, rateLimitChecked: botContext.MessageData.RateLimitChecked);
                                this.botResponsesCache.Add(message.Id, sentMessage);
                            }
                        }
                    }
                }
                finally
                {
                    typingState?.Dispose();
                }
            }
        }

        private async Task<bool> CheckPermissions(IDiscordBotContext context, TypeInfo typeInfo)
        {
            var attributes = typeInfo.GetCustomAttributes().Where(a => a is PermissionsAttribute);
            var attributeChecksPassed = true;
            foreach (PermissionsAttribute attr in attributes)
            {
                if (!attr.CheckPermissions(context))
                {
                    if (!string.IsNullOrEmpty(attr.FailureString))
                    {
                        var sentMessage = await this.RespondAsync(context.Message, context.Settings.GetString(attr.FailureString));
                        this.botResponsesCache.Add(context.Message.Id, sentMessage);
                    }

                    attributeChecksPassed = false;
                }
            }

            return attributeChecksPassed;
        }

        /// <summary>
        /// Sends mod log messages if configured, and re-processes messages if updated recently.
        /// </summary>
        private async Task HandleMessageUpdated(Cacheable<IMessage, ulong> messageBefore, SocketMessage messageAfter, ISocketMessageChannel channel)
        {
            if (messageAfter?.Channel is IGuildChannel guildChannel)
            {
                var textChannel = guildChannel as ITextChannel;
                var settings = SettingsConfig.GetSettings(guildChannel.Guild.Id);

                if (settings.HasFlag(ModOptions.Mod_LogEdit) &&
                    this.Client.GetChannel(settings.Mod_LogId) is ITextChannel modLogChannel && modLogChannel.GetCurrentUserPermissions().SendMessages &&
                    messageAfter.Channel.Id != settings.Mod_LogId && !messageAfter.Author.IsBot)
                {
                    if (messageBefore.HasValue && messageBefore.Value.Content != messageAfter.Content && !string.IsNullOrEmpty(messageBefore.Value.Content))
                    {
                        var beforeContext = messageBefore.Value.Content.Replace("`", "\\`").Replace("> ", ">");
                        var afterContent = messageAfter.Content.Replace("`", "\\`").Replace("> ", ">");
                        string editText = $"**{messageAfter.Author.Mention} ({messageAfter.Author})** modified in {textChannel.Mention}:\n> {beforeContext}\nto\n> {afterContent}";
                        await modLogChannel.SendMessageAsync(editText.SubstringUpTo(Discord.DiscordConfig.MaxMessageSize), allowedMentions: AllowedMentions.None);
                    }
                }

                // if the message is from the last hour, see if we can re-process it.
                if (messageBefore.HasValue && messageAfter.Content != messageBefore.Value.Content && messageAfter.Author.Id != this.Client.CurrentUser.Id &&
                    DateTimeOffset.UtcNow.Subtract(messageAfter.Timestamp) < TimeSpan.FromHours(1))
                {
                    await this.HandleMessageReceivedAsync(messageAfter);
                }
            }
        }

        /// <summary>
        /// Sends mod log messages if configured, and deletes any corresponding bot response.
        /// </summary>
        private async Task HandleMessageDeleted(Cacheable<IMessage, ulong> cachedMessage, ISocketMessageChannel channel)
        {
            var msg = this.botResponsesCache.Remove(cachedMessage.Id);
            if (msg != null)
            {
                try
                {
                    await msg.DeleteAsync();
                }
                catch (Exception)
                {
                    // ignore, don't care if we can't delete our own message
                }
            }

            if (cachedMessage.HasValue && channel is IGuildChannel guildChannel)
            {
                var message = cachedMessage.Value;
                var textChannel = guildChannel as ITextChannel;
                var guild = guildChannel.Guild as SocketGuild;
                var settings = SettingsConfig.GetSettings(guildChannel.GuildId);

                if (settings.HasFlag(ModOptions.Mod_LogDelete) && guildChannel.Id != settings.Mod_LogId && !message.Author.IsBot &&
                    this.Client.GetChannel(settings.Mod_LogId) is ITextChannel modLogChannel && modLogChannel.GetCurrentUserPermissions().SendMessages)
                {
                    string delText = "";

                    if (settings.TriggersCensor(message.Content, out _))
                    {
                        delText = "```Word Censor Triggered```";
                    }

                    delText += $"**{message.Author.Mention} ({message.Author})** deleted in {textChannel.Mention}: {message.Content}";

                    // Include attachment URLs, if applicable
                    if (message.Attachments?.Count > 0)
                    {
                        delText += " " + string.Join(" ", message.Attachments.Select(a => a.Url));
                    }

                    this.BatchSendMessageAsync(modLogChannel, delText.SubstringUpTo(Discord.DiscordConfig.MaxMessageSize));
                }
            }
        }

        private async Task HandleReactionAdded(Cacheable<IUserMessage, ulong> message, ISocketMessageChannel channel, SocketReaction reaction)
        {
            if (reaction.User.IsSpecified && reaction.User.Value.IsBot)
            {
                return;
            }

            string reactionEmote = reaction.Emote.Name;

            // if an Eye emoji was added, let's process it
            if ((reactionEmote == "👁" || reactionEmote == "🖼") &&
                reaction.Message.IsSpecified &&
                (IsAuthorPatron(reaction.UserId) || BotConfig.Instance.OcrAutoIds.Contains(channel.Id)) &&
                reaction.Message.Value.ParseImageUrl() != null)
            {
                if (reaction.Message.Value.Reactions.Any(r => (r.Key.Name == "👁" || r.Key.Name == "🖼") && r.Value.ReactionCount > 1))
                {
                    return;
                }

                await this.HandleMessageReceivedAsync(reaction.Message.Value, reactionEmote);
            }

            var guildChannel = channel as SocketTextChannel;
            var settings = SettingsConfig.GetSettings(guildChannel.Guild.Id);
            var customEmote = reaction.Emote as Emote;

            if ((reactionEmote == "💬" || reactionEmote == "🗨️" || reactionEmote == "❓" || reactionEmote == "🤖") && reaction.Message.IsSpecified && !string.IsNullOrEmpty(reaction.Message.Value?.Content))
            {
                // if the reaction already exists, don't re-process.
                if (reaction.Message.Value.Reactions.Any(r => (r.Key.Name == "💬" || r.Key.Name == "🗨️" || r.Key.Name == "❓" || r.Key.Name == "🤖") && r.Value.ReactionCount > 1))
                {
                    return;
                }

                IUser reactionUser;
                if (reaction.User.IsSpecified)
                {
                    reactionUser = reaction.User.Value;
                }
                else
                {
                    await guildChannel.Guild.DownloadUsersAsync();
                    reactionUser = guildChannel.GetUser(reaction.UserId);
                }

                await this.HandleMessageReceivedAsync(reaction.Message.Value, reactionEmote, reactionUser);
            }
            else if (reactionEmote == "➕" || reactionEmote == "➖" || customEmote?.Id == settings.RoleAddEmoteId || customEmote?.Id == settings.RoleRemoveEmoteId)
            {
                // handle possible role adds/removes
                IUserMessage reactionMessage = null;
                if (reaction.Message.IsSpecified)
                {
                    reactionMessage = reaction.Message.Value;
                }
                else
                {
                    reactionMessage = await reaction.Channel.GetMessageAsync(reaction.MessageId) as IUserMessage;
                }

                if (await RoleCommand.AddRoleViaReaction(reactionMessage, reaction))
                {
                    await reactionMessage.RemoveReactionAsync(reaction.Emote, reaction.User.Value);
                }
            }
        }

        protected override async Task RespondAsync(BotMessageData messageData, string response)
        {
            await this.RespondAsync(messageData.DiscordMessageData, response, rateLimitChecked: messageData.RateLimitChecked);
        }

        private async Task<IUserMessage> RespondAsync(SocketUserMessage message, string response, Embed embedResponse = null, bool bypassEdit = false, bool rateLimitChecked = false)
        {
            SocketGuild guild = (message.Channel as SocketGuildChannel)?.Guild;

            if (!rateLimitChecked)
            {
                this.Throttler.Increment(message.Author.Id.ToString(), ThrottleType.User);

                if (guild != null)
                {
                    this.Throttler.Increment(guild.Id.ToString(), ThrottleType.Guild);
                }
            }

            var props = new Dictionary<string, string> {
                { "server", guild?.Id.ToString() ?? "private" },
                { "channel", message.Channel.Id.ToString() },
            };

            this.TrackEvent("messageSent", props);

            response = response.SubstringUpTo(Discord.DiscordConfig.MaxMessageSize);

            if (!bypassEdit && this.botResponsesCache.Get(message.Id) is IUserMessage oldMsg)
            {
                try
                {
                    await oldMsg.ModifyAsync((m) =>
                    {
                        m.Content = response;
                        m.Embed = embedResponse;
                    });
                }
                catch (HttpException ex) when (ex.DiscordCode == 10008) 
                {
                    // ignore unknown messages; likely deleted
                }

                return null;
            }
            else
            {
                return await message.Channel.SendMessageAsync(response, false, embedResponse);
            }
        }

        private async Task CallOutgoingWebhookAsync(SocketUserMessage message)
        {
            if (message != null && this.Config.Discord.OutgoingWebhooks.TryGetValue(message.Channel.Id, out var webhook))
            {
                if (!message.Author.IsBot || message.Author.Username != webhook.UserName)
                {
                    try
                    {
                        var text = $"<{message.Author.Username}> {message.Content}";
                        if (message.MentionedUsers.Any(u => u.Id == webhook.MentionUserId))
                        {
                            text += webhook.MentionText;
                        }

                        var result = await webhook.Endpoint.PostJsonAsync(new { text, username = message.Author.Username });
                    }
                    catch (Exception ex)
                    {
                        Log.Warning(ex, "Outgoing webhook failed");
                    }
                }
            }
        }
    }
}
