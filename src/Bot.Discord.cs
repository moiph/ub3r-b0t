namespace UB3RB0T
{
    using Discord;
    using Discord.Audio;
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
                AudioMode = AudioMode.Outgoing,
                LogLevel = LogSeverity.Verbose,
                MessageCacheSize = 500,
            });

            client.MessageReceived += Discord_OnMessageReceivedAsync;
            client.Log += Discord_Log;
            client.UserJoined += Discord_UserJoinedAsync;
            client.UserLeft += Discord_UserLeftAsync;
            client.JoinedGuild += Client_JoinedGuildAsync;
            client.LeftGuild += Discord_LeftGuildAsync;
            client.MessageDeleted += Client_MessageDeletedAsync;
            client.MessageUpdated += Client_MessageUpdatedAsync;
            client.UserBanned += Client_UserBannedAsync;
            client.UserUpdated += Client_UserUpdatedAsync;
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
            await client.ConnectAsync();
            await this.client.SetGameAsync(this.Config.Discord.Status);

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
        }

        private async Task Client_UserVoiceStateUpdatedAsync(SocketUser arg1, SocketVoiceState arg2, SocketVoiceState arg3)
        {
            // voice state detection
            var guildUser = (arg1 as IGuildUser);
            var botGuildUser = await guildUser.Guild.GetCurrentUserAsync();
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

        private async Task Client_JoinedGuildAsync(SocketGuild arg)
        {
            if (this.isReady)
            {
                this.AppInsights?.TrackEvent("serverJoin");

                //var defaultChannel = await arg.GetDefaultChannelAsync(); // arg.DefaultChannel;
                var defaultChannel = arg.DefaultChannel;
                //var owner = await arg.GetOwnerAsync(); // arg.Owner;
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
                if (settings.Mod_LogId != 0 && settings.HasFlag(ModOptions.Mod_LogUserRole))
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
            }
        }

        private async Task Client_UserUpdatedAsync(SocketUser arg1, SocketUser arg2)
        {
            if (arg1 is IGuildUser guildUserBefore && arg2 is IGuildUser guildUserAfter)
            {
                // mod log
                var settings = SettingsConfig.GetSettings(guildUserBefore.GuildId);
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

        private async Task Client_MessageUpdatedAsync(Optional<SocketMessage> arg1, SocketMessage arg2)
        {
            if (arg2 != null && arg2.Channel != null && arg2.Channel is IGuildChannel guildChannel)
            {
                var textChannel = guildChannel as ITextChannel;
                var settings = SettingsConfig.GetSettings(guildChannel.Guild.Id);

                if (settings.Mod_LogId != 0 && settings.HasFlag(ModOptions.Mod_LogEdit) && arg2.Channel.Id != settings.Mod_LogId && !arg2.Author.IsBot)
                {
                    if (arg1.IsSpecified && arg1.Value.Content != arg2.Content && !string.IsNullOrEmpty(arg1.Value.Content))
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
        }

        private async Task Client_MessageDeletedAsync(ulong arg1, Optional<SocketMessage> arg2)
        {
            var msg = this.botResponsesCache.Remove(arg1);
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

            if (arg2.IsSpecified && arg2.Value.Channel is IGuildChannel guildChannel)
            {
                var message = arg2.Value;
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

        public async Task Discord_OnMessageReceivedAsync(SocketMessage socketMessage)
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
                    consoleLogger.Log(LogType.Outgoing, $"\tSending to {message.Channel.Name}: {message.Content}");
                }

                return;
            }

            // grab the settings for this server
            var botGuildUser = (message.Channel is IGuildChannel guildChannel) ? await guildChannel.GetUserAsync(client.CurrentUser.Id) : null;
            var guildUser = message.Author as IGuildUser;
            var guildId = guildUser?.GuildId;
            var settings = SettingsConfig.GetSettings(guildId?.ToString());

            // if it's a globally blocked server, ignore it unless it's the owner
            if (message.Author.Id != this.Config.Discord.OwnerId && guildId != null && this.Config.Discord.BlockedServers.Contains(guildId.Value))
            {
                return;
            }

            // if the user is blocked based on role, return
            var botlessRoleId = guildUser?.Guild.Roles.FirstOrDefault(r => r.Name.ToLowerInvariant() == "botless")?.Id;
            if ((message.Author as IGuildUser)?.RoleIds.Any(r => botlessRoleId != null && r == botlessRoleId.Value) ?? false)
            {
                return;
            }

            // Bail out with help info if it's a PM
            if (message.Channel is IDMChannel && (message.Content.Contains("help") || message.Content.Contains("info") || message.Content.Contains("commands")))
            {
                await message.Channel.SendMessageAsync("Info and commands can be found at: https://ub3r-b0t.com");
                return;
            }

            var textChannel = message.Channel as ITextChannel;
            if (botGuildUser != null && !botGuildUser.GetPermissions(textChannel).SendMessages)
            {
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
                        await message.Channel.SendMessageAsync(message.Content);
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
                query = message.Content.Substring(argPos);
            }
            else if (message.Content.StartsWith(settings.Prefix))
            {
                query = message.Content.Substring(settings.Prefix.Length);
            }

            string command = query.Split(new[] { ' ' }, 2)?[0];

            // if it's a blocked command, bail
            if (settings.IsCommandDisabled(CommandsConfig.Instance, command))
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
                    await message.Channel.SendMessageAsync("rate limited try later");
                }
                else
                {
                    var sentMessage = await discordCommands.Commands[command].Invoke(message).ConfigureAwait(false);
                    if (sentMessage != null)
                    {
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

                try
                {
                    BotResponseData responseData = await this.ProcessMessageAsync(BotMessageData.Create(message, query, settings), settings);

                    if (responseData.Embed != null)
                    {
                        var sentMessage = await message.Channel.SendMessageAsync(((char)1).ToString(), false, responseData.Embed.CreateEmbedBuilder());
                        this.botResponsesCache.Add(message.Id, sentMessage);
                    }
                    else
                    {
                        foreach (string response in responseData.Responses)
                        {
                            if (!string.IsNullOrEmpty(response))
                            {
                                var sentMessage = await message.Channel.SendMessageAsync(response.Substring(0, Math.Min(response.Length, 2000)));
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
                        await arg.AddRolesAsync(role);
                    }
                    catch (HttpException ex) when (ex.HttpCode == HttpStatusCode.Forbidden)
                    {
                        await arg.Guild.SendOwnerDMAsync($"Permissions error detected for {arg.Guild.Name}: Auto role add on user joined failed, role `{role.Name}` is higher in order than my role");
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
            if (arg.Message.Contains("Unknown OpCode (Speaking)") || (arg.Source.Contains("Audio") && arg.Message.Contains("Latency = ")))
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

            this.consoleLogger.Log(logType, arg.ToString());

            return Task.CompletedTask;
        }
    }
}
