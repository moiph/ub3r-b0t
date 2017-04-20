namespace UB3RB0T
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Discord;
    using Discord.WebSocket;
    using UB3RIRC;
    using System.Collections.Concurrent;
    using System.Text.RegularExpressions;
    using Microsoft.ApplicationInsights;
    using Microsoft.ApplicationInsights.Extensibility;
    using Microsoft.ApplicationInsights.DataContracts;
    using Flurl.Http;
    using Microsoft.AspNetCore.Hosting;

    public partial class Bot : IDisposable
    {
        private int shard = 0;
        private BotType botType;
        private int instanceCount;

        private IWebHost listenerHost;

        public  DiscordSocketClient client;
        private Dictionary<string, IrcClient> ircClients;
        private static long startTime;

        private ConcurrentDictionary<string, int> commandsIssued = new ConcurrentDictionary<string, int>();
        private ConcurrentDictionary<string, RepeatData> repeatData = new ConcurrentDictionary<string, RepeatData>();
        private ConcurrentDictionary<string, SeenUserData> seenUsers = new ConcurrentDictionary<string, SeenUserData>();
        private ConcurrentDictionary<string, string> urls = new ConcurrentDictionary<string, string>();

        private static Regex UrlRegex = new Regex("(https?://[^ ]+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static Regex channelRegex = new Regex("#([a-zA-Z0-9\\-]+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static Regex httpRegex = new Regex("https?://([^\\s]+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static Regex timerRegex = new Regex(".*?remind (?<target>.+?) in (?<years>[0-9]+ year)?s? ?(?<weeks>[0-9]+ week)?s? ?(?<days>[0-9]+ day)?s? ?(?<hours>[0-9]+ hour)?s? ?(?<minutes>[0-9]+ minute)?s? ?(?<seconds>[0-9]+ seconds)?.*?(?<prep>[^ ]+) (?<reason>.+)", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Singleline);
        private static Regex timer2Regex = new Regex(".*?remind (?<target>.+?) (?<prep>[^ ]+) (?<reason>.+?) in (?<years>[0-9]+ year)?s? ?(?<weeks>[0-9]+ week)?s? ?(?<days>[0-9]+ day)?s? ?(?<hours>[0-9]+ hour)?s? ?(?<minutes>[0-9]+ minute)?s? ?(?<seconds>[0-9]+ seconds)?", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Singleline);
        private static Regex timerOnRegex  = new Regex(".*?remind (?<target>.+?) (?<prep>[^ ]+) (?<reason>.+?) at (?<time>[0-9]+:[0-9]{2} ?(am|pm)? ?(\\+[0-9]+|\\-[0-9]+)?)( on (?<date>[0-9]+(/|-)[0-9]+(/|-)[0-9]+))?", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Singleline);
        private static Regex timerOn2Regex = new Regex(".*?remind (?<target>.+?) at (?<time>[0-9]+:[0-9]{2} ?(am|pm)? ?(\\+[0-9]+|\\-[0-9]+)?)( on (?<date>[0-9]+(/|-)[0-9]+(/|-)[0-9]+))? ?(?<prep>[^ ]+) (?<reason>.+)", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Singleline);

        private Timer notificationsTimer;
        private Timer remindersTimer;
        private Timer packagesTimer;
        private Timer heartbeatTimer;
        private Timer seenTimer;

        private int missedHeartbeats = 0;

        private Logger consoleLogger = Logger.GetConsoleLogger();

        // TODO: Genalize this API support -- currently specific to private API
        private BotApi BotApi;

        public Bot(BotType botType, int shard, int instanceCount)
        {
            this.botType = botType;
            this.shard = shard;
            this.instanceCount = instanceCount;
        }

        public BotConfig Config => BotConfig.Instance;
        public TelemetryClient AppInsights { get; private set; }

        private int exitCode = 0;
        private bool isShuttingDown;

        /// Initialize and connect to the desired clients, hook up event handlers.
        /// </summary>
        /// <summary>
        public async Task<int> RunAsync()
        {
            Console.Title = $"{this.Config.Name} - {this.botType} {this.shard} #{this.instanceCount}";

            if (!string.IsNullOrEmpty(Config.InstrumentationKey))
            {
                this.AppInsights = new TelemetryClient(new TelemetryConfiguration
                {
                    InstrumentationKey = Config.InstrumentationKey,
                });

                if (this.Config.IsDevMode)
                {
                    TelemetryConfiguration.Active.TelemetryChannel.DeveloperMode = true;
                }

                this.AppInsights.Context.Properties.Add("Shard", this.shard.ToString());
                this.AppInsights.Context.Properties.Add("BotType", this.botType.ToString());
            }

            // If a custom API endpoint is supported...support it
            if (this.Config.ApiEndpoint != null)
            {
                this.BotApi = new BotApi(this.Config.ApiEndpoint, this.Config.ApiKey, this.botType);
            }

            if (botType == BotType.Discord)
            {
                if (string.IsNullOrEmpty(this.Config.Discord.Token))
                {
                    throw new InvalidConfigException("Discord auth token is missing.");
                }

                await this.CreateDiscordBotAsync();
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

                await this.CreateIrcBotsAsync();
            }

            Bot.startTime = Utilities.Utime;

            this.StartTimers();
            this.StartWebListener();
            this.StartConsoleListener();
    
            while (this.exitCode == 0)
            {
                await Task.Delay(10000);
            }

            await this.ShutdownAsync(this.exitCode == (int)Program.ExitCode.UnexpectedError);
            consoleLogger.Log(LogType.Info, "Exited.");
            return this.exitCode;
        }

        private void StartConsoleListener()
        {
            Task.Run(() =>
            {
                string read = "";
                while (read != "exit")
                {
                    read = Console.ReadLine();

                    try
                    {
                        // TODO:
                        // proper console command support
                        if (read.StartsWith("JOIN"))
                        {
                            var args = read.Split(new[] { ' ' });
                            if (args.Length != 3)
                            {
                                Console.WriteLine("invalid");
                            }
                            else if (this.ircClients.ContainsKey(args[1]))
                            {
                                // TODO: update irc library to handle this nonsense
                                this.serverData[args[1]].Channels[args[2].ToLowerInvariant()] = new ChannelData();
                                this.ircClients[args[1]].Command("JOIN", args[2], string.Empty);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine(ex);
                    }
                }

                this.exitCode = (int)Program.ExitCode.ExpectedShutdown;
            });
        }

        private void StartWebListener()
        {
            Task.Run(() =>
            {
                int port = 9100;
                if (botType == BotType.Discord)
                {
                    port += 10 + shard;
                }

                this.listenerHost = new WebHostBuilder()
                    .UseKestrel()
                    .UseUrls($"http://localhost:{port}", $"http://{this.Config.WebListenerHostName}:{port}")
                    .UseStartup<Program>()
                    .Build();
                this.listenerHost.Start();
            });
        }

        private void StartTimers()
        {
            // TODO: I see a pattern here.  Clean this up.
            notificationsTimer = new Timer(CheckNotificationsAsync, null, 10000, 10000);
            remindersTimer = new Timer(CheckRemindersAsync, null, 10000, 10000);
            heartbeatTimer = new Timer(HeartbeatTimerAsync, null, 60000, 60000);
            seenTimer = new Timer(SeenTimerAsync, null, 60000, 60000);
            packagesTimer = new Timer(CheckPackagesAsync, null, 1800000, 1800000);
        }

        private async Task HeartbeatAsync()
        {
            var heartbeatData = new HeartbeatData
            {
                BotType = this.botType.ToString(),
                Shard = this.shard,
                StartTime = Bot.startTime,
            };

            if (this.botType == BotType.Discord && this.client != null)
            {
                var metric = new MetricTelemetry
                {
                    Name = "Guilds",
                    Sum = this.client.Guilds.Count(),
                };

                this.AppInsights?.TrackMetric(metric);

                if (this.Config.HeartbeatEndpoint != null)
                {
                    heartbeatData.ServerCount = this.client.Guilds.Count();
                    heartbeatData.VoiceChannelCount = this.client.Guilds.Select(g => g.CurrentUser?.VoiceChannel).Where(v => v != null).Count();
                    heartbeatData.UserCount = this.client.Guilds.Sum(x => x.Users.Count);
                    heartbeatData.ChannelCount = this.client.Guilds.Sum(x => x.TextChannels.Count);
                }
            }
            else if (this.botType == BotType.Irc)
            {
                heartbeatData.ServerCount = this.ircClients.Count;
                heartbeatData.UserCount = this.GetIrcUserCount();
                heartbeatData.ChannelCount = this.serverData.Values.Sum(i => i.Channels.Count);

                // TODO: special case twitch until it becomes a first class type
                if (this.ircClients.ContainsKey("irc.chat.twitch.tv"))
                {
                    heartbeatData.BotType = "Twitch";
                }
            }

            if (this.Config.HeartbeatEndpoint != null && !this.Config.IsDevMode)
            {
                var result = await this.Config.HeartbeatEndpoint.ToString().PostJsonAsync(heartbeatData);
            }
        }

        private async Task UpdateSettingsAsync()
        {
            try
            {
                consoleLogger.Log(LogType.Debug, "Fetching server settings...");
                var sinceToken = SettingsConfig.Instance.SinceToken;
                await SettingsConfig.Instance.OverrideAsync(this.Config.SettingsEndpoint.AppendQueryParam("since", sinceToken.ToString()));
                consoleLogger.Log(LogType.Debug, "Server settings updated.");
            }
            catch (Exception ex)
            {
                // TODO: Update to using one of the logging classes (Discord/IRC)
                consoleLogger.Log(LogType.Warn, $"Failed to update server settings: {ex}");
            }
        }

        private bool processingtimers = false;
        private async void CheckRemindersAsync(object state)
        {
            if (processingtimers) { return; }
            processingtimers = true;
            if (CommandsConfig.Instance.RemindersEndpoint != null)
            {
                var reminders = await Utilities.GetApiResponseAsync<ReminderData[]>(CommandsConfig.Instance.RemindersEndpoint);
                if (reminders != null)
                {
                    var remindersToDelete = new List<string>();
                    foreach (var timer in reminders.Where(t => t.BotType == this.botType))
                    {
                        string requestedBy = string.IsNullOrEmpty(timer.Requestor) ? string.Empty : "[Requested by " + timer.Requestor + "]";


                        if (this.botType == BotType.Irc)
                        {
                            if (this.ircClients.ContainsKey(timer.Server))
                            {
                                string msg = string.Format("{0}: {1} ({2} ago) {3}", timer.Nick, timer.Reason, timer.Duration, requestedBy);
                                this.ircClients[timer.Server]?.Command("PRIVMSG", timer.Channel, msg);
                                remindersToDelete.Add(timer.Id);
                            }
                        }
                        else if (this.client != null)
                        {
                            IMessageChannel channel = this.client.GetChannel(Convert.ToUInt64(timer.Channel)) as ISocketMessageChannel;
                            IGuild guild = timer.Server != "private" ? this.client.GetGuild(Convert.ToUInt64(timer.Server)) : null;
                            // it may have been a PM so try to get the user
                            var user = this.client.GetUser(Convert.ToUInt64(timer.UserId));
                            if (channel == null && user != null && timer.Server == "private")
                            {
                                channel = await user.CreateDMChannelAsync() as IDMChannel;
                            }

                            if (channel != null || guild != null)
                            {
                                string nick = timer.Nick;

                                try
                                {
                                    string msg = string.Format("{0}: {1} ({2} ago) {3}", nick, timer.Reason, timer.Duration, requestedBy);

                                    if (channel is IGuildChannel guildChan && (guildChan.Guild as SocketGuild).CurrentUser.GetPermissions(guildChan).SendMessages)
                                    {
                                        // try to find an exact match for the user, failing that perform a nick search
                                        var guildUser = await guildChan.GetUserAsync(Convert.ToUInt64(timer.UserId));
                                        if (string.IsNullOrEmpty(requestedBy) && guildUser != null)
                                        {
                                            nick = guildUser.Mention;
                                        }
                                        else
                                        {
                                            nick = (await guildChan.Guild.GetUsersAsync().ConfigureAwait(false)).Find(nick).FirstOrDefault()?.Mention ?? nick;
                                        }

                                        msg = string.Format("{0}: {1} ({2} ago) {3}", nick, timer.Reason, timer.Duration, requestedBy);
                                        await channel.SendMessageAsync(msg);
                                    }
                                    else if (guild != null)
                                    {
                                        var guildUser = await guild.GetUserAsync(Convert.ToUInt64(timer.UserId));
                                        if (guildUser != null)
                                        {
                                            msg = "(Seems the original channel this reminder was created on was deleted): " + msg;
                                            await (await guildUser.CreateDMChannelAsync()).SendMessageAsync(msg);
                                        }
                                    }
                                    else if (channel != null)
                                    {
                                        await channel.SendMessageAsync(msg);
                                    }

                                    remindersToDelete.Add(timer.Id);
                                }
                                catch (Exception ex)
                                {
                                    // TODO: logging
                                    Console.WriteLine(ex);
                                }
                            }
                        }
                    }

                    if (remindersToDelete.Count > 0)
                    {
                        await Utilities.GetApiResponseAsync<object>(new Uri(CommandsConfig.Instance.RemindersEndpoint.ToString() + "?ids=" + string.Join(",", remindersToDelete)));
                    }
                }
            }

            processingtimers = false;
        }

        private async void HeartbeatTimerAsync(object state)
        {
            consoleLogger.Log(LogType.Debug, "Heartbeat");
            this.commandsIssued.Clear();

            PhrasesConfig.Instance.Reset();
            CommandsConfig.Instance.Reset();
            BotConfig.Instance.Reset();
            consoleLogger.Log(LogType.Info, "Config reloaded.");

            try
            {
                await this.HeartbeatAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
            }

            // if we haven't seen any incoming discord messages since the last heartbeat, we've got a problem
            if (this.botType == BotType.Discord)
            {
                if (messageCount == 0)
                {
                    this.missedHeartbeats++;

                    if (missedHeartbeats >= 3)
                    {
                        this.missedHeartbeats = 0;
                        this.exitCode = 1;
                        if (this.Config.AlertEndpoint != null)
                        {
                            string messageContent = $"\U0001F501 Shard {this.shard} triggered automatic restart due to inactivity";
                            try
                            {
                                await this.Config.AlertEndpoint.ToString().PostJsonAsync(new { content = messageContent });
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine(ex);
                            }
                        }
                    }
                }
                else
                {
                    this.missedHeartbeats = 0;
                }
            }

            // reset message count
            messageCount = 0;
        }

        private async void SeenTimerAsync(object state)
        {
            var seenCopy = new Dictionary<string, SeenUserData>(seenUsers);
            seenUsers.Clear();
            if (seenCopy.Count > 0)
            {
                try
                {
                    await this.Config.SeenEndpoint.ToString().PostJsonAsync(seenCopy.Values);
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex);
                }
            }
        }

        private bool processingnotifications = false;
        private async void CheckNotificationsAsync(object state)
        {
            if (processingnotifications) { return; }
            processingnotifications = true;
            if (CommandsConfig.Instance.NotificationsEndpoint != null)
            {
                NotificationData[] notifications = null;
                try
                {
                    notifications = await Utilities.GetApiResponseAsync<NotificationData[]>(CommandsConfig.Instance.NotificationsEndpoint);
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex);
                }
                if (notifications != null)
                {
                    var notificationsToDelete = new List<string>();
                    foreach (var notification in notifications.Where(t => t.BotType == this.botType))
                    {
                        if (this.botType == BotType.Irc)
                        {
                            // Pending support
                        }
                        else if (this.client != null && this.client.ConnectionState == ConnectionState.Connected)
                        {
                            try
                            {
                                if (this.client.GetChannel(Convert.ToUInt64(notification.Channel)) is ITextChannel channel)
                                {
                                    notificationsToDelete.Add(notification.Id);

                                    if (!string.IsNullOrEmpty(notification.Text) || notification.Embed != null)
                                    {
                                        if ((channel.Guild as SocketGuild).CurrentUser.GetPermissions(channel).SendMessages)
                                        {
                                            var settings = SettingsConfig.GetSettings(channel.GuildId);
                                            // adjust the notification text to disable discord link parsing, if configured to do so
                                            if (settings.DisableLinkParsing)
                                            {
                                                notification.Text = UrlRegex.Replace(notification.Text, new MatchEvaluator((Match urlMatch) =>
                                                {
                                                    return $"<{urlMatch.Captures[0]}>";
                                                }));
                                            }

                                            try
                                            {
                                                if (notification.Embed != null &&
                                                    ((settings.TwitterEmbed && notification.Type == NotificationType.Twitter) ||
                                                    (settings.RssEmbed && notification.Type == NotificationType.Rss)))
                                                {
                                                    await channel.SendMessageAsync(string.Empty, false, notification.Embed.CreateEmbedBuilder());
                                                }
                                                else
                                                {
                                                    await channel.SendMessageAsync(notification.Text);
                                                }
                                            }
                                            catch (Exception ex)
                                            {
                                                // somehow seeing 403s even if sendmessages is true?
                                                Console.WriteLine(ex);
                                            }
                                        }
                                    }
                                }
                                else if (this.client.GetGuild(Convert.ToUInt64(notification.Server)) is IGuild guild)
                                {
                                    notificationsToDelete.Add(notification.Id);

                                    if (!string.IsNullOrEmpty(notification.Text))
                                    {
                                        var defaultChannel = await guild.GetDefaultChannelAsync();
                                        var botGuildUser = await defaultChannel.GetUserAsync(this.client.CurrentUser.Id);
                                        if ((defaultChannel.Guild as SocketGuild).CurrentUser.GetPermissions(defaultChannel).SendMessages)
                                        {
                                            try
                                            {
                                                defaultChannel?.SendMessageAsync($"(Configured notification channel no longer exists, please fix it in the settings!) {notification.Text}");
                                            }
                                            catch (Exception ex)
                                            {
                                                // somehow seeing 403s even if sendmessages is true?
                                                Console.WriteLine(ex);
                                            }
                                        }
                                    }
                                }
                            }
                            catch (NullReferenceException)
                            {
                                // TODO: Fix the nullref, something's funky with client state when reconnecting
                            }
                        }
                    }

                    if (notificationsToDelete.Count > 0)
                    {
                        await CommandsConfig.Instance.NotificationsEndpoint.ToString().PostJsonAsync(new { ids = string.Join(",", notificationsToDelete)});
                    }
                }
            }

            processingnotifications = false;
        }

        private async void CheckPackagesAsync(object state)
        {
            if (CommandsConfig.Instance.PackagesEndpoint != null)
            {
                var packages = await Utilities.GetApiResponseAsync<PackageData[]>(CommandsConfig.Instance.PackagesEndpoint);
                if (packages != null)
                {
                    foreach (var package in packages.Where(t => t.BotType == this.botType))
                    {
                        string query = $"ups bot {package.Tracking}";
                        var messageData = new BotMessageData(this.botType)
                        {
                            UserName = package.Nick,
                            Channel = package.Channel,
                            Server = package.Server,
                            UserId = string.Empty,
                        };

                        if (this.botType == BotType.Irc)
                        {
                            var apiResponse = await this.BotApi.IssueRequestAsync(messageData, query);

                            foreach (var response in apiResponse.Responses)
                            {
                                this.ircClients[package.Server]?.Command("PRIVMSG", package.Channel, response);
                            }
                        }
                        else if (this.client != null)
                        {
                            if (this.client.GetChannel(Convert.ToUInt64(package.Channel)) is ITextChannel channel)
                            {
                                var botResponse = await this.BotApi.IssueRequestAsync(messageData, query);

                                if ((channel.Guild as SocketGuild).CurrentUser.GetPermissions(channel).SendMessages)
                                {
                                    if (botResponse.Responses.Count > 0 && !string.IsNullOrEmpty(botResponse.Responses[0]))
                                    {
                                        string senderNick = package.Nick;
                                        var user = (await channel.Guild.GetUsersAsync().ConfigureAwait(false)).Find(package.Nick).FirstOrDefault();
                                        if (user != null)
                                        {
                                            senderNick = user.Mention;
                                        }

                                        await channel.SendMessageAsync($"{senderNick} oshi- an upsdate!");

                                        foreach (var response in botResponse.Responses)
                                        {
                                            if (!string.IsNullOrEmpty(response))
                                            {

                                                await channel.SendMessageAsync(response);
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }

        /// <summary>
        ///  Handles shutdown tasks
        /// </summary>
        /// <param name="unexpected">When true, we'll bypass audio goodbyes</param>
        public async Task ShutdownAsync(bool unexpected = false)
        {
            if (!this.isShuttingDown)
            {
                this.isShuttingDown = true;
                if (this.botType == BotType.Discord)
                {
                    // explicitly leave all audio channels so that we can say goodbye
                    if (!unexpected)
                    {
                        var audioTask = this.audioManager?.LeaveAllAudioAsync();
                        var timeoutTask = Task.Delay(15000);
                        await Task.WhenAny(audioTask, timeoutTask);
                    }

                    this.client.StopAsync().Forget(); // awaiting this likes to hang
                    await Task.Delay(5000);
                }
                else if (this.botType == BotType.Irc)
                {
                    foreach (var client in this.ircClients.Values)
                    {
                        client.Disconnect("Shutting down.");
                    }
                }

                // flush any remaining seen data before shutdown
                this.SeenTimerAsync(null);
            }
        }

        // Whether or not the message author is the bot owner (will only return true in Discord scenarios).
        private bool IsAuthorOwner(BotMessageData messageData)
        {
            return !string.IsNullOrEmpty(messageData.UserId) && messageData.UserId == this.Config.Discord?.OwnerId.ToString();
        }

        private bool IsAuthorOwner(SocketUserMessage message)
        {
            return message.Author.Id == this.Config.Discord?.OwnerId;
        }

        private bool IsAuthorPatron(ulong userId)
        {
            return this.Config.Discord.Patrons.Contains(userId);
        }

        private async Task<BotResponseData> ProcessMessageAsync(BotMessageData messageData, Settings settings)
        {
            var responses = new List<string>();
            var responseData = new BotResponseData { Responses = responses };

            if (this.BotApi != null)
            {
                // if an explicit command is being used, it wins out over any implicitly parsed command
                string query = messageData.Query;
                string command = CommandsConfig.Instance.Commands.ContainsKey(messageData.Command) ? messageData.Command : string.Empty;
                string[] contentParts = messageData.Content.Split(new[] { ' ' });

                if (string.IsNullOrEmpty(command))
                {
                    if (messageData.Content.ToLowerInvariant().Contains("remind "))
                    {
                        // check for reminders
                        Match timerAtMatch = timerOnRegex.Match(messageData.Content);
                        Match timerAt2Match = timerOn2Regex.Match(messageData.Content);
                        if (timerAtMatch.Success && Utilities.TryParseAbsoluteReminder(timerAtMatch, messageData, out query))
                        {
                            command = "timer";
                        }
                        else if (timerAt2Match.Success && Utilities.TryParseAbsoluteReminder(timerAt2Match, messageData, out query))
                        {
                            command = "timer";
                        }
                        else // try relative timers if absolute had no match
                        {
                            Match timerMatch = timerRegex.Match(messageData.Content);
                            Match timer2Match = timer2Regex.Match(messageData.Content);

                            if (timerMatch.Success || timer2Match.Success)
                            {
                                Match matchToUse = timerMatch.Success && !timerMatch.Groups["prep"].Value.All(char.IsDigit) ? timerMatch : timer2Match;
                                if (Utilities.TryParseReminder(matchToUse, messageData, out query))
                                {
                                    command = "timer";
                                }
                            }

                        }
                    }

                    if (string.IsNullOrEmpty(command))
                    {
                        if (settings.AutoTitlesEnabled && CommandsConfig.Instance.AutoTitleMatches.Any(t => messageData.Content.Contains(t)))
                        {
                            Match match = httpRegex.Match(messageData.Content);
                            if (match != null)
                            {
                                command = "title";
                                query = $"{command} {match.Value}";
                            }
                        }
                        else if ((settings.FunResponsesEnabled || IsAuthorOwner(messageData)) && contentParts.Length > 1 && contentParts[1] == "face")
                        {
                            command = "face";
                            query = $"{command} {contentParts[0]}";
                        }
                    }
                }

                // handle special owner internal commands
                if (await this.TryHandleInternalCommandAsync(messageData))
                {
                    return responseData;
                }

                // Ignore if the command is disabled on this server
                if (settings.IsCommandDisabled(CommandsConfig.Instance, command) && !IsAuthorOwner(messageData))
                {
                    return responseData;
                }

                if (!string.IsNullOrEmpty(command) && CommandsConfig.Instance.Commands.ContainsKey(command))
                {
                    // make sure we're not rate limited
                    var commandKey = command + messageData.Server;
                    var commandCount = this.commandsIssued.AddOrUpdate(commandKey, 1, (key, val) =>
                    {
                        return val + 1;
                    });

                    if (commandCount > 10)
                    {
                        responses.Add("rate limited try later");
                    }
                    else
                    {
                        var props = new Dictionary<string, string> {
                            { "serverId", messageData.Server },
                        };
                        this.AppInsights?.TrackEvent(command.ToLowerInvariant(), props);

                        // extra processing on ".title" command
                        if ((command == "title" || command == "t") && messageData.Content.EndsWith(command) && urls.ContainsKey(messageData.Channel))
                        {
                            query += $" {urls[messageData.Channel]}";
                        }

                        responseData = await this.BotApi.IssueRequestAsync(messageData, query);
                    }
                }
            }

            if (responseData.Responses.Count == 0 && responseData.Embed == null)
            {
                bool mentionsBot = false;
                if (messageData.BotType == BotType.Discord)
                {
                    mentionsBot = messageData.DiscordMessageData.MentionedUsers.Count == 1 && messageData.DiscordMessageData.MentionedUsers.First().Id == client.CurrentUser.Id ||
                        messageData.Content.ToLowerInvariant().Contains(this.Config.Name.ToLowerInvariant());
                }
                else
                {
                    mentionsBot = messageData.IrcMessageData.Text.Contains(this.Config.Name);
                }

                string response = null;
                if (mentionsBot)
                {
                    var responseValue = PhrasesConfig.Instance.PartialMentionPhrases.Where(kvp => messageData.Content.ToLowerInvariant().Contains(kvp.Key.ToLowerInvariant())).FirstOrDefault().Value;
                    if (!string.IsNullOrEmpty(responseValue))
                    { 
                        response = PhrasesConfig.Instance.Responses[responseValue].Random();
                    }
                }
                
                if (response == null && (settings.FunResponsesEnabled || IsAuthorOwner(messageData)) && PhrasesConfig.Instance.ExactPhrases.ContainsKey(messageData.Content) && new Random().Next(1, 100) <= settings.FunResponseChance)
                {
                    response = PhrasesConfig.Instance.Responses[PhrasesConfig.Instance.ExactPhrases[messageData.Content]].Random();
                }

                if (response != null)
                {
                    response = response.Replace("%from%", messageData.UserName);
                    string[] resps = response.Split(new char[] { '|' });
                    responseData.Responses.AddRange(resps);
                }
            }

            return responseData;
        }

        // Won't work on IRC; we rely on unique ID to match the owner via Discord.
        private async Task<bool> TryHandleInternalCommandAsync(BotMessageData messageData)
        {
            if (messageData.BotType == BotType.Discord && messageData.UserId == this.Config.Discord.OwnerId.ToString())
            {
                switch (messageData.Command)
                {
                    case "restart":
                        await this.ShutdownAsync();
                        this.exitCode = 1;
                        break;
                }
            }
            return false;
        }

        public void Dispose() => Dispose(true);

        public void Dispose(bool disposing)
        {
            Console.WriteLine("dispose start");
            settingsUpdateTimer?.Dispose();
            packagesTimer?.Dispose();
            remindersTimer?.Dispose();
            heartbeatTimer?.Dispose();
            statsTimer?.Dispose();
            notificationsTimer?.Dispose();
            listenerHost?.Dispose();
            Console.WriteLine("disposing of client");
            this.client?.Dispose();
            Console.WriteLine("disposing of audio manger");
            audioManager?.Dispose();
            Console.WriteLine("dispose end");
        }
    }
}
