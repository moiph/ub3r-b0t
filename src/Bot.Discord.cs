namespace UB3RB0T
{
    using Discord;
    using Discord.Audio;
    using Discord.WebSocket;
    using Flurl;
    using Flurl.Http;
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Net;
    using System.Text.RegularExpressions;
    using System.Threading;
    using System.Threading.Tasks;
    using UB3RIRC;

    public partial class Bot
    {
        private MessageCache BotResponsesCache = new MessageCache();

        public async Task CreateDiscordBotAsync()
        {
            client = new DiscordSocketClient(new DiscordSocketConfig
            {
                ShardId = this.shard,
                TotalShards = this.Config.Discord.ShardCount,
                AudioMode = AudioMode.Outgoing,
                LogLevel = LogSeverity.Verbose,
            });

            client.MessageReceived += Discord_OnMessageReceivedAsync;
            client.Log += Discord_Log;
            client.UserJoined += Discord_UserJoinedAsync;
            client.UserLeft += Discord_UserLeftAsync;
            client.LeftGuild += Discord_LeftGuildAsync;
            client.MessageDeleted += Client_MessageDeleted;

            // If user customizeable server settings are supported...support them
            // Currently discord only.
            if (this.Config.SettingsEndpoint != null)
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
            await this.client.SetGame(this.Config.Discord.Status);

            if (!string.IsNullOrEmpty(this.Config.Discord.DiscordBotsKey) || !string.IsNullOrEmpty(this.Config.Discord.CarbonStatsKey))
            {
                statsTimer = new Timer(async (object state) =>
                {
                    if (!string.IsNullOrEmpty(this.Config.Discord.DiscordBotsKey))
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

                }, null, 3600000, 3600000);
            }
        }

        private async Task Client_MessageDeleted(ulong arg1, Optional<SocketMessage> arg2)
        {
            var msg = BotResponsesCache.Remove(arg1);
            if (msg != null)
            {
                await msg.DeleteAsync();
            }
        }

        public async Task Discord_OnMessageReceivedAsync(SocketMessage socketMessage)
        {
            // Ignore system and our own messages.
            var message = socketMessage as SocketUserMessage;
            bool isOutbound = false;
            if (message == null || (isOutbound = message.Author.Id == client.CurrentUser.Id))
            {
                if (isOutbound)
                {
                    consoleLogger.Log(LogType.Outgoing, $"\tSending to {message.Channel.Name}: {message.Content}");
                }

                return;
            }

            // grab the settings for this server
            var botGuildUser = await (message.Channel as IGuildChannel).GetUserAsync(client.CurrentUser.Id);
            var guildUser = message.Author as IGuildUser;
            var guildId = guildUser?.GuildId;
            var settings = SettingsConfig.GetSettings(guildId?.ToString());

            // if it's a globally blocked server, ignore it unless it's the owner
            if (message.Author.Id != this.Config.Discord.OwnerId && guildId != null && this.Config.Discord.BlockedServers.Contains(guildId.Value))
            {
                return;
            }

            // validate server settings don't block this channel;
            // if the ID is in there and it's block, bail. if it's not in there and it's allow mode, also bail.
            if (settings.Channels.Contains(socketMessage.Channel.Id.ToString()) && settings.IsChannelListBlock)
            {
                return;
            }
            else if (!settings.Channels.Contains(socketMessage.Channel.Id.ToString()) && !settings.IsChannelListBlock)
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
            if (!string.IsNullOrEmpty(command) && new DiscordCommands().Commands.ContainsKey(command))
            {
                await new DiscordCommands().Commands[command].Invoke(message);
            }
            else
            {
                IDisposable typingState = null;
                if (CommandsConfig.Instance.Commands.ContainsKey(command))
                {
                    typingState = message.Channel.EnterTypingState();
                }

                List<string> responses = await this.ProcessMessageAsync(BotMessageData.Create(message, query), settings);

                foreach (string response in responses)
                {
                    if (!string.IsNullOrEmpty(response))
                    {
                        var sentMessage = await message.Channel.SendMessageAsync(response);
                        BotResponsesCache.Add(message.Id, sentMessage);
                    }
                }

                typingState?.Dispose();
            }
        }

        private async Task Discord_LeftGuildAsync(SocketGuild arg)
        {
            if (this.Config.PruneEndpoint != null)
            {
                var req = WebRequest.Create($"{this.Config.PruneEndpoint}?id={arg.Id}");
                await req.GetResponseAsync();
            }
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

                var farewellChannel = this.client.GetChannel(settings.FarewellId) as ITextChannel ?? await arg.Guild.GetDefaultChannelAsync();

                var botGuildUser = await farewellChannel.GetUserAsync(arg.Discord.CurrentUser.Id);

                if (botGuildUser.GetPermissions(farewellChannel).SendMessages)
                {
                    await farewellChannel.SendMessageAsync(farewell);
                }
                else
                {
                    await (await (await farewellChannel.Guild.GetOwnerAsync()).CreateDMChannelAsync()).SendMessageAsync($"Permissions error detected for {farewellChannel.Guild.Name}: Can't send messages to configured farewell channel.");
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
                    string channelName = chanMatch.Captures[0].Value;
                    var channel = arg.Guild.Channels.Where(c => c.Name == channelName).FirstOrDefault();

                    if (channel != null)
                    {
                        return ((ITextChannel)channel).Mention;
                    }

                    return channelName;
                }));

                var greetingChannel = this.client.GetChannel(settings.GreetingId) as ITextChannel ?? await arg.Guild.GetDefaultChannelAsync();

                var botGuildUser = await greetingChannel.GetUserAsync(arg.Discord.CurrentUser.Id);

                if (botGuildUser.GetPermissions(greetingChannel).SendMessages)
                {
                    await greetingChannel.SendMessageAsync(greeting);
                }
                else
                {
                    await (await (await greetingChannel.Guild.GetOwnerAsync()).CreateDMChannelAsync()).SendMessageAsync($"Permissions error detected for {greetingChannel.Guild.Name}: Can't send messages to configured greeting channel.");
                }
            }
        }

        private Task Discord_Log(LogMessage arg)
        {
            Console.WriteLine(arg.ToString());

            return Task.CompletedTask;
        }
    }
}
