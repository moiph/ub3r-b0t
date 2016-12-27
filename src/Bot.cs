namespace UB3RB0T
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Discord;
    using Discord.Audio;
    using Discord.WebSocket;
    using UB3RIRC;
    using Flurl;
    using Flurl.Http;
    using System.Collections.Concurrent;
    using System.Text.RegularExpressions;
    using System.Net;

    public class Bot
    {
        private int shard = 0;
        private BotType botType;
        private DiscordSocketClient client;

        // TODO: Wire up audio support once Discord.NET supports it.
        // private IAudioClient _audio;

        private Dictionary<string, IrcClient> ircClients;

        private ConcurrentDictionary<string, int> commandsIssued = new ConcurrentDictionary<string, int>();
        private static Regex channelRegex = new Regex("#([a-zA-Z0-9\\-]+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static Regex httpRegex = new Regex("https?://([^\\s]+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private Timer notificationsTimer;
        private Timer settingsUpdateTimer;
        private Timer remindersTimer;
        private Timer statsTimer;
        private Timer oneMinuteTimer;

        private Logger consoleLogger = Logger.GetConsoleLogger();

        // TODO: Genalize this API support -- currently specific to private API
        private BotApi BotApi;

        public Bot(BotType botType, int shard)
        {
            this.botType = botType;
            this.shard = shard;
        }

        public BotConfig Config => BotConfig.Instance;

        /// Initialize and connect to the desired clients, hook up event handlers.
        /// </summary>
        /// <summary>
        public async Task RunAsync()
        {
            notificationsTimer = new Timer(CheckNotifications, null, 10000, 10000);
            remindersTimer = new Timer(CheckReminders, null, 10000, 10000);

            oneMinuteTimer = new Timer(OneMinuteTimer, null, 60000, 60000);

            if (botType == BotType.Discord)
            {
                if (string.IsNullOrEmpty(this.Config.Discord.Token))
                {
                    throw new InvalidConfigException("Discord auth token is missing.");
                }

                client = new DiscordSocketClient(new DiscordSocketConfig
                {
                    ShardId = this.shard,
                    TotalShards = this.Config.Discord.ShardCount,
                    AudioMode = AudioMode.Outgoing,
                    LogLevel = LogSeverity.Verbose,
                });

                client.MessageReceived += OnMessageReceivedAsync;
                client.Log += Client_Log;
                client.UserJoined += Client_UserJoined;
                client.UserLeft += Client_UserLeft;
                client.LeftGuild += Client_LeftGuild;

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
            }
            else
            {
                if (this.Config.Irc.Servers == null)
                {
                    throw new InvalidConfigException("Irc server list is missing.");
                }

                if (this.Config.Irc.Servers.Any(s => s.Channels.Any(c => !c.StartsWith("#"))))
                {
                    throw new InvalidConfigException("Invalid channel specified; all channels should start with #.");
                }

                ircClients = new Dictionary<string, IrcClient>();

                foreach (IrcServer server in this.Config.Irc.Servers)
                {
                    var ircClient = new IrcClient(server.Id, this.Config.Name, server.Host, server.Port, /* useSsl -- pending support */false);
                    ircClients.Add(server.Id, ircClient);
                    ircClient.OnIrcEvent += this.OnIrcEventAsync;

                    await ircClient.ConnectAsync();
                }
            }

            if (this.botType == BotType.Discord &&
                (!string.IsNullOrEmpty(this.Config.Discord.DiscordBotsKey) || !string.IsNullOrEmpty(this.Config.Discord.CarbonStatsKey)))
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

            // If a custom API endpoint is supported...support it
            if (this.Config.ApiEndpoint != null)
            {
                this.BotApi = new BotApi(this.Config.ApiEndpoint, this.Config.ApiKey, this.botType);
            }

            string read = string.Empty;
            while (read != "exit")
            {
                read = Console.ReadLine();

                string[] argv = read.Split(new char[] { ' ' }, 4);

                switch (argv[0])
                {
                    case "reload":
                        JsonConfig.ConfigInstances.Clear();
                        Console.WriteLine("Config reloaded.");
                        break;

                    default:
                        break;
                }
            }

            await this.client.DisconnectAsync();
            Console.WriteLine("Exited.");
        }

        private async Task Client_LeftGuild(SocketGuild arg)
        {
            if (this.Config.PruneEndpoint != null)
            {
                var req = WebRequest.Create($"{this.Config.PruneEndpoint}?id={arg.Id}");
                await req.GetResponseAsync();
            }
        }

        private async Task Client_UserLeft(SocketGuildUser arg)
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
                await farewellChannel.SendMessageAsync(farewell);
            }
        }

        private async Task Client_UserJoined(SocketGuildUser arg)
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
                await greetingChannel.SendMessageAsync(greeting);
            }
        }

        private static void Heartbeat()
        {

        }

        private async Task UpdateSettingsAsync()
        {
            try
            {
                await SettingsConfig.Instance.OverrideAsync(this.Config.SettingsEndpoint);
            }
            catch (Exception ex)
            {
                // TODO: Update to using one of the logging classes (Discord/IRC)
                Console.WriteLine($"Failed to update server settings: {ex}");
            }
        }

        private void CheckReminders(object state)
        {

        }

        private void OneMinuteTimer(object state)
        {
            this.commandsIssued.Clear();
        }

        private void CheckNotifications(object state)
        {
            // ((ISocketMessageChannel)this.client.GetChannel(209556044123340801)).SendMessageAsync("notification");
        }

        public async Task OnMessageReceivedAsync(SocketMessage socketMessage)
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
            var guildId = (message.Author as IGuildUser)?.GuildId;
            var settings = SettingsConfig.GetSettings(guildId?.ToString());

            // if it's a blocked server, ignore it unless it's the owner
            if (message.Author.Id != this.Config.Discord.OwnerId && guildId != null && this.Config.Discord.BlockedServers.Contains(guildId.Value))
            {
                return;
            }

            // if the user is blocked based on role, return
            var botlessRoleId = (message.Author as IGuildUser).Guild.Roles.FirstOrDefault(r => r.Name.ToLowerInvariant() == "botless")?.Id;
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

            // Check discord specific commands prior to general ones.
            if (!string.IsNullOrEmpty(command) && new DiscordCommands().Commands.ContainsKey(command))
            {
                await new DiscordCommands().Commands[command].Invoke(message);
            }
            else
            {
                IDisposable typingState = null;
                if (CommandsConfig.Instance.Commands.Contains(command))
                {
                    typingState = message.Channel.EnterTypingState();
                }

                string[] responses = await this.ProcessMessageAsync(BotMessageData.Create(message), settings);

                if (responses != null && responses.Length > 0)
                {
                    foreach (string response in responses)
                    {
                        if (!string.IsNullOrEmpty(response))
                        {
                            await message.Channel.SendMessageAsync(response);
                        }
                    }
                }

                typingState?.Dispose();
            }
        }

        // TODO: IRC library needs... some improvements.
        public async void OnIrcEventAsync(MessageData data, IrcClient client)
        {
            if (data.Verb == ReplyCode.RPL_ENDOFMOTD || data.Verb == ReplyCode.RPL_NOMOTD) //  motd end or motd missing
            {
                foreach (string channel in this.Config.Irc.Servers.Where(s => s.Id == client.Id).First().Channels)
                {
                    client.Command("JOIN", channel, string.Empty);
                }
            }

            if (data.Verb == "PRIVMSG")
            {
                string[] responses = await this.ProcessMessageAsync(BotMessageData.Create(data, client));
                if (responses != null && responses.Length > 0)
                {
                    foreach (string response in responses)
                    {
                        client.Command("PRIVMSG", data.Target, response);
                    }
                }
            }
        }

        private async Task<string[]> ProcessMessageAsync(BotMessageData messageData)
        {
            return await ProcessMessageAsync(messageData, new Settings());
        }

        private async Task<string[]> ProcessMessageAsync(BotMessageData messageData, Settings settings)
        {
            string[] responses = new string[] { };
            string prefix = settings.Prefix;

            string query = messageData.Content;
            if (messageData.Content.StartsWith(prefix))
            {
                query = messageData.Content.Substring(prefix.Length, messageData.Content.Length - prefix.Length);
            }

            string[] queryParts = query.Split(new[] { ' ' });
            string command = queryParts[0];

            // Ignore if the command is disabled on this server
            if (settings.DisabledCommands.Contains(command))
            {
                return responses;
            }

            if (this.BotApi != null)
            {
                if (CommandsConfig.Instance.Commands.Contains(command))
                {
                    // make sure we're not rate limited
                    var commandKey = command + messageData.Server;
                    var commandCount = this.commandsIssued.AddOrUpdate(commandKey, 1, (key, val) =>
                    {
                        return val + 1;
                    });

                    if (commandCount > 10)
                    {
                        responses = new string[] { "rate limited try later" };
                    }
                    else
                    {
                        responses = await this.BotApi.IssueRequestAsync(messageData, query);
                    }
                }
                else
                {
                    bool sendRequest = false;
                    if (settings.AutoTitlesEnabled && CommandsConfig.Instance.AutoTitleMatches.Any(t => messageData.Content.Contains(t)))
                    {
                        Match match = httpRegex.Match(messageData.Content);
                        if (match != null)
                        {
                            query = "title " + match.Value;
                            sendRequest = true;
                        }
                    }
                    else if (settings.FunResponsesEnabled && queryParts.Length > 1 && queryParts[1] == "face")
                    {
                        query = "face " + queryParts[0];
                        sendRequest = true;
                    }

                    if (sendRequest)
                    {
                        responses = await this.BotApi.IssueRequestAsync(messageData, query);
                    }
                }
            }

            if (PhrasesConfig.Instance.Phrases.ContainsKey(messageData.Content))
            {
                responses = new string[] { PhrasesConfig.Instance.Responses[PhrasesConfig.Instance.Phrases[messageData.Content]][0] };
            }

            return responses;
        }

        private Task Client_Log(LogMessage arg)
        {
            Console.WriteLine(arg.ToString());

            return Task.CompletedTask;
        }
    }
}
