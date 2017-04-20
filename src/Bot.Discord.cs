namespace UB3RB0T
{
    using Discord;
    using Discord.Net;
    using Discord.WebSocket;
    using Flurl;
    using Flurl.Http;
    using Newtonsoft.Json;
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Net;
    using System.Net.Http;
    using System.Text.RegularExpressions;
    using System.Threading;
    using System.Threading.Tasks;
    using UB3RIRC;

    public partial class Bot
    {
        private MessageCache botResponsesCache = new MessageCache();
        private int messageCount = 0;

        private bool isReady;

        private Timer statsTimer;
        private Timer settingsUpdateTimer;

        private DiscordCommands discordCommands;
        private AudioManager audioManager;

        public async Task CreateDiscordBotAsync()
        {
            client = new DiscordSocketClient(new DiscordSocketConfig
            {
                ShardId = this.shard,
                TotalShards = this.Config.Discord.ShardCount,
                LogLevel = LogSeverity.Verbose,
                MessageCacheSize = 500,
            });

            client.MessageReceived += Discord_OnMessageReceivedAsync;
            client.Log += Discord_Log;
            client.UserJoined += Discord_UserJoinedAsync;
            client.UserLeft += Discord_UserLeftAsync;
            client.JoinedGuild += Client_JoinedGuildAsync;
            client.LeftGuild += Discord_LeftGuildAsync;
            client.ReactionAdded += Client_ReactionAdded;
            client.MessageDeleted += Client_MessageDeletedAsync;
            client.MessageUpdated += Client_MessageUpdatedAsync;
            client.UserBanned += Client_UserBannedAsync;
            client.GuildMemberUpdated += Client_GuildMemberUpdatedAsync;
            client.UserVoiceStateUpdated += Client_UserVoiceStateUpdatedAsync;
            client.Ready += Client_Ready;
            client.Disconnected += Client_Disconnected;

            audioManager = new AudioManager();
            discordCommands = new DiscordCommands(client, audioManager, this.BotApi);

            // If user customizeable server settings are supported...support them
            // Currently discord only.
            if (this.Config.SettingsEndpoint != null && settingsUpdateTimer == null)
            {
                await this.UpdateSettingsAsync();

                // set a recurring timer to refresh settings
                settingsUpdateTimer = new Timer(async (object state) =>
                {
                    await this.UpdateSettingsAsync();
                }, null, 30000, 30000);
            }

            await client.LoginAsync(TokenType.Bot, this.Config.Discord.Token);
            await client.StartAsync();

            if ((!string.IsNullOrEmpty(this.Config.Discord.DiscordBotsKey) || !string.IsNullOrEmpty(this.Config.Discord.CarbonStatsKey) || !string.IsNullOrEmpty(this.Config.Discord.DiscordListKey)) && statsTimer == null)
            {
                statsTimer = new Timer(StatsTimerAsync, null, 3600000, 3600000);
            }
        }

        private Task Client_Disconnected(Exception arg)
        {
            this.isReady = false;
            return Task.CompletedTask;
        }

        private Task Client_Ready()
        {
            this.isReady = true;
            this.client.SetGameAsync(this.Config.Discord.Status);

            /*Task.Run(async () =>
            {
                await Task.Delay(10000);
                foreach (var guildSetting in SettingsConfig.Instance.Settings)
                {
                    if (guildSetting.Value.VoiceId != 0)
                    {
                        try
                        {
                            var guild = this.client.GetGuild(Convert.ToUInt64(guildSetting.Key));
                            var voiceChannel = guild != null ? await guild.GetVoiceChannelAsync(guildSetting.Value.VoiceId) : null;
                            if (voiceChannel != null)
                            {
                                await this.audioManager.JoinAudioAsync(voiceChannel);
                            }
                        }
                        catch (Exception ex)
                        {
                            // TODO: proper logging
                            Console.WriteLine(ex);
                        }
                    }
                }
            }).Forget();*/

            return Task.CompletedTask;
        }

        private async void StatsTimerAsync(object state)
        {
            if (!string.IsNullOrEmpty(this.Config.Discord.DiscordBotsKey) && this.client != null)
            {
                try
                {
                    var result = await "https://bots.discord.pw"
                        .AppendPathSegment($"api/bots/{client.CurrentUser.Id}/stats")
                        .WithHeader("Authorization", this.Config.Discord.DiscordBotsKey)
                        .PostJsonAsync(new { shard_id = client.ShardId, shard_count = this.Config.Discord.ShardCount, server_count = client.Guilds.Count() });
                }
                catch (Exception ex)
                {
                    // TODO: Update to using one of the logging classes (Discord/IRC)
                    Console.WriteLine($"Failed to update bots.discord.pw stats: {ex}");
                }
            }

            if (!string.IsNullOrEmpty(this.Config.Discord.CarbonStatsKey))
            {
                try
                {
                    var result = await "https://www.carbonitex.net"
                        .AppendPathSegment("/discord/data/botdata.php")
                        .PostJsonAsync(new { key = this.Config.Discord.CarbonStatsKey, shard_id = client.ShardId, shard_count = this.Config.Discord.ShardCount, servercount = client.Guilds.Count() });
                }
                catch (Exception ex)
                {
                    // TODO: Update to using one of the logging classes (Discord/IRC)
                    Console.WriteLine($"Failed to update carbon stats: {ex}");
                }
            }

            if (!string.IsNullOrEmpty(this.Config.Discord.DiscordListKey) && this.shard == 0)
            {
                try
                {
                    using (var httpClient = new HttpClient())
                    {
                        httpClient.BaseAddress = new Uri("https://bots.discordlist.net");
                        var content = new FormUrlEncodedContent(new[]
                        {
                            new KeyValuePair<string, string>("token", this.Config.Discord.DiscordListKey),
                            new KeyValuePair<string, string>("servers", (this.client.Guilds.Count() * this.Config.Discord.ShardCount).ToString()),
                        });

                        var result = await httpClient.PostAsync("/api", content);
                        if (result.IsSuccessStatusCode)
                        {
                            Console.WriteLine($"Updated discordlist.net server count to {this.client.Guilds.Count() * this.Config.Discord.ShardCount}");
                        }
                        else
                        {
                            Console.WriteLine(await result.Content.ReadAsStringAsync());
                        }
                    }
                }
                catch (Exception ex)
                {
                    // TODO: Update to using one of the logging classes (Discord/IRC)
                    Console.WriteLine($"Failed to update discordlist.net stats: {ex}");
                }
            }

            if (!string.IsNullOrEmpty(this.Config.Discord.DiscordBotsOrgKey))
            {
                try
                {
                    var result = await $"https://discordbots.org/api/bots/{client.CurrentUser.Id}/stats"
                        .WithHeader("Authorization", this.Config.Discord.DiscordBotsOrgKey)
                        .PostJsonAsync(new { shard_id = client.ShardId, shard_count = this.Config.Discord.ShardCount, server_count = client.Guilds.Count() });
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Failed to update discordbots.org stats: {ex}");
                }
            }
        }

        private async Task Client_UserVoiceStateUpdatedAsync(SocketUser arg1, SocketVoiceState arg2, SocketVoiceState arg3)
        {
            // voice state detection
            var guildUser = (arg1 as IGuildUser);
            var botGuildUser = await guildUser.Guild.GetCurrentUserAsync();
            if (guildUser.Id != botGuildUser.Id) // ignore joins/leaves from the bot
            {
                if (arg2.VoiceChannel != arg3.VoiceChannel && arg3.VoiceChannel == botGuildUser.VoiceChannel)
                {
                    // if they are connecting for the first time, wait a moment to account for possible conncetion delay. otherwise play immediately.
                    if (arg2.VoiceChannel == null)
                    {
                        await Task.Delay(1000);
                    }

                    Task.Run(async () =>
                    {
                        try
                        {
                            await this.audioManager.SendAudioAsync(guildUser, arg3.VoiceChannel, VoicePhraseType.UserJoin);
                        }
                        catch (Exception ex)
                        {
                            // TODO: proper logging
                            Console.WriteLine(ex);
                        }
                    }).Forget();
                }
                else if (arg2.VoiceChannel != arg3.VoiceChannel && arg2.VoiceChannel == botGuildUser.VoiceChannel)
                {
                    Task.Run(async () =>
                    {
                        try
                        {
                            await this.audioManager.SendAudioAsync(guildUser, arg2.VoiceChannel, VoicePhraseType.UserLeave);
                        }
                        catch (Exception ex)
                        {
                            // TODO: proper logging
                            Console.WriteLine(ex);
                        }
                    }).Forget();
                }
            }

            // mod logging
            var settings = SettingsConfig.GetSettings(guildUser.GuildId);
            if (settings.Mod_LogId != 0)
            {
                if (this.client.GetChannel(settings.Mod_LogId) is ITextChannel modLogChannel && botGuildUser.GetPermissions(modLogChannel).SendMessages)
                {
                    if (settings.HasFlag(ModOptions.Mod_LogUserLeaveVoice) && arg2.VoiceChannel != null && arg2.VoiceChannel.Id != arg3.VoiceChannel?.Id)
                    {
                        modLogChannel.SendMessageAsync($"{ guildUser.Username} left voice channel { arg2.VoiceChannel.Name}").Forget();
                    }

                    if (settings.HasFlag(ModOptions.Mod_LogUserJoinVoice) && arg3.VoiceChannel != null && arg3.VoiceChannel.Id != arg2.VoiceChannel?.Id)
                    {
                        modLogChannel.SendMessageAsync($"{guildUser.Username} joined voice channel {arg3.VoiceChannel.Name}").Forget();
                    }
                }
                else
                {
                    await guildUser.Guild.SendOwnerDMAsync($"Permissions error detected for {guildUser.Guild.Name} on voice channel updates: Can't send messages to configured mod logging channel.");
                }
            }
        }

        private async Task Client_JoinedGuildAsync(SocketGuild arg)
        {
            if (this.isReady)
            {
                this.AppInsights?.TrackEvent("serverJoin");

                var defaultChannel = arg.DefaultChannel;
                var owner = arg.Owner;
                if (arg.CurrentUser != null && arg.CurrentUser.GetPermissions(defaultChannel).SendMessages)
                {
                    await defaultChannel.SendMessageAsync($"(HELLO, I AM UB3R-B0T! .halp for info. {owner.Mention} you're the kickass owner-- you can use .admin to configure some stuff.)");
                }
            }
        }

        private async Task Client_GuildMemberUpdatedAsync(SocketGuildUser arg1, SocketGuildUser arg2)
        {
            if (arg1 is IGuildUser guildUserBefore && arg2 is IGuildUser guildUserAfter)
            {
                // Mod log
                var settings = SettingsConfig.GetSettings(guildUserBefore.GuildId);
                if (settings.Mod_LogId != 0)
                {
                    if (settings.HasFlag(ModOptions.Mod_LogUserRole))
                    {
                        var rolesAdded = new List<string>();
                        foreach (ulong roleId in guildUserAfter.RoleIds)
                        {
                            if (!guildUserBefore.RoleIds.Any(r => r == roleId))
                            {
                                rolesAdded.Add(guildUserAfter.Guild.Roles.First(g => g.Id == roleId).Name.TrimStart('@'));
                            }
                        }

                        var rolesRemoved = new List<string>();
                        foreach (ulong roleId in guildUserBefore.RoleIds)
                        {
                            if (!guildUserAfter.RoleIds.Any(r => r == roleId))
                            {
                                rolesRemoved.Add(guildUserBefore.Guild.Roles.First(g => g.Id == roleId).Name.TrimStart('@'));
                            }
                        }

                        if (rolesAdded.Count > 0 || rolesRemoved.Count > 0)
                        {
                            var modLogChannel = this.client.GetChannel(settings.Mod_LogId) as ITextChannel;
                            var botUser = (modLogChannel?.Guild as SocketGuild).CurrentUser;
                            if (botUser.GetPermissions(modLogChannel).SendMessages)
                            {
                                if (rolesAdded.Count > 0)
                                {
                                    string roleText = $"**{guildUserAfter.Username}#{guildUserAfter.Discriminator}** had these roles added: `{string.Join(",", rolesAdded)}`";
                                    await modLogChannel?.SendMessageAsync(roleText.Substring(0, Math.Min(roleText.Length, 2000)));
                                }

                                if (rolesRemoved.Count > 0)
                                {
                                    string roleText = $"**{guildUserAfter.Username}#{guildUserAfter.Discriminator}** had these roles removed: `{string.Join(",", rolesRemoved)}`";
                                    await modLogChannel?.SendMessageAsync(roleText.Substring(0, Math.Min(roleText.Length, 2000)));
                                }
                            }
                            else
                            {
                                await guildUserBefore.Guild.SendOwnerDMAsync($"Permissions error detected for {guildUserBefore.Guild.Name} on user role updates: Can't send messages to configured mod logging channel.");
                            }
                        }
                    }

                    if (settings.Mod_LogId != 0 && settings.HasFlag(ModOptions.Mod_LogUserNick))
                    {
                        if (guildUserAfter.Nickname != guildUserBefore.Nickname)
                        {
                            var modLogChannel = this.client.GetChannel(settings.Mod_LogId) as ITextChannel;
                            var botUser = (modLogChannel?.Guild as SocketGuild).CurrentUser;
                            if (botUser.GetPermissions(modLogChannel).SendMessages)
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
                            else
                            {
                                await guildUserBefore.Guild.SendOwnerDMAsync($"Permissions error detected for {guildUserBefore.Guild.Name} on user name updates: Can't send messages to configured mod logging channel.");
                            }
                        }
                    }
                }
            }
        }

        private async Task Client_UserBannedAsync(SocketUser arg1, SocketGuild arg2)
        {
            // mod log
            var settings = SettingsConfig.GetSettings(arg2.Id);
            if (settings.Mod_LogId != 0 && settings.HasFlag(ModOptions.Mod_LogUserBan))
            {
                string banText = $"{arg1.Username}#{arg1.Discriminator} was banned.";
                var modLogChannel = this.client.GetChannel(settings.Mod_LogId) as ITextChannel;

                if (arg2.CurrentUser.GetPermissions(modLogChannel).SendMessages)
                {
                    await modLogChannel.SendMessageAsync(banText);
                }
                else
                {
                    await arg2.SendOwnerDMAsync($"Permissions error detected for {arg2.Name} on user bans: Can't send messages to configured mod logging channel.");
                }
            }
        }

        private async Task Client_ReactionAdded(Cacheable<IUserMessage, ulong> arg1, ISocketMessageChannel arg2, SocketReaction arg3)
        {
            // if an Eye emoji was added, let's process it
            if ((arg3.Emoji.Name == "👁" || arg3.Emoji.Name == "🖼") && 
                arg3.Message.IsSpecified && 
                IsAuthorPatron(arg3.UserId) && 
                string.IsNullOrEmpty(arg3.Message.Value.Content) && 
                arg3.Message.Value.Attachments.Count > 0)
            {
                await this.HandleMessageAsync(arg3.Message.Value, arg3.Emoji.Name);
            }
        }

        private async Task Client_MessageUpdatedAsync(Cacheable<IMessage, ulong> arg1, SocketMessage arg2, ISocketMessageChannel arg3)
        {
            if (arg2 != null && arg2.Channel != null && arg2.Channel is IGuildChannel guildChannel)
            {
                var textChannel = guildChannel as ITextChannel;
                var settings = SettingsConfig.GetSettings(guildChannel.Guild.Id);

                if (settings.Mod_LogId != 0 && settings.HasFlag(ModOptions.Mod_LogEdit) && arg2.Channel.Id != settings.Mod_LogId && !arg2.Author.IsBot)
                {
                    if (arg1.HasValue && arg1.Value.Content != arg2.Content && !string.IsNullOrEmpty(arg1.Value.Content))
                    {
                        string editText = $"**{arg2.Author.Username}** modified in {textChannel.Mention}: `{arg1.Value.Content}` to `{arg2.Content}`";
                        var botUser = (guildChannel.Guild as SocketGuild).CurrentUser;
                        if (botUser != null && this.client.GetChannel(settings.Mod_LogId) is ITextChannel modLogChannel && botUser.GetPermissions(modLogChannel).SendMessages)
                        {
                            await modLogChannel.SendMessageAsync(editText.Substring(0, Math.Min(editText.Length, Discord.DiscordConfig.MaxMessageSize)));
                        }
                        else
                        {
                            await guildChannel.Guild.SendOwnerDMAsync($"Permissions error detected for {guildChannel.Guild.Name} on message updates: Can't send messages to configured mod logging channel.");
                        }
                    }
                }
            }

            // if the message is from the last hour, see if we can re-process it.
            if (arg2 != null && arg1.HasValue && arg2.Content != arg1.Value.Content &&
                arg2.Author.Id != client.CurrentUser.Id &&
                DateTimeOffset.UtcNow.Subtract(arg2.Timestamp) < TimeSpan.FromHours(1))
            {
                await this.HandleMessageAsync(arg2);
            }
        }

        private async Task Client_MessageDeletedAsync(Cacheable<IMessage, ulong> arg1, ISocketMessageChannel arg2)
        {
            var msg = this.botResponsesCache.Remove(arg1.Id);
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

            if (arg1.HasValue && arg2 is IGuildChannel guildChannel)
            {
                var message = arg1.Value;
                var textChannel = guildChannel as ITextChannel;
                var settings = SettingsConfig.GetSettings(guildChannel.GuildId);

                if (settings.Mod_LogId != 0 && settings.HasFlag(ModOptions.Mod_LogDelete) && guildChannel.Id != settings.Mod_LogId && !message.Author.IsBot)
                {
                    string delText = "";

                    if (settings.WordCensors.Count() > 0)
                    {
                        var messageWords = message.Content.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                        foreach (var word in messageWords)
                        {
                            if (settings.WordCensors.Contains(word, StringComparer.OrdinalIgnoreCase))
                            {
                                delText = "```Word Censor Triggered```";
                            }
                        }
                    }

                    delText += $"**{message.Author.Username}#{message.Author.Discriminator}** deleted in {textChannel.Mention}: {message.Content}";

                    var botUser = (guildChannel.Guild as SocketGuild).CurrentUser;
                    if (botUser != null && this.client.GetChannel(settings.Mod_LogId) is ITextChannel modLogChannel && botUser.GetPermissions(modLogChannel).SendMessages)
                    {
                        modLogChannel.SendMessageAsync(delText.Substring(0, Math.Min(delText.Length, Discord.DiscordConfig.MaxMessageSize))).Forget();
                    }
                    else
                    {
                        guildChannel.Guild.SendOwnerDMAsync($"Permissions error detected for {guildChannel.Guild.Name} on message deletes: Can't send messages to configured mod logging channel.").Forget();
                    }
                }
            }
        }

        public Task Discord_OnMessageReceivedAsync(SocketMessage socketMessage)
        {
            Task.Run(async () =>
            {
                try
                {
                    await this.HandleMessageAsync(socketMessage);
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex);
                    this.AppInsights?.TrackException(ex);
                    this.ReportError(LogSeverity.Error, $":warning: shard {this.shard}: " + ex.ToString().Substring(0, Math.Min(ex.ToString().Length, 1950)));
                }
            }).Forget();

            return Task.CompletedTask;
        }

        // TODO: this method is waaaaaaay too huge
        private async Task HandleMessageAsync(SocketMessage socketMessage, string ocrType = null)
        {
            messageCount++;

            // Ignore system and our own messages.
            var message = socketMessage as SocketUserMessage;
            bool isOutbound = false;

            // replicate to webhook, if configured
            this.CallOutgoingWebhookAsync(message).Forget();

            if (message == null || (isOutbound = message.Author.Id == client.CurrentUser.Id))
            {
                if (isOutbound)
                {
                    if (message.Embeds?.Count > 0)
                    {
                        consoleLogger.Log(LogType.Outgoing, $"\tSending [embed content] to {message.Channel.Name}");
                    }
                    else
                    {
                        consoleLogger.Log(LogType.Outgoing, $"\tSending to {message.Channel.Name}: {message.Content}");
                    }
                }

                return;
            }

            // grab the settings for this server
            var botGuildUser = (message.Channel is IGuildChannel guildChannel) ? await guildChannel.GetUserAsync(client.CurrentUser.Id) : null;
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
            if (settings.WordCensors.Count() > 0 && botGuildUser != null && botGuildUser.GuildPermissions.ManageMessages)
            {
                var messageWords = message.Content.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var word in messageWords)
                {
                    if (settings.WordCensors.Contains(word, StringComparer.OrdinalIgnoreCase))
                    {
                        await message.DeleteAsync();
                        var dmChannel = await message.Author.CreateDMChannelAsync();
                        await dmChannel.SendMessageAsync($"hi uh sorry but your most recent message was tripped up by the word `{word}` and thusly was deleted. complain to management, i'm just the enforcer");
                        return;
                    }
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

            // Update the seen data
            // TODO: Pull this out to common area to share with IRC
            if (settings.SeenEnabled && textChannel != null && !string.IsNullOrEmpty(message.Content) && this.Config.SeenEndpoint != null)
            {
                var messageText = message.Content;
                if (messageText.Length > 256)
                {
                    messageText = message.Content.Substring(0, 253) + "...";
                }

                seenUsers["" + textChannel.Id + message.Author.Id] = new SeenUserData
                {
                    Name = message.Author.Id.ToString(),
                    Channel = textChannel.Id.ToString(),
                    Server = textChannel.Guild.Id.ToString(),
                    Text = messageText,
                    Timestamp = Utilities.Utime,
                };
            }

            var httpMatch = httpRegex.Match(message.Content);
            if (httpMatch.Success)
            {
                this.urls[message.Channel.Id.ToString()] = httpMatch.Value;
            }

            string messageContent = message.Content;
            // OCR for fun if requested (patrons only)
            // TODO: need to drive this via config
            if (!string.IsNullOrEmpty(ocrType) && string.IsNullOrEmpty(message.Content) && message.Attachments?.FirstOrDefault()?.Url is string attachmentUrl)
            {
                string newMessageContent = string.Empty;

                if (ocrType == "👁")
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
                else if (ocrType == "🖼")
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

                messageContent = newMessageContent ?? messageContent;
            }

            // TODO: Pull this out to common area to share with IRC
            if (settings.FunResponsesEnabled && !string.IsNullOrEmpty(message.Content))
            {
                var repeat = repeatData.GetOrAdd(message.Channel.Id.ToString(), new RepeatData());
                if (string.Equals(repeat.Text, message.Content, StringComparison.OrdinalIgnoreCase))
                {
                    if (!repeat.Nicks.Contains(message.Author.Id.ToString()))
                    {
                        repeat.Nicks.Add(message.Author.Id.ToString());
                    }

                    if (repeat.Nicks.Count == 3)
                    {
                        await this.RespondAsync(message, message.Content);
                        repeat.Reset(string.Empty, string.Empty);
                    }
                }
                else
                {
                    repeat.Reset(message.Author.Id.ToString(), message.Content);
                }
            }

            // If it's a command, match that before anything else.
            string query = string.Empty;
            bool hasBotMention = message.MentionedUsers.Any(u => u.Id == client.CurrentUser.Id);

            int argPos = 0;
            if (message.HasMentionPrefix(client.CurrentUser, ref argPos))
            {
                query = messageContent.Substring(argPos);
            }
            else if (messageContent.StartsWith(settings.Prefix))
            {
                query = messageContent.Substring(settings.Prefix.Length);
            }

            string command = query.Split(new[] { ' ' }, 2)?[0];

            // if it's a blocked command, bail
            if (settings.IsCommandDisabled(CommandsConfig.Instance, command) && !IsAuthorOwner(message))
            {
                return;
            }

            // Check discord specific commands prior to general ones.
            if (!string.IsNullOrEmpty(command) && discordCommands.Commands.ContainsKey(command))
            {
                // make sure we're not rate limited
                var commandKey = command + guildId;
                var commandCount = this.commandsIssued.AddOrUpdate(commandKey, 1, (key, val) =>
                {
                    return val + 1;
                });

                if (commandCount > 10)
                {
                    await this.RespondAsync(message, "rate limited try later");
                }
                else
                {
                    var response = await discordCommands.Commands[command].Invoke(message).ConfigureAwait(false);
                    if (response != null && (!string.IsNullOrEmpty(response.Text) || response.Embed != null))
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

                var messageData = BotMessageData.Create(message, query, settings);
                messageData.Content = messageContent;

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

                        var result = await webhook.Endpoint.ToString().PostJsonAsync(new { text = text, username = message.Author.Username });
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Outgoing webhook failed {ex}");
                    }
                }
            }
        }

        private async Task Discord_LeftGuildAsync(SocketGuild arg)
        {
            this.AppInsights.TrackEvent("serverLeave");

            if (this.Config.PruneEndpoint != null)
            {
                var req = WebRequest.Create($"{this.Config.PruneEndpoint}?id={arg.Id}");
                await req.GetResponseAsync();
            }

            Task.Run(async () =>
            {
                try
                {
                    await audioManager.LeaveAudioAsync(arg.Id);
                }
                catch (Exception ex)
                {
                    // TODO: proper logging
                    Console.WriteLine(ex);
                }
            }).Forget();
        }

        private async Task Discord_UserLeftAsync(SocketGuildUser arg)
        {
            var settings = SettingsConfig.GetSettings(arg.Guild.Id);

            if (!string.IsNullOrEmpty(settings.Farewell))
            {
                var farewell = settings.Farewell.Replace("%user%", arg.Mention);

                farewell = channelRegex.Replace(farewell, new MatchEvaluator((Match chanMatch) =>
                {
                    string channelName = chanMatch.Captures[0].Value;
                    var channel = arg.Guild.Channels.Where(c => c.Name == channelName).FirstOrDefault();

                    if (channel != null)
                    {
                        return ((ITextChannel)channel).Mention;
                    }

                    return channelName;
                }));

                var farewellChannel = this.client.GetChannel(settings.FarewellId) as ITextChannel ?? arg.Guild.DefaultChannel;
                var botUser = (farewellChannel.Guild as SocketGuild).CurrentUser;

                if (botUser != null && botUser.GetPermissions(farewellChannel).SendMessages)
                {
                    await farewellChannel.SendMessageAsync(farewell);
                }
                else
                {
                    await farewellChannel.Guild.SendOwnerDMAsync($"Permissions error detected for {farewellChannel.Guild.Name}: Can't send messages to configured farewell channel.");
                }
            }

            // mod log
            if (settings.Mod_LogId != 0 && settings.HasFlag(ModOptions.Mod_LogUserLeave))
            {
                var botUser = arg.Guild.CurrentUser;
                string leaveText = $"{arg.Username}#{arg.Discriminator} left.";
                if (this.client.GetChannel(settings.Mod_LogId) is ITextChannel modLogChannel && botUser != null && botUser.GetPermissions(modLogChannel).SendMessages)
                {
                    await modLogChannel.SendMessageAsync(leaveText);
                }
                else
                {
                    await arg.Guild.SendOwnerDMAsync($"Permissions error detected for {arg.Guild.Name} on user leave: Can't send messages to configured mod logging channel.");
                }
            }
        }

        private async Task Discord_UserJoinedAsync(SocketGuildUser arg)
        {
            var settings = SettingsConfig.GetSettings(arg.Guild.Id);

            if (!string.IsNullOrEmpty(settings.Greeting))
            {
                var greeting = settings.Greeting.Replace("%user%", arg.Mention);

                greeting = channelRegex.Replace(greeting, new MatchEvaluator((Match chanMatch) =>
                {
                    string channelName = chanMatch.Groups[1].Value;
                    var channel = arg.Guild.Channels.Where(c => c is ITextChannel && c.Name.Equals(channelName, StringComparison.OrdinalIgnoreCase)).FirstOrDefault();

                    if (channel != null)
                    {
                        return ((ITextChannel)channel).Mention;
                    }

                    return channelName;
                }));

                var greetingChannel = this.client.GetChannel(settings.GreetingId) as ITextChannel ?? arg.Guild.DefaultChannel;
                var botUser = (greetingChannel.Guild as SocketGuild).CurrentUser;
                if (botUser != null && botUser.GetPermissions(greetingChannel).SendMessages)
                {
                    await greetingChannel.SendMessageAsync(greeting);
                }
                else
                {
                    await greetingChannel.Guild.SendOwnerDMAsync($"Permissions error detected for {greetingChannel.Guild.Name}: Can't send messages to configured greeting channel.");
                }
            }

            if (settings.JoinRoleId != 0 && arg.Guild.CurrentUser.GuildPermissions.ManageRoles)
            {
                var role = arg.Guild.GetRole(settings.JoinRoleId);
                if (role != null)
                {
                    try
                    {
                        await arg.AddRolesAsync(new[] { role });
                    }
                    catch (HttpException ex) when (ex.HttpCode == HttpStatusCode.Forbidden)
                    {
                        await arg.Guild.SendOwnerDMAsync($"Permissions error detected for {arg.Guild.Name}: Auto role add on user joined failed, role `{role.Name}` is higher in order than my role");
                    }
                    catch (HttpException ex) when (ex.HttpCode == HttpStatusCode.NotFound)
                    {
                        await arg.Guild.SendOwnerDMAsync($"Error detected for {arg.Guild.Name}: Auto role add on user joined failed, role `{role.Name}` does not exist");
                    }
                }
            }

            // mod log
            if (settings.Mod_LogId != 0 && settings.HasFlag(ModOptions.Mod_LogUserJoin))
            {
                string joinText = $"{arg.Username}#{arg.Discriminator} joined.";
                if (this.client.GetChannel(settings.Mod_LogId) is ITextChannel modLogChannel && arg.Guild.CurrentUser.GetPermissions(modLogChannel).SendMessages)
                {
                    await modLogChannel.SendMessageAsync(joinText);
                }
                else
                {
                    await arg.Guild.SendOwnerDMAsync($"Permissions error detected for {arg.Guild.Name} on user join: Can't send messages to configured mod logging channel.");
                }
            }
        }

        private Task Discord_Log(LogMessage arg)
        {
            // TODO: Temporary filter for audio warnings; remove with future Discord.NET update
            if (arg.Message != null && arg.Message.Contains("Unknown OpCode") || (arg.Source != null && arg.Source.Contains("Audio") && arg.Message != null && (arg.Message.Contains("Latency = "))))
            {
                return Task.CompletedTask;
            }

            if (arg.Severity <= LogSeverity.Warning && arg.Message != null)
            {
                this.ReportError(arg.Severity, arg.Message);
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

            this.consoleLogger.Log(logType, arg.ToString());

            return Task.CompletedTask;
        }

        private void ReportError(LogSeverity severity, string message)
        {
            if (!string.IsNullOrEmpty(message))
            {
                message = message.ToLowerInvariant();
                if (!message.ToLowerInvariant().Contains("rate limit") && !message.Contains("referenced an unknown"))
                {
                    try
                    {
                        string messageContent = $":information_source: {severity} on shard {this.shard}: {message}";
                        this.Config.AlertEndpoint?.ToString().PostJsonAsync(new { content = messageContent });
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine("Error sending log data: " + ex);
                    }
                }
            }
        }
    }
}
