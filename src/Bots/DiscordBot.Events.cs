
namespace UB3RB0T
{
    using System;
    using System.Linq;
    using System.Net;
    using System.Text.RegularExpressions;
    using System.Threading.Tasks;
    using Discord;
    using Discord.Net;
    using Discord.WebSocket;
    using Flurl.Http;
    using Newtonsoft.Json;
    using UB3RIRC;

    public partial class DiscordBot
    {
        private MessageCache botResponsesCache = new MessageCache();
        private bool isReady;

        /// <summary>
        /// Wraps the actual event handler callbacks with a task.run
        /// </summary>
        /// <param name="eventType">The event being handled.</param>
        /// <param name="args">The event arguments.</param>
        /// <returns></returns>
        private Task HandleEvent(DiscordEventType eventType, params object[] args)
        {
            Task.Run(async () =>
            {
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
                        case DiscordEventType.UserVoiceStateUpdated:
                            await this.HandleUserVoiceStateUpdatedAsync((SocketUser)args[0], (SocketVoiceState)args[1], (SocketVoiceState)args[2]);
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
                            await this.HandleMessageDeleted((Cacheable <IMessage, ulong>)args[0], (ISocketMessageChannel)args[1]);
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
                    this.Logger.Log(LogType.Warn, $"Error in {eventType} handler: {ex}");
                    this.AppInsights?.TrackException(ex);
                }
            }).Forget();

            return Task.CompletedTask;
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

        private Task Discord_Log(LogMessage arg)
        {
            // TODO: Temporary filter for audio warnings; remove with future Discord.NET update
            if (arg.Message != null && arg.Message.Contains("Unknown OpCode") || (arg.Source != null && arg.Source.Contains("Audio") && arg.Message != null && (arg.Message.Contains("Latency = "))))
            {
                return Task.CompletedTask;
            }

            LogType logType = LogType.Debug;
            switch (arg.Severity)
            {
                case LogSeverity.Critical:
                    logType = LogType.Fatal;
                    break;
                case LogSeverity.Error:
                    logType = LogType.Error;
                    break;
                case LogSeverity.Warning:
                    logType = LogType.Warn;
                    break;
                case LogSeverity.Info:
                    logType = LogType.Info;
                    break;
            }

            if (arg.Exception != null)
            {
                this.AppInsights?.TrackException(arg.Exception);
            }

            this.Logger.Log(logType, arg.ToString());

            return Task.CompletedTask;
        }

        private Task HandleDisconnected(Exception ex)
        {
            this.Logger.Log(LogType.Warn, $"Disconnected: {ex}");
            return Task.CompletedTask;
        }

        /// <summary>
        /// Handles joining a guild and announcing such.
        /// </summary>
        private async Task HandleJoinedGuildAsync(SocketGuild guild)
        {
            if (this.isReady)
            {
                this.AppInsights?.TrackEvent("serverJoin");

                // if it's a bot farm, bail out.
                await guild.DownloadUsersAsync();

                var botCount = guild.Users.Count(u => u.IsBot);
                var botRatio = (double)botCount / guild.Users.Count;
                if (botCount > 30 && botRatio > .5)
                {
                    this.Logger.Log(LogType.Warn, $"Auto bailed on a bot farm: {guild.Name} (#{guild.Id})");
                    await guild.LeaveAsync();
                    return;
                }

                var defaultChannel = guild.DefaultChannel;
                var owner = guild.Owner;
                if (guild.CurrentUser.GetPermissions(defaultChannel).SendMessages)
                {
                    await defaultChannel.SendMessageAsync($"(HELLO, I AM UB3R-B0T! .halp for info. {owner.Mention} you're the kickass owner-- you can use .admin to configure some stuff.)");
                }
            }
        }

        /// <summary>
        /// Handles leaving a guild. Calls the prune endpoint to clear out settings.
        /// </summary>
        private async Task HandleLeftGuildAsync(SocketGuild guild)
        {
            this.AppInsights?.TrackEvent("serverLeave");

            SettingsConfig.RemoveSettings(guild.Id.ToString());

            if (this.Config.PruneEndpoint != null)
            {
                var req = WebRequest.Create($"{this.Config.PruneEndpoint}?id={guild.Id}");
                await req.GetResponseAsync();
            }

            await audioManager.LeaveAudioAsync(guild.Id);
        }

        /// <summary>
        /// Sends greetings and mod log messages, and sets an auto role, if configured.
        /// </summary>
        private async Task HandleUserJoinedAsync(SocketGuildUser guildUser)
        {
            var settings = SettingsConfig.GetSettings(guildUser.Guild.Id);

            if (!string.IsNullOrEmpty(settings.Greeting))
            {
                var greeting = settings.Greeting.Replace("%user%", guildUser.Mention);
                greeting = greeting.Replace("%username%", $"{guildUser.Username}#{guildUser.Discriminator}");

                greeting = Consts.ChannelRegex.Replace(greeting, new MatchEvaluator((Match chanMatch) =>
                {
                    string channelName = chanMatch.Groups[1].Value;
                    var channel = guildUser.Guild.Channels.Where(c => c is ITextChannel && c.Name.Equals(channelName, StringComparison.OrdinalIgnoreCase)).FirstOrDefault();
                    return (channel as ITextChannel)?.Mention ?? channelName;
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
                string joinText = $"{guildUser.Username}#{guildUser.Discriminator} joined.";
                if (this.Client.GetChannel(settings.Mod_LogId) is ITextChannel modLogChannel && modLogChannel.GetCurrentUserPermissions().SendMessages)
                {
                    await modLogChannel.SendMessageAsync(joinText);
                }
            }
        }

        /// <summary>
        /// Sends farewells and mod log messages, if configured.
        /// </summary>
        private async Task HandleUserLeftAsync(SocketGuildUser guildUser)
        {
            var settings = SettingsConfig.GetSettings(guildUser.Guild.Id);

            if (!string.IsNullOrEmpty(settings.Farewell))
            {
                var farewell = settings.Farewell.Replace("%user%", guildUser.Mention);
                farewell = farewell.Replace("%username%", $"{guildUser.Username}#{guildUser.Discriminator}");

                farewell = Consts.ChannelRegex.Replace(farewell, new MatchEvaluator((Match chanMatch) =>
                {
                    string channelName = chanMatch.Captures[0].Value;
                    var channel = guildUser.Guild.Channels.Where(c => c is ITextChannel && c.Name.Equals(channelName, StringComparison.OrdinalIgnoreCase)).FirstOrDefault();
                    return (channel as ITextChannel)?.Mention ?? channelName;
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
                await modLogChannel.SendMessageAsync($"{guildUser.Username}#{guildUser.Discriminator} left.");
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
                    if (settings.HasFlag(ModOptions.Mod_LogUserLeaveVoice) && beforeState.VoiceChannel != null && beforeState.VoiceChannel.Id != afterState.VoiceChannel?.Id)
                    {
                        modLogChannel.SendMessageAsync($"{guildUser.Username} left voice channel { beforeState.VoiceChannel.Name}").Forget();
                    }

                    if (settings.HasFlag(ModOptions.Mod_LogUserJoinVoice) && afterState.VoiceChannel != null && afterState.VoiceChannel.Id != beforeState.VoiceChannel?.Id)
                    {
                        modLogChannel.SendMessageAsync($"{guildUser.Username} joined voice channel {afterState.VoiceChannel.Name}").Forget();
                    }
                }
            }
        }

        /// <summary>
        /// Sends mod log messages for role and nickname changes, if configured.
        /// </summary>
        private async Task HandleGuildMemberUpdated(SocketGuildUser guildUserBefore, SocketGuildUser guildUserAfter)
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
                        string roleText = $"**{guildUserAfter.Username}#{guildUserAfter.Discriminator}** had these roles added: `{string.Join(",", rolesAdded)}`";
                        await modLogChannel.SendMessageAsync(roleText);
                    }

                    if (rolesRemoved.Count > 0)
                    {
                        string roleText = $"**{guildUserAfter.Username}#{guildUserAfter.Discriminator}** had these roles removed: `{string.Join(",", rolesRemoved)}`";
                        await modLogChannel.SendMessageAsync(roleText);
                    }
                }

                if (settings.HasFlag(ModOptions.Mod_LogUserNick) && guildUserAfter.Nickname != guildUserBefore.Nickname)
                {
                    if (string.IsNullOrEmpty(guildUserAfter.Nickname))
                    {
                        await modLogChannel.SendMessageAsync($"{guildUserAfter.Username}#{guildUserAfter.Discriminator} removed their nickname (was {guildUserBefore.Nickname})");
                    }
                    else if (string.IsNullOrEmpty(guildUserBefore.Nickname))
                    {
                        await modLogChannel.SendMessageAsync($"{guildUserAfter.Username}#{guildUserAfter.Discriminator} set a new nickname to {guildUserAfter.Nickname}");
                    }
                    else
                    {
                        await modLogChannel.SendMessageAsync($"{guildUserAfter.Username}#{guildUserAfter.Discriminator} changed their nickname from {guildUserBefore.Nickname} to {guildUserAfter.Nickname}");
                    }
                }
            }
        }

        /// <summary>
        /// Sends mod log messages for user bans, if configured.
        /// </summary>
        private async Task HandleUserBanned(SocketUser user, SocketGuild guild)
        {
            // mod log
            var settings = SettingsConfig.GetSettings(guild.Id);
            if (settings.HasFlag(ModOptions.Mod_LogUserBan) && this.Client.GetChannel(settings.Mod_LogId) is ITextChannel modLogChannel && modLogChannel.GetCurrentUserPermissions().SendMessages)
            {
                string userIdentifier = user != null ? $"{user.Username}#{user.Discriminator}" : "Unknown user";
                await modLogChannel.SendMessageAsync($"{userIdentifier} was banned.");
            }
        }

        /// <summary>
        /// Handles responses for messages.
        /// TODO: This method is way too huge.
        /// </summary>
        private async Task HandleMessageReceivedAsync(SocketMessage socketMessage, string reactionType = null, IUser reactionUser = null)
        {
            // Ignore system and our own messages.
            var message = socketMessage as SocketUserMessage;
            bool isOutbound = false;

            // replicate to webhook, if configured
            this.CallOutgoingWebhookAsync(message).Forget();

            if (message == null || (isOutbound = message.Author.Id == this.Client.CurrentUser.Id))
            {
                if (isOutbound)
                {
                    if (message.Embeds?.Count > 0)
                    {
                        this.Logger.Log(LogType.Outgoing, $"\tSending [embed content] to {message.Channel.Name}");
                    }
                    else
                    {
                        this.Logger.Log(LogType.Outgoing, $"\tSending to {message.Channel.Name}: {message.Content}");
                    }
                }

                return;
            }
            
            // Ignore other bots
            if (message.Author.IsBot)
            {
                return;
            }

            // grab the settings for this server
            var botGuildUser = (message.Channel as SocketGuildChannel)?.Guild.CurrentUser;
            var guildUser = message.Author as IGuildUser;
            var guildId = (guildUser != null && guildUser.IsWebhook) ? null : guildUser?.GuildId;
            var settings = SettingsConfig.GetSettings(guildId?.ToString());

            // if it's a globally blocked server, ignore it unless it's the owner
            if (message.Author.Id != this.Config.Discord.OwnerId && guildId != null && this.Config.Discord.BlockedServers.Contains(guildId.Value))
            {
                return;
            }

            // if the user is blocked based on role, return
            var botlessRoleId = guildUser?.Guild?.Roles?.FirstOrDefault(r => r.Name?.ToLowerInvariant() == "botless")?.Id;
            if ((message.Author as IGuildUser)?.RoleIds.Any(r => botlessRoleId != null && r == botlessRoleId.Value) ?? false)
            {
                return;
            }

            // Bail out with help info if it's a PM
            if (message.Channel is IDMChannel && (message.Content.Contains("help") || message.Content.Contains("info") || message.Content.Contains("commands")))
            {
                await this.RespondAsync(message, "Info and commands can be found at: https://ub3r-b0t.com");
                return;
            }

            // check for word censors
            if (botGuildUser?.GuildPermissions.ManageMessages ?? false)
            {
                if (settings.TriggersCensor(message.Content, out string offendingWord))
                {
                    offendingWord = offendingWord != null ? $"`{offendingWord}`" : "*FANCY lanuage filters*";
                    await message.DeleteAsync();
                    var dmChannel = await message.Author.GetOrCreateDMChannelAsync();
                    await dmChannel.SendMessageAsync($"hi uh sorry but your most recent message was tripped up by {offendingWord} and thusly was deleted. complain to management, i'm just the enforcer");
                    return;
                }
            }

            var textChannel = message.Channel as ITextChannel;
            if (botGuildUser != null && !botGuildUser.GetPermissions(textChannel).SendMessages)
            {
                return;
            }

            // special case FAQ channel
            if (message.Channel.Id == this.Config.FaqChannel && message.Content.EndsWith("?") && this.Config.FaqEndpoint != null)
            {
                string content = message.Content.Replace("<@85614143951892480>", "ub3r-b0t");
                var result = await this.Config.FaqEndpoint.ToString().WithHeader("Ocp-Apim-Subscription-Key", this.Config.FaqKey).PostJsonAsync(new { question = content });
                if (result.IsSuccessStatusCode)
                {
                    var response = await result.Content.ReadAsStringAsync();
                    var qnaData = JsonConvert.DeserializeObject<QnAMakerData>(response);
                    var score = Math.Floor(qnaData.Score);
                    var answer = WebUtility.HtmlDecode(qnaData.Answer);
                    await message.Channel.SendMessageAsync($"{answer} ({score}% match)");
                }
                else
                {
                    await message.Channel.SendMessageAsync("An error occurred while fetching data");
                }

                return;
            }

            string messageContent = message.Content;
            // OCR for fun if requested (patrons only)
            // TODO: need to drive this via config
            // TODO: Need to generalize even further due to more reaction types
            // TODO: oh my god stop writing TODOs and just make the code less awful
            if (!string.IsNullOrEmpty(reactionType))
            {
                string newMessageContent = string.Empty;

                if (reactionType == "💬" || reactionType == "🗨️")
                {
                    newMessageContent = $".quote add \"{messageContent}\" - userid:{message.Author.Id} {message.Author.Username}";
                    await message.AddReactionAsync(new Emoji("💬"));
                }
                else if (string.IsNullOrEmpty(message.Content) && message.Attachments?.FirstOrDefault()?.Url is string attachmentUrl)
                {
                    if (reactionType == "👁")
                    {
                        var result = await this.Config.OcrEndpoint.ToString()
                        .WithHeader("Ocp-Apim-Subscription-Key", this.Config.VisionKey)
                        .PostJsonAsync(new { url = attachmentUrl });

                        if (result.IsSuccessStatusCode)
                        {
                            var response = await result.Content.ReadAsStringAsync();
                            var ocrData = JsonConvert.DeserializeObject<OcrData>(response);
                            if (!string.IsNullOrEmpty(ocrData.GetText()))
                            {
                                newMessageContent = ocrData.GetText();
                            }
                        }
                    }
                    else if (reactionType == "🖼")
                    {
                        var analyzeResult = await this.Config.AnalyzeEndpoint.ToString()
                            .WithHeader("Ocp-Apim-Subscription-Key", this.Config.VisionKey)
                            .PostJsonAsync(new { url = attachmentUrl });

                        if (analyzeResult.IsSuccessStatusCode)
                        {
                            var response = await analyzeResult.Content.ReadAsStringAsync();
                            var analyzeData = JsonConvert.DeserializeObject<AnalyzeImageData>(response);
                            if (analyzeData.Description.Tags.Contains("ball"))
                            {
                                newMessageContent = ".8ball foo";
                            }
                            else if (analyzeData.Description.Tags.Contains("outdoor"))
                            {
                                newMessageContent = ".fw";
                            }
                        }
                    }
                }

                messageContent = newMessageContent ?? messageContent;
            }

            // If it's a command, match that before anything else.
            string query = string.Empty;
            bool hasBotMention = message.MentionedUsers.Any(u => u.Id == this.Client.CurrentUser.Id);

            int argPos = 0;
            if (message.HasMentionPrefix(this.Client.CurrentUser, ref argPos))
            {
                query = messageContent.Substring(argPos);
            }
            else if (messageContent.StartsWith(settings.Prefix))
            {
                query = messageContent.Substring(settings.Prefix.Length);
            }

            var messageData = BotMessageData.Create(message, query, settings);
            messageData.Content = messageContent;
            await this.PreProcessMessage(messageData, settings);

            string command = messageData.Command;

            if (message.Attachments.FirstOrDefault() is Attachment attachment)
            {
                imageUrls[messageData.Channel] = attachment;
            }

            // if it's a blocked command, bail
            if (settings.IsCommandDisabled(CommandsConfig.Instance, command) && !IsAuthorOwner(message))
            {
                return;
            }

            // Check discord specific commands prior to general ones.
            if (discordCommands.Commands.ContainsKey(command))
            {
                var response = await discordCommands.Commands[command].Invoke(message).ConfigureAwait(false);
                if (response != null)
                {
                    if (response.Attachment != null)
                    {
                        var sentMessage = await message.Channel.SendFileAsync(response.Attachment.Stream, response.Attachment.Name, response.Text);
                        this.botResponsesCache.Add(message.Id, sentMessage);
                    }
                    else if (!string.IsNullOrEmpty(response.Text) || response.Embed != null)
                    {
                        var sentMessage = await this.RespondAsync(message, response.Text, response.Embed);
                        this.botResponsesCache.Add(message.Id, sentMessage);
                    }
                }
            }
            else
            {
                IDisposable typingState = null;
                if (CommandsConfig.Instance.Commands.ContainsKey(command))
                {
                    // possible bug with typing state
                    Console.WriteLine($"typing triggered by {command}");
                    typingState = message.Channel.EnterTypingState();
                }

                if (messageData.Command == "quote" && reactionUser != null)
                {
                    messageData.UserName = reactionUser.Username;
                }

                try
                {
                    BotResponseData responseData = await this.ProcessMessageAsync(messageData, settings);

                    if (responseData.Embed != null)
                    {
                        var sentMessage = await this.RespondAsync(message, string.Empty, responseData.Embed.CreateEmbedBuilder());
                        this.botResponsesCache.Add(message.Id, sentMessage);
                    }
                    else
                    {
                        foreach (string response in responseData.Responses)
                        {
                            if (!string.IsNullOrEmpty(response))
                            {
                                // if sending a multi part message, skip the edit optimization.
                                var sentMessage = await this.RespondAsync(message, response, embedResponse: null, bypassEdit: responseData.Responses.Count > 1);
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
                        string editText = $"**{messageAfter.Author.Username}** modified in {textChannel.Mention}: `{messageBefore.Value.Content}` to `{messageAfter.Content}`";
                        await modLogChannel.SendMessageAsync(editText.Substring(0, Math.Min(editText.Length, Discord.DiscordConfig.MaxMessageSize)));
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
            try
            {
                await msg?.DeleteAsync();
            }
            catch (Exception)
            {
                // ignore, don't care if we can't delete our own message
            }

            if (cachedMessage.HasValue && channel is IGuildChannel guildChannel)
            {
                var message = cachedMessage.Value;
                var textChannel = guildChannel as ITextChannel;
                var settings = SettingsConfig.GetSettings(guildChannel.GuildId);

                if (settings.HasFlag(ModOptions.Mod_LogDelete) && guildChannel.Id != settings.Mod_LogId && !message.Author.IsBot &&
                    this.Client.GetChannel(settings.Mod_LogId) is ITextChannel modLogChannel && modLogChannel.GetCurrentUserPermissions().SendMessages)
                {
                    string delText = "";

                    if (settings.TriggersCensor(message.Content, out _))
                    {
                        delText = "```Word Censor Triggered```";
                    }

                    delText += $"**{message.Author.Username}#{message.Author.Discriminator}** deleted in {textChannel.Mention}: {message.Content}";
                    await modLogChannel.SendMessageAsync(delText.SubstringUpTo(2000));
                }
            }
        }

        private async Task HandleReactionAdded(Cacheable<IUserMessage, ulong> message, ISocketMessageChannel channel, SocketReaction reaction)
        {
            // if an Eye emoji was added, let's process it
            if ((reaction.Emote.Name == "👁" || reaction.Emote.Name == "🖼") &&
                reaction.Message.IsSpecified &&
                IsAuthorPatron(reaction.UserId) &&
                string.IsNullOrEmpty(reaction.Message.Value.Content) &&
                reaction.Message.Value.Attachments.Count > 0)
            {
                await this.HandleMessageReceivedAsync(reaction.Message.Value, reaction.Emote.Name);
            }

            if ((reaction.Emote.Name == "💬" || reaction.Emote.Name == "🗨️") && reaction.Message.IsSpecified && !string.IsNullOrEmpty(reaction.Message.Value?.Content))
            {
                // if the reaction already exists, don't re-process.
                if (reaction.Message.Value.Reactions.Any(r => r.Key.Name == "💬" && r.Value.ReactionCount > 1) || reaction.Message.Value.Reactions.Any(r => r.Key.Name == "🗨️" && r.Value.ReactionCount > 1))
                {
                    return;
                }

                await this.HandleMessageReceivedAsync(reaction.Message.Value, reaction.Emote.Name, reaction.User.Value);
            }
        }

        protected override async Task RespondAsync(BotMessageData messageData, string response)
        {
            await this.RespondAsync(messageData.DiscordMessageData, response);
        }

        private async Task<IUserMessage> RespondAsync(SocketUserMessage message, string response, Embed embedResponse = null, bool bypassEdit = false)
        {
            response = response.Substring(0, Math.Min(response.Length, 2000));

            if (!bypassEdit && this.botResponsesCache.Get(message.Id) is IUserMessage oldMsg)
            {
                await oldMsg.ModifyAsync((m) =>
                {
                    m.Content = response;
                    m.Embed = embedResponse;
                });

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

                        var result = await webhook.Endpoint.PostJsonAsync(new { text = text, username = message.Author.Username });
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Outgoing webhook failed {ex}");
                    }
                }
            }
        }
    }
}
