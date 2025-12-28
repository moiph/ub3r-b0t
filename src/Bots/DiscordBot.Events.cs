
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
    using UB3RB0T.Modules;

    public partial class DiscordBot
    {
        private static readonly HashSet<Type> ExpectedExceptionTypes = new()
        {
            typeof(GatewayReconnectException)
        };

        private readonly MessageCache<ulong> botResponsesCache = new MessageCache<ulong>();
        private bool isReady;

        /// <summary>
        /// Wraps the actual event handler callbacks with a task.run
        /// </summary>
        /// <param name="eventType">The event being handled.</param>
        /// <param name="args">The event arguments.</param>
        /// <returns></returns>
        private async Task HandleEvent(DiscordEventType eventType, params object[] args)
        {
            var discordEvent = new DiscordEvent
            {
                EventType = eventType,
                Args = args,
            };

            if (eventType == DiscordEventType.UserVoiceStateUpdated)
            {
                await this.voiceEventChannel.Writer.WriteAsync(discordEvent);
            }
            else
            {
                await this.eventChannel.Writer.WriteAsync(discordEvent);
            }
        }

        /// <summary>
        /// Processes events in the queue.
        /// </summary>
        public async Task ProcessEvents()
        {
            // Kick off voice event processing separately
            Task.Run(() => this.ProcessVoiceEvents()).Forget();

            await foreach(var eventToProcess in this.eventChannel.Reader.ReadAllAsync())
            {

                Task.Run(async () =>
                {
                    var eventType = eventToProcess.EventType;
                    var args = eventToProcess.Args;
                    try
                    {
                        switch (eventType)
                        {
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
                                await this.HandleUserLeftAsync((SocketGuild)args[0], (SocketUser)args[1]);
                                break;
                            case DiscordEventType.GuildMemberUpdated:
                                await this.HandleGuildMemberUpdated((Cacheable<SocketGuildUser,ulong>)args[0], args[1] as SocketGuildUser);
                                break;
                            case DiscordEventType.UserBanned:
                                await this.HandleUserBanned(args[0] as SocketGuildUser, (SocketGuild)args[1]);
                                break;
                            case DiscordEventType.MessageReceived:
                                this.TrackTimer("eventQueueDuration", eventToProcess.Elapsed.TotalMilliseconds);
                                await this.HandleMessageReceivedAsync(args[0] as IUserMessage);
                                break;
                            case DiscordEventType.MessageUpdated:
                                await this.HandleMessageUpdated((Cacheable<IMessage, ulong>)args[0], (SocketMessage)args[1], (ISocketMessageChannel)args[2]);
                                break;
                            case DiscordEventType.MessageDeleted:
                                await this.HandleMessageDeleted((Cacheable<IMessage, ulong>)args[0], (Cacheable<IMessageChannel, ulong>)args[1]);
                                break;
                            case DiscordEventType.ReactionAdded:
                                await this.HandleReactionAdded((Cacheable<IUserMessage, ulong>)args[0], (Cacheable<IMessageChannel, ulong>)args[1], (SocketReaction)args[2]);
                                break;
                            case DiscordEventType.SlashCommand:
                                await this.HandleUserCommand((SocketSlashCommand)args[0]);
                                break;
                            case DiscordEventType.UserCommand:
                                await this.HandleUserCommand((SocketUserCommand)args[0]);
                                break;
                            case DiscordEventType.MessageCommand:
                                await this.HandleUserCommand((SocketMessageCommand)args[0]);
                                break;
                            case DiscordEventType.MessageComponent:
                                await this.HandleComponent((SocketMessageComponent)args[0]);
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
                }).Forget();
            }
        }

        /// <summary>
        /// Processes voice events in the queue.
        /// </summary>
        private async Task ProcessVoiceEvents()
        {
            await foreach(var eventToProcess in this.voiceEventChannel.Reader.ReadAllAsync())
            {
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
            Log.Information($"Client ready.");
            this.JoinAudio();
            return Task.CompletedTask;
        }

        private Task Client_Connected()
        {
            Log.Information($"Client connected.");
            return Task.CompletedTask;
        }

        private Task Client_Disconnected(Exception ex)
        {
            Log.Warning(ex, "Client disconnected.");
            return Task.CompletedTask;
        }

        private Task Discord_Log(LogMessage logMessage)
        {
            // TODO: Temporary filter for audio warnings; remove with future Discord.NET update
            if (logMessage.Message != null && logMessage.Message.Contains("Unknown OpCode") || 
                logMessage.Source != null && logMessage.Source.Contains("Audio") && logMessage.Message != null && 
                (logMessage.Message.Contains("Latency = ") || logMessage.Message.Contains("Malformed Frame")))
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

            if (logMessage.Exception != null && !ExpectedExceptionTypes.Contains(logMessage.Exception.GetType()))
            {
                this.AppInsights?.TrackException(logMessage.Exception);
            }

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

                var defaultChannel = guild.DefaultChannel;
                var canSendToDefaultChannel = defaultChannel != null && guild.CurrentUser.GetPermissions(defaultChannel).SendMessages;

                // if it's a bot farm, bail out.
                await guild.DownloadUsersAsync();

                var botCount = guild.Users.Count(u => u.IsBot);
                var botRatio = (double)botCount / guild.Users.Count;

                if (botCount > 30 && botRatio > .5)
                {
                    Log.Warning($"Auto bailed on a bot farm: {guild.Name} (#{guild.Id})");
                    
                    if (canSendToDefaultChannel)
                    {
                        await defaultChannel.SendMessageAsync("https://tenor.com/view/im-out-hands-up-exit-gif-14678218");
                        Log.Information($"Sent snark to bot farm {guild.Id}");
                    }
                    else
                    {
                        Log.Warning($"Unable to send message to default channel for bot farm {guild.Id}");
                    }

                    await guild.LeaveAsync();
                    return;
                }

                if (canSendToDefaultChannel)
                {
                    await defaultChannel.SendMessageAsync($"(HELLO, I AM UB3R-B0T! .halp for info. {guild.Owner?.Mention ?? "idk who but to someone..."} you're the kickass owner-- you can use .admin to configure some stuff. By using me you agree to these terms: https://ub3r-b0t.com/terms)");
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

            if (settings.Greetings.Count > 0 && settings.GreetingId != 0)
            {
                var greeting = settings.Greetings.Random().Replace("%user%", guildUser.Mention);
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
                    this.BatchSendMessageAsync(modLogChannel, joinText, ModOptions.Mod_LogUserJoin);
                }
            }
        }

        /// <summary>
        /// Sends farewells and mod log messages, if configured.
        /// </summary>
        private async Task HandleUserLeftAsync(SocketGuild guild, SocketUser user)
        {
            if (guild != null)
            {
                var settings = SettingsConfig.GetSettings(guild.Id);

                if (settings.Farewells.Count > 0 && settings.FarewellId != 0)
                {
                    var farewell = settings.Farewells.Random().Replace("%user%", user.Mention);
                    farewell = farewell.Replace("%username%", $"{user}");

                    farewell = Consts.ChannelRegex.Replace(farewell, new MatchEvaluator((Match chanMatch) =>
                    {
                        string channelName = chanMatch.Groups[1].Value;
                        var channel = guild.Channels.Where(c => c is ITextChannel && c.Name.Equals(channelName, StringComparison.OrdinalIgnoreCase)).FirstOrDefault();
                        return (channel as ITextChannel)?.Mention ?? $"#{channelName}";
                    }));

                    var farewellChannel = this.Client.GetChannel(settings.FarewellId) as ITextChannel ?? guild.DefaultChannel;
                    if (farewellChannel.GetCurrentUserPermissions().SendMessages)
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
                else
                {
                    if (settings.DebugMode)
                    {
                        Log.Verbose($"[DBGM] [guild: {guild.Id}] User left, no farewell configured");
                    }
                }

                // mod log
                if (settings.HasFlag(ModOptions.Mod_LogUserLeave) && this.Client.GetChannel(settings.Mod_LogId) is ITextChannel modLogChannel && modLogChannel.GetCurrentUserPermissions().SendMessages)
                {
                    this.BatchSendMessageAsync(modLogChannel, $"{user.Mention} ({user}) left.", ModOptions.Mod_LogUserLeave);
                }

                var messageData = BotMessageData.Create(user, guild, settings);
                messageData.Content = ".terminate guild";
                messageData.Prefix = ".";
                await this.BotApi.IssueRequestAsync(messageData);
            }
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
                    // wait a moment to account for possible conncetion delay.
                    await Task.Delay(1000);

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
                        this.BatchSendMessageAsync(modLogChannel, msg, ModOptions.Mod_LogUserJoinVoice);
                    }
                }
            }
        }

        /// <summary>
        /// Sends mod log messages for role and nickname changes, if configured.
        /// </summary>
        private async Task HandleGuildMemberUpdated(Cacheable<SocketGuildUser, ulong> cachedGuildUserBefore, SocketGuildUser guildUserAfter)
        {
            // Mod log
            var guildUserBefore = await cachedGuildUserBefore.GetOrDownloadAsync();
            if (guildUserBefore != null)
            {
                var settings = SettingsConfig.GetSettings(guildUserAfter.Guild.Id);
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
                            this.BatchSendMessageAsync(modLogChannel, roleText, ModOptions.Mod_LogUserRole);
                        }

                        if (rolesRemoved.Count > 0)
                        {
                            string roleText = $"**{guildUserAfter.Mention} ({guildUserAfter})** had these roles removed: `{string.Join(",", rolesRemoved)}`";
                            this.BatchSendMessageAsync(modLogChannel, roleText, ModOptions.Mod_LogUserRole);
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

                        this.BatchSendMessageAsync(modLogChannel, nickText, ModOptions.Mod_LogUserRole);
                    }

                    if (settings.HasFlag(ModOptions.Mod_LogUserTimeout))
                    {
                        if (guildUserAfter.TimedOutUntil.HasValue && !guildUserBefore.TimedOutUntil.HasValue)
                        {
                            string timeoutText = $"{guildUserAfter.Mention} ({guildUserAfter}) was put in a timeout until <t:{guildUserAfter.TimedOutUntil.Value.ToUnixTimeSeconds()}>";
                            this.BatchSendMessageAsync(modLogChannel, timeoutText, ModOptions.Mod_LogUserTimeout);
                        }
                        else if (!guildUserAfter.TimedOutUntil.HasValue && guildUserBefore.TimedOutUntil.HasValue)
                        {
                            string timeoutText = $"{guildUserAfter.Mention} ({guildUserAfter}) is no longer in a timeout";
                            this.BatchSendMessageAsync(modLogChannel, timeoutText, ModOptions.Mod_LogUserTimeout);
                        }
                    }
                }
            }
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
                this.BatchSendMessageAsync(modLogChannel, $"{userIdentifier} was banned.", ModOptions.Mod_LogUserBan);
            }

            return Task.CompletedTask;
        }

        /// <summary>
        /// Handles responses for messages.
        /// TODO: This method is way too huge.
        /// TODO: read prior todo, IT'S GETTING WORSe, STAHP
        /// </summary>
        private async Task HandleMessageReceivedAsync(IUserMessage message, String reactionType = null, IUser reactionUser = null)
        {
            // Ignore system and our own messages.
            bool isOutbound = false;
            if (message == null || (isOutbound = message.Author.Id == this.Client.CurrentUser.Id))
            {
                if (isOutbound && this.Config.LogOutgoing)
                {
                    var logMessage = message.Embeds?.Count > 0 ? $"Sending [embed content] to {message.Channel.Name}" : $"Sending to {message.Channel.Name}: {message.Content}";
                    Log.Verbose($"{{Outgoing}} {logMessage}", ">>>");
                }

                return;
            }

            var props = new Dictionary<string, string> {
                { "server", (message.Channel as SocketGuildChannel)?.Guild.Id.ToString() ?? "private" },
                { "channel", message.Channel.Id.ToString() },
            };

            this.TrackEvent("messageReceived", props);

            // Ignore other bots unless it's an allowed webhook
            // Always ignore bot reactions
            var webhookUser = message.Author as IWebhookUser;
            if (message.Author.IsBot && !this.Config.Discord.AllowedWebhooks.Contains(webhookUser?.WebhookId ?? 0) || reactionUser?.IsBot == true)
            {
                return;
            }

            var guildUser = message.Author as IGuildUser;
            var guildId = webhookUser?.GuildId ?? guildUser?.Guild.Id;

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

            await ProcessUserEvent(botContext);
        }

        private async Task ProcessUserEvent(DiscordBotContext botContext)
        {
            foreach (var (module, typeInfo) in this.preProcessModules)
            {
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

            var textChannel = botContext.Channel as ITextChannel;
            if (botContext.CurrentUser == null || !botContext.CurrentUser.GetPermissions(textChannel).SendMessages)
            {
                return;
            }
            
            // If it's a command, match that before anything else.
            await this.PreProcessMessage(botContext.MessageData, botContext.Settings);
            string command = botContext.MessageData.Command;

            if (botContext.Message?.Attachments.FirstOrDefault() is Attachment attachment)
            {
                this.ImageUrls[botContext.MessageData.Channel] = attachment;
            }

            // if it's a blocked command, bail
            if (botContext.Settings.IsCommandDisabled(CommandsConfig.Instance, command) && !IsAuthorOwner(botContext.Author))
            {
                return;
            }

            var commandHandled = false;
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
                        if (botContext.Interaction != null)
                        {
                            await botContext.Interaction.RespondWithFileAsync(response.Attachment.Stream, response.Attachment.Name, response.Text, null, false, false, null, null, null, null);
                        }
                        else
                        {
                            var sentMessage = await botContext.Channel.SendFileAsync(response.Attachment.Stream, response.Attachment.Name, response.Text);
                            if (botContext.Message != null)
                            {
                                this.botResponsesCache.Add(botContext.Message.Id, sentMessage.Id);
                            }
                        }
                        commandHandled = true;
                    }
                    else if (response?.MultiText != null)
                    {
                        foreach (var messageText in response.MultiText)
                        {
                            await this.RespondAsync(botContext, messageText, response.Embed, bypassEdit: true);
                        }
                        commandHandled = true;
                    }
                    else if (!string.IsNullOrEmpty(response?.Text) || response?.Embed != null)
                    {
                        await this.RespondAsync(botContext, response.Text, response.Embed);
                        commandHandled = true;
                    }
                    else if (response != null && response.IsHandled)
                    {
                        commandHandled = true;
                    }
                }
                else
                {
                    commandHandled = true;
                }
            }
            
            if (!commandHandled)
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
                    typingState = botContext.Message.Channel.EnterTypingState();
                }

                bool bypassEdit = false;
                if (botContext.MessageData.Command == "quote" && botContext.ReactionUser != null)
                {
                    botContext.MessageData.UserName = botContext.ReactionUser.Username;
                    bypassEdit = true; // don't edit a reply if it's a reaction added quote
                }

                try 
                {
                    BotResponseData responseData = await this.ProcessMessageAsync(botContext.MessageData, botContext.Settings);

                    if (Uri.TryCreate(responseData.AttachmentUrl, UriKind.Absolute, out Uri attachmentUri))
                    {
                        Stream fileStream = await attachmentUri.GetStreamAsync();
                        string fileName = Path.GetFileName(attachmentUri.AbsolutePath);

                        if (botContext.Interaction != null)
                        {
                            await botContext.Interaction.RespondWithFileAsync(fileStream, fileName, responseData.Responses?.FirstOrDefault(), null, false, responseData.Ephemeral, null, null, null, null);
                        }
                        else
                        {
                            var sentMessage = await botContext.Channel.SendFileAsync(fileStream, fileName, responseData.Responses?.FirstOrDefault());
                            this.botResponsesCache.Add(botContext.Message.Id, sentMessage.Id);
                        }

                        commandHandled = true;
                    }
                    else if (responseData.Embed != null)
                    {
                        await this.RespondAsync(botContext, string.Empty, responseData.Embed.CreateEmbedBuilder().Build(), bypassEdit: bypassEdit, rateLimitChecked: botContext.MessageData.RateLimitChecked, allowMentions: responseData.AllowMentions, ephemeral: responseData.Ephemeral);
                        commandHandled = true;
                    }
                    else
                    {
                        foreach (string response in responseData.Responses)
                        {
                            if (!string.IsNullOrWhiteSpace(response))
                            {
                                // if sending a multi part message, skip the edit optimization.
                                await this.RespondAsync(botContext, response, embedResponse: null, bypassEdit: responseData.Responses.Count > 1 || bypassEdit, rateLimitChecked: botContext.MessageData.RateLimitChecked, allowMentions: responseData.AllowMentions, ephemeral: responseData.Ephemeral);
                                commandHandled = true;
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "Error in command response handling");
                }
                finally
                {
                    typingState?.Dispose();
                }
            }

            if (commandHandled)
            {
                foreach (var (module, typeInfo) in this.postProcessModules)
                {
                    var permissionChecksPassed = await this.CheckPermissions(botContext, typeInfo);
                    if (!permissionChecksPassed)
                    {
                        continue;
                    }

                    await module.Process(botContext);
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
                        await this.RespondAsync(context, context.Settings.GetString(attr.FailureString), ephemeral: true);
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
                    await this.HandleMessageReceivedAsync(messageAfter as IUserMessage);
                }
            }
        }

        /// <summary>
        /// Sends mod log messages if configured, and deletes any corresponding bot response.
        /// </summary>
        private async Task HandleMessageDeleted(Cacheable<IMessage, ulong> cachedMessage, Cacheable<IMessageChannel, ulong> cachedChannel)
        {
            var msgId = this.botResponsesCache.Remove(cachedMessage.Id);
            var textChannel = (await cachedChannel.GetOrDownloadAsync()) as ITextChannel;

            if (msgId != 0 && textChannel != null)
            {
                var settings = SettingsConfig.GetSettings(textChannel.GuildId);
                if (!settings.DisableMessageCleanup)
                {
                    try
                    {
                        var oldMsg = await textChannel.GetMessageAsync(msgId) as IUserMessage;
                        if (oldMsg != null)
                        {
                            await oldMsg.DeleteAsync();
                        }
                    }
                    catch (Exception)
                    {
                        // ignore, don't care if we can't delete our own message
                    }
                }
            }

            if (cachedMessage.HasValue && textChannel != null)
            {
                var message = cachedMessage.Value;
                var guild = textChannel.Guild as SocketGuild;
                var settings = SettingsConfig.GetSettings(textChannel.GuildId);

                if (settings.HasFlag(ModOptions.Mod_LogDelete) && textChannel.Id != settings.Mod_LogId && !message.Author.IsBot &&
                    this.Client.GetChannel(settings.Mod_LogId) is ITextChannel modLogChannel && modLogChannel.GetCurrentUserPermissions().SendMessages)
                {
                    string delText = "";

                    if (settings.TriggersCensor(message.Content, out _))
                    {
                        delText = "```Word Censor Triggered```";
                    }

                    delText += $"**{message.Author.Mention} ({message.Author})** deleted message #{message.Id} in {textChannel.Mention}: {message.Content}";

                    // Include attachment URLs, if applicable
                    if (message.Attachments?.Count > 0)
                    {
                        delText += " " + string.Join(" ", message.Attachments.Select(a => a.Url));
                    }

                    this.BatchSendMessageAsync(modLogChannel, delText.SubstringUpTo(Discord.DiscordConfig.MaxMessageSize), ModOptions.Mod_LogDelete);
                }
            }
        }

        private async Task HandleReactionAdded(Cacheable<IUserMessage, ulong> message, Cacheable<IMessageChannel, ulong> channel, SocketReaction reaction)
        {
            var guildChannel = (await channel.GetOrDownloadAsync()) as ITextChannel;
            if (guildChannel == null)
            {
                return;
            }

            var settings = SettingsConfig.GetSettings(guildChannel.Guild.Id);

            if (settings.DebugMode)
            {
                Log.Verbose($"[DBGM] [chan: {guildChannel.Id} | guild: {guildChannel.Guild.Id}] Reaction added: {reaction.Emote.Name}");
            }

            if (reaction.User.IsSpecified && reaction.User.Value.IsBot)
            {
                if (settings.DebugMode)
                {
                    Log.Verbose($"[DBGM] [chan: {guildChannel.Id} | guild: {guildChannel.Guild.Id}] User was bot");
                }

                return;
            }

            string reactionEmote = reaction.Emote.Name;

            // if an Eye emoji was added, let's process it
            if (reactionEmote == "👁️" &&
                reaction.Message.IsSpecified && 
                (IsAuthorPatron(reaction.UserId) || BotConfig.Instance.OcrAutoIds.Contains(channel.Id)) &&
                reaction.Message.Value.ParseImageUrl() != null)
            {
                if (reaction.Message.Value.Reactions.Any(r => r.Key.Name == "👁" && r.Value.ReactionCount > 1))
                {
                    return;
                }

                IUser reactionUser = await reaction.GetOrDownloadUserAsync();
                if (reactionUser.IsBot)
                {
                    return;
                }

                await this.HandleMessageReceivedAsync(reaction.Message.Value, reactionEmote, reactionUser);
            }

            var customEmote = reaction.Emote as Emote;

            var allowDownload = reactionEmote == "💬" || reactionEmote == "🗨️";

            if ((reactionEmote == "💬" || reactionEmote == "🗨️" || reactionEmote == "❓" || reactionEmote == "🤖") && (allowDownload || (reaction.Message.IsSpecified && !string.IsNullOrEmpty(reaction.Message.Value?.Content))))
            {
                IUserMessage reactionMessage = allowDownload ? await reaction.GetOrDownloadMessage() : reaction.Message.Value;

                // if the reaction already exists, don't re-process.
                if (reactionMessage.Reactions.Any(r => (r.Key.Name == "💬" || r.Key.Name == "🗨️" || r.Key.Name == "❓" || r.Key.Name == "🤖") && r.Value.ReactionCount > 1))
                {
                    return;
                }

                IUser reactionUser = await reaction.GetOrDownloadUserAsync();
                if (reactionUser.IsBot)
                {
                    return;
                }

                if (!string.IsNullOrEmpty(reactionMessage.Content))
                {
                    await this.HandleMessageReceivedAsync(reactionMessage, reactionEmote, reactionUser);
                }
            }
            else if (reactionEmote == "➕" || reactionEmote == "➖" || customEmote?.Id == settings.RoleAddEmoteId || customEmote?.Id == settings.RoleRemoveEmoteId || reaction.Channel.Id == settings.SelfRolesChannelId)
            {
                if (settings.DebugMode)
                {
                    Log.Verbose($"[DBGM] [chan: {guildChannel.Id} | guild: {guildChannel.Guild.Id}] Role change via reaction");
                }

                var reactionUser = await reaction.GetOrDownloadUserAsync() as IGuildUser;
                if (reactionUser == null || reactionUser.IsBot)
                {
                    if (settings.DebugMode)
                    {
                        Log.Verbose($"[DBGM] [chan: {guildChannel.Id} | guild: {guildChannel.Guild.Id}] Role change via reaction failed, user DNE or bot");
                    }

                    return;
                }

                if (!guildChannel.GetCurrentUserGuildPermissions().ManageRoles)
                {
                    if (settings.DebugMode)
                    {
                        Log.Verbose($"[DBGM] [chan: {guildChannel.Id} | guild: {guildChannel.Guild.Id}] Role modification skipped due to missing permissions");
                    }

                    return;
                }

                // handle possible role adds/removes
                IUserMessage reactionMessage = await message.GetOrDownloadAsync();

                ulong selfRoleId = settings.SelfRoles.FirstOrDefault(kvp => kvp.Value == customEmote?.Id).Key;
                bool roleChanged = false;
                if (selfRoleId != 0)
                {
                    roleChanged = await RoleCommand.AddRoleViaReaction(selfRoleId, reactionUser);
                }
                else
                {
                    roleChanged = await RoleCommand.AddRoleViaReaction(reactionMessage, reaction, reactionUser);
                }

                if (roleChanged && guildChannel.GetCurrentUserPermissions().ManageMessages)
                {
                    await reactionMessage.RemoveReactionAsync(reaction.Emote, reactionUser);
                }
            }
            else if (reactionEmote == "🏅" && !settings.IsCommandDisabled(CommandsConfig.Instance, "rep"))
            {
                IUserMessage reactionMessage = await message.GetOrDownloadAsync();
                if (reactionMessage.Author.IsBot || reactionMessage.Author.IsWebhook)
                {
                    return;
                }

                var reactionUser = await reaction.GetOrDownloadUserAsync() as IGuildUser;
                if (reactionUser == null || reactionUser.IsBot)
                {
                    return;
                }

                await this.HandleMessageReceivedAsync(reactionMessage, reactionEmote, reactionUser);
            }
        }

        private async Task HandleUserCommand(SocketCommandBase command)
        {
            IUserMessage message = null;
            if (command is SocketMessageCommand messageCommand)
            {
                // If author is null, it means the message wasn't in the cache, so download it
                if (messageCommand.Data.Message.Author == null)
                {
                    message = await command.Channel.GetMessageAsync(messageCommand.Data.Message.Id) as IUserMessage;
                }
                else
                {
                    message = messageCommand.Data.Message as IUserMessage;
                }
            }

            var botContext = new DiscordBotContext(this.Client, command, message)
            {
                BotApi = this.BotApi,
                Bot = this,
            };

            var props = new Dictionary<string, string> {
                { "command",  command.CommandName },
                { "server",  botContext.GuildChannel?.Guild.Id.ToString() },
                { "channel", botContext.Channel.Id.ToString() },
                { "shard",  this.Shard.ToString() },
            };
            this.TrackEvent("slashcommandProcessed", props);

            await ProcessUserEvent(botContext);
        }

        private async Task HandleComponent(SocketMessageComponent component)
        {
            var botContext = new DiscordBotContext(this.Client, component, null)
            {
                BotApi = this.BotApi,
                Bot = this,
            };

            await ProcessUserEvent(botContext);
        }

        protected override async Task RespondAsync(BotMessageData messageData, string response)
        {
            if (!string.IsNullOrEmpty(messageData.Server))
            {
                Log.Debug($"Sending {messageData.Command} response to message {messageData.MessageId} by {messageData.UserId} in channel {messageData.Channel} on guild {messageData.Server}");
            }

            await this.RespondAsync(messageData.DiscordMessageData, response, rateLimitChecked: messageData.RateLimitChecked);
        }

        private async Task RespondAsync(IDiscordBotContext context, string response, Embed embedResponse = null, bool bypassEdit = false, bool rateLimitChecked = false, bool allowMentions = true, bool ephemeral = false)
        {
            if (!string.IsNullOrEmpty(context.MessageData.Server))
            {
                Log.Debug($"Sending {context.MessageData.Command} response to message {context.MessageData.MessageId} by {context.MessageData.UserId} in channel {context.MessageData.Channel} on guild {context.MessageData.Server}");
            }

            if (context.Interaction != null)
            {
                await context.Interaction.RespondAsync(response, embedResponse != null ? new Embed[] { embedResponse } : null, false, ephemeral, allowMentions ? null : AllowedMentions.None);
            }
            else if (context.Message != null)
            {
                await RespondAsync(context.Message, response, embedResponse, bypassEdit, rateLimitChecked, allowMentions);
            }
        }

        private async Task RespondAsync(IUserMessage message, string response, Embed embedResponse = null, bool bypassEdit = false, bool rateLimitChecked = false, bool allowMentions = true)
        {
            var textChannel = message.Channel as ITextChannel;
            IGuild guild = textChannel?.Guild;

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

            response = response?.SubstringUpTo(Discord.DiscordConfig.MaxMessageSize);
            var allowedMentions = allowMentions ? null : AllowedMentions.None;

            if (!bypassEdit && textChannel != null && this.botResponsesCache.Get(message.Id) is ulong oldMessageId && oldMessageId != 0)
            {
                try
                {
                    var oldMsg = await textChannel.GetMessageAsync(oldMessageId) as IUserMessage;
                    await oldMsg.ModifyAsync((m) =>
                    {
                        m.Content = response;
                        m.Embed = embedResponse;
                    });
                }
                catch (HttpException ex) when (ex.DiscordCode == DiscordErrorCode.UnknownMessage) 
                {
                    // ignore unknown messages; likely deleted
                }
            }
            else
            {
                var sentMessage = await message.Channel.SendMessageAsync(response, false, embedResponse, allowedMentions: allowedMentions);
                if (sentMessage != null)
                {
                    this.botResponsesCache.Add(message.Id, sentMessage.Id);
                }
            }
        }
    }
}
