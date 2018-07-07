
namespace UB3RB0T
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Linq;
    using System.Security.Cryptography.X509Certificates;
    using System.Text;
    using System.Text.RegularExpressions;
    using System.Threading;
    using System.Threading.Tasks;
    using Microsoft.ApplicationInsights;
    using Microsoft.ApplicationInsights.DataContracts;
    using Microsoft.ApplicationInsights.Extensibility;
    using Microsoft.AspNetCore.Hosting;
    using Microsoft.Azure.ServiceBus;
    using Newtonsoft.Json;
    using StatsdClient;
    using UB3RIRC;

    /// <summary>
    /// It's...UB3R-B0T
    /// </summary>
    public abstract class Bot : IDisposable
    {
        private int instanceCount;
        private int exitCode = 0;
        private int messageCount = 0;
        private int missedHeartbeats = 0;
        private bool isShuttingDown;
        private static long startTime;

        private IWebHost listenerHost;
        private QueueClient queueClient;
        private readonly string queueName;

        private Timer heartbeatTimer;
        private Timer seenTimer;
        private Timer throttleTimer;

        private readonly ConcurrentDictionary<string, string> urls = new ConcurrentDictionary<string, string>();
        private readonly ConcurrentDictionary<string, int> commandsIssued = new ConcurrentDictionary<string, int>();
        private readonly ConcurrentDictionary<string, RepeatData> repeatData = new ConcurrentDictionary<string, RepeatData>();
        private readonly ConcurrentDictionary<string, SeenUserData> seenUsers = new ConcurrentDictionary<string, SeenUserData>();

        private readonly SemaphoreSlim settingsLock = new SemaphoreSlim(1, 1);

        protected int Shard { get; private set; } = 0;
        protected Logger Logger { get; }
        protected TelemetryClient AppInsights { get; private set; }
        protected BotApi BotApi { get; }

        protected virtual string UserId { get; }

        public int TotalShards { get; private set; } = 1;
        public BotConfig Config => BotConfig.Instance;
        public abstract BotType BotType { get; }

        protected Bot(int shard, int totalShards)
        {
            this.Logger = new Logger(LogType.Warn, new List<ILog> { new ConsoleLog() });
            this.Shard = shard;
            this.TotalShards = totalShards;
            if (!string.IsNullOrEmpty(this.Config.QueueNamePrefix))
            {
                var suffix = this.BotType == BotType.Irc ? "irc" : $"{shard}";
                this.queueName = $"{this.Config.QueueNamePrefix}{suffix}";
            }

            this.SetupAppInsights();

            if (this.Config.AlertEndpoint != null)
            {
                this.Logger.AddLogger(new WebhookLog(this.BotType, this.Shard, this.Config.AlertEndpoint));
            }

            // If a custom API endpoint is supported...support it
            if (this.Config.ApiEndpoint != null)
            {
                this.BotApi = new BotApi(this.Config.ApiEndpoint, this.Config.ApiKey, this.BotType);
            }
        }

        /// <summary>
        /// Factory method to create a bot of the desired type.
        /// </summary>
        /// <param name="botType">BotType to create.</param>
        /// <param name="shard">Shard of this bot instance.</param>
        /// <param name="instanceCount">Instance of this bot (increments on internal automatic restarts)</param>
        /// <returns></returns>
        public static Bot Create(BotType botType, int shard, int totalShards, int instanceCount)
        {
            Bot bot;
            switch (botType)
            {
                case BotType.Irc:
                    bot = new IrcBot(0);
                    break;

                case BotType.Discord:
                    bot = new DiscordBot(shard, totalShards);
                    break;

                default:
                    throw new ArgumentException("Invalid bot type.");
            }

            bot.instanceCount = instanceCount;

            return bot;
        }

        public async Task<int> StartAsync()
        {
            Console.Title = $"{this.Config.Name} - {this.BotType} {this.Shard} #{this.instanceCount}";

            // If user customizeable server settings are supported...support them
            // Currently discord only.
            if (this.Config.SettingsEndpoint != null && this.BotType == BotType.Discord)
            {
                await this.UpdateSettingsAsync();
            }

            await this.StartAsyncInternal();

            Bot.startTime = Utilities.Utime;

            this.StartTimers();
            this.StartConsoleListener();

            if (!string.IsNullOrEmpty(this.Config.WebListenerHostName))
            {
                this.StartWebListener();
            }

            if (!string.IsNullOrEmpty(this.Config.ServiceBusConnectionString))
            {
                this.StartServiceBusListener();
            }

            while (this.exitCode == (int)ExitCode.Success)
            {
                await Task.Delay(10000);
            }

            await this.StopAsync(this.exitCode == (int)ExitCode.UnexpectedError);
            this.Logger.Log(LogType.Info, "Exited.");
            return this.exitCode;
        }

        /// <summary>
        /// Creates a QueueClient for azure service bus to listen for notifications.
        /// </summary>
        private void StartServiceBusListener()
        {
            this.queueClient = new QueueClient(this.Config.ServiceBusConnectionString, this.queueName, ReceiveMode.PeekLock);

            // Register an OnMessage callback
            this.queueClient.RegisterMessageHandler(
                async (message, token) =>
                {
                    var body = Encoding.UTF8.GetString(message.Body);

                    NotificationData notificationData = null;

                    try
                    {
                        notificationData = JsonConvert.DeserializeObject<NotificationData>(body);
                    }
                    catch (JsonSerializationException ex)
                    {
                        this.Logger.Log(LogType.Warn, $"Failed to deserialize notification: {ex}");
                    }

                    if (notificationData != null)
                    {

                        // If it's a system notification, handle it directly, otherwise pass it along
                        if (notificationData.Type == NotificationType.System)
                        {
                            if (notificationData.SubType == SubType.SettingsUpdate)
                            {
                                await this.UpdateSettingsAsync();
                            }
                            else
                            {
                                this.Logger.Log(LogType.Error, $"Error processing notification, unrecognized subtype: {notificationData.SubType}");
                            }
                        }
                        else
                        {
                            try
                            {
                                await this.SendNotification(notificationData);
                            }
                            catch (Exception ex)
                            {
                                this.Logger.Log(LogType.Error, "Error processing notification: {0}", ex);
                            }
                        }
                    }

                    // For now, no retries. Regardless of notification send success, complete it.
                    await queueClient.CompleteAsync(message.SystemProperties.LockToken);
                });
        }

        public async Task StopAsync(bool unexpected = false)
        {
            if (!this.isShuttingDown)
            {
                this.isShuttingDown = true;
                await this.StopAsyncInternal(unexpected);

                // flush any remaining seen data before shutdown
                this.SeenTimerAsync(null);
            }
        }

        public void Dispose() => Dispose(true);

        public virtual void Dispose(bool disposing)
        {
            this.Logger.Log(LogType.Debug, "dispose start");
            this.queueClient?.CloseAsync();
            this.heartbeatTimer?.Dispose();
            this.throttleTimer?.Dispose();
            this.seenTimer?.Dispose();
            this.listenerHost?.Dispose();
            this.Logger.Log(LogType.Debug, "dispose end");
        }

        protected void UpdateSeen(string key, SeenUserData seenUserData)
        {
            if (this.Config.SeenEndpoint != null && !string.IsNullOrEmpty(seenUserData.Name))
            {
                seenUserData.Timestamp = Utilities.Utime;
                this.seenUsers[key] = seenUserData;
            }
        }

        protected abstract Task StartAsyncInternal();

        protected abstract Task StopAsyncInternal(bool unexpected);

        protected abstract HeartbeatData GetHeartbeatData();

        /// <summary>
        /// Attempts to process and send a notification.
        /// </summary>
        /// <param name="notification">The notification data.</param>
        /// <returns>True, if notification was successfully processed.</returns>
        protected abstract Task<bool> SendNotification(NotificationData notification);

        protected async Task PreProcessMessage(BotMessageData messageData, Settings settings)
        {
            this.messageCount++;

            // Update the seen data
            this.UpdateSeen(messageData.Channel + messageData.UserName, new SeenUserData
            {
                Name = messageData.UserId ?? messageData.UserName,
                Channel = messageData.Channel,
                Server = messageData.Server,
                //Text = messageData.Content, removed due to discord tos
            });

            var httpMatch = Consts.HttpRegex.Match(messageData.Content);
            if (httpMatch.Success)
            {
                this.urls[messageData.Channel] = httpMatch.Value;
            }

            if (settings.FunResponsesEnabled && !string.IsNullOrEmpty(messageData.Content))
            {
                var repeat = repeatData.GetOrAdd(messageData.Channel + messageData.Server, new RepeatData());
                if (string.Equals(repeat.Text, messageData.Content, StringComparison.OrdinalIgnoreCase))

                {
                    if (!repeat.Nicks.Contains(messageData.UserId ?? messageData.UserName))
                    {
                        repeat.Nicks.Add(messageData.UserId ?? messageData.UserName);
                    }

                    if (repeat.Nicks.Count == 3)
                    {
                        await this.RespondAsync(messageData, messageData.Content);
                        repeat.Reset(string.Empty, string.Empty);
                    }
                }
                else
                {
                    repeat.Reset(messageData.UserId ?? messageData.UserName, messageData.Content);
                }
            }

            string command = messageData.Command;
            if (!CommandsConfig.Instance.Commands.ContainsKey(command))
            {
                command = string.Empty;
            }

            if (string.IsNullOrEmpty(command))
            {
                // match fun
                bool mentionsBot = messageData.MentionsBot(this.Config.Name, Convert.ToUInt64(this.UserId));
                if (CommandsConfig.Instance.TryParseForCommand(messageData.Content, mentionsBot, out string parsedCommand, out string query))
                {
                    messageData.Content = $"{settings.Prefix}{query}";
                }
            }
        }

        protected abstract Task RespondAsync(BotMessageData messageData, string text);

        protected async Task<BotResponseData> ProcessMessageAsync(BotMessageData messageData, Settings settings)
        {
            var responses = new List<string>();
            var responseData = new BotResponseData { Responses = responses };

            if (this.BotApi != null)
            {
                // if an explicit command is being used, it wins out over any implicitly parsed command
                string query = messageData.Query;
                string command = messageData.Command;

                string[] contentParts = messageData.Content.Split(new[] { ' ' });

                if (string.IsNullOrEmpty(command))
                {
                    if (messageData.Content.IContains("remind "))
                    {
                        // check for reminders
                        Match timerAtMatch = Consts.TimerOnRegex.Match(messageData.Content);
                        Match timerAt2Match = Consts.TimerOn2Regex.Match(messageData.Content);
                        if (timerAtMatch.Success && Utilities.TryParseAbsoluteReminder(timerAtMatch, messageData, out query) ||
                            timerAt2Match.Success && Utilities.TryParseAbsoluteReminder(timerAt2Match, messageData, out query))
                        {
                            command = "timer";
                        }
                        else // try relative timers if absolute had no match
                        {
                            Match timerMatch = Consts.TimerRegex.Match(messageData.Content);
                            Match timer2Match = Consts.Timer2Regex.Match(messageData.Content);

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
                        if (settings.AutoTitlesEnabled && (CommandsConfig.Instance.AutoTitleMatches.Any(t => messageData.Content.Contains(t)) || Consts.RedditRegex.IsMatch(messageData.Content)))
                        {
                            Match match = Consts.HttpRegex.Match(messageData.Content);
                            string matchValue = !string.IsNullOrEmpty(match.Value) ? match.Value : Consts.RedditRegex.Match(messageData.Content).Value;
                            if (!string.IsNullOrEmpty(matchValue))
                            {
                                command = "title";
                                query = $"{command} {matchValue}";
                            }
                        }
                        else if ((settings.FunResponsesEnabled || IsAuthorOwner(messageData)) && contentParts.Length > 1 && contentParts[1] == "face")
                        {
                            command = "face";
                            query = $"{command} {contentParts[0]}";
                        }
                    }
                }

                // Ignore if the command is disabled on this server
                if (settings.IsCommandDisabled(CommandsConfig.Instance, command) && !IsAuthorOwner(messageData))
                {
                    return responseData;
                }

                if (CommandsConfig.Instance.Commands.ContainsKey(command))
                {
                    if (!messageData.RateLimitChecked)
                    {
                        // make sure we're not rate limited
                        var commandKey = command + messageData.Server;
                        var commandCount = this.commandsIssued.AddOrUpdate(commandKey, 1, (key, val) =>
                        {
                            return val + 1;
                        });

                        if (commandCount > 6)
                        {
                            return responseData;
                        }
                        else if (commandCount > 5)
                        {
                            responses.Add("rate limited try later");
                            return responseData;
                        }
                    }

                    var props = new Dictionary<string, string> {
                        { "command",  command.ToLowerInvariant() },
                        { "server", messageData.Server },
                        { "channel", messageData.Channel }
                    };
                    this.TrackEvent("commandProcessed", props);

                    // extra processing on ".title" command
                    if ((command == "title" || command == "t") && messageData.Content.EndsWith(command) && urls.ContainsKey(messageData.Channel))
                    {
                        query += $" {this.urls[messageData.Channel]}";
                    }

                    using (DogStatsd.StartTimer("commandDuration", tags: new[] { $"shard:{this.Shard}", $"command:{command.ToLowerInvariant()}", $"{this.BotType}" }))
                    {
                        messageData.Content = $"{settings.Prefix}{query}";
                        responseData = await this.BotApi.IssueRequestAsync(messageData);
                    }
                }
            }

            if (responseData.Responses.Count == 0 && responseData.Embed == null)
            {
                string response = null;
                if (messageData.MentionsBot(this.Config.Name, Convert.ToUInt64(this.UserId)))
                {
                    var responseValue = PhrasesConfig.Instance.PartialMentionPhrases.FirstOrDefault(kvp => messageData.Content.IContains(kvp.Key)).Value;
                    if (!string.IsNullOrEmpty(responseValue))
                    {
                        response = PhrasesConfig.Instance.Responses[responseValue].Random();
                    }
                }

                if (response == null && PhrasesConfig.Instance.ExactPhrases.ContainsKey(messageData.Content) && (settings.FunResponsesEnabled && new Random().Next(1, 100) <= settings.FunResponseChance || IsAuthorOwner(messageData)))
                {
                    response = PhrasesConfig.Instance.Responses[PhrasesConfig.Instance.ExactPhrases[messageData.Content]].Random();
                }

                if (response == null)
                {
                    response = settings.CustomCommands.FirstOrDefault(c => c.IsExactMatch && c.Command == messageData.Content || !c.IsExactMatch && messageData.Content.IContains(c.Command))?.Response;
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

        protected void TrackEvent(string eventName, Dictionary<string, string> properties = null)
        {
            // set common properties/tags
            if (properties == null)
            {
                properties = new Dictionary<string, string>();
            }

            properties["botType"] = $"{this.BotType}";
            properties["shard"] = $"{this.Shard}";

            // remove server/channel properties from discord unless sponsored by a patron
            if (this.BotType == BotType.Discord && properties.ContainsKey("server"))
            {
                var settings = SettingsConfig.GetSettings(properties["server"]);
                if (settings.PatronSponsor == 0)
                {
                    properties.Remove("server");
                    properties.Remove("channel");
                }
            }

            var tags = new List<string>();
            foreach (var kvp in properties)
            {
                tags.Add($"{kvp.Key}:{kvp.Value}");
            }

            DogStatsd.Increment(eventName, tags: tags.ToArray());
        }

        private void StartWebListener()
        {
            Task.Run(() =>
            {
                int port = 9100;
                if (this.BotType == BotType.Discord)
                {
                    port += 10 + this.Shard;
                }

                X509Certificate2 cert = null;
                if (!string.IsNullOrEmpty(this.Config.CertThumbprint))
                {
                    var store = new X509Store(StoreName.My, StoreLocation.LocalMachine);
                    store.Open(OpenFlags.ReadOnly);
                    var certificates = store.Certificates.Find(X509FindType.FindByThumbprint, this.Config.CertThumbprint, validOnly: false);
                    if (certificates?.Count > 0)
                    {
                        cert = certificates[0];
                    }
                }

                this.listenerHost = new WebHostBuilder()
                    .UseKestrel(options =>
                    {
                        if (cert != null)
                        {
                            options.UseHttps(cert);
                        }
                    })
                    .UseUrls($"http://localhost:{port}", $"http://{this.Config.WebListenerHostName}:{port}")
                    .UseStartup<Program>()
                    .Build();
                this.listenerHost.Start();
            });
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
                            /*else if (this.ircClients.ContainsKey(args[1]))
                            {
                                // TODO: update irc library to handle this nonsense
                                this.serverData[args[1]].Channels[args[2].ToLowerInvariant()] = new ChannelData();
                                this.ircClients[args[1]].Command("JOIN", args[2], string.Empty);
                            }*/
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine(ex);
                    }
                }

                this.exitCode = (int)ExitCode.ExpectedShutdown;
            });
        }

        /// <summary>
        /// Updates settings from the service. (Currently Discord only)
        /// </summary>
        /// <returns></returns>
        private async Task UpdateSettingsAsync()
        {
            await settingsLock.WaitAsync();

            try
            {
                this.Logger.Log(LogType.Debug, "Fetching server settings...");
                var sinceToken = SettingsConfig.Instance.SinceToken;
                var configEndpoint = this.Config.SettingsEndpoint.AppendQueryParam("since", sinceToken.ToString()).AppendQueryParam("shard", this.Shard.ToString()).AppendQueryParam("shardcount", this.TotalShards.ToString());
                await SettingsConfig.Instance.OverrideAsync(configEndpoint);
                this.Logger.Log(LogType.Debug, "Server settings updated.");
            }
            catch (Exception ex)
            {
                this.Logger.Log(LogType.Warn, $"Failed to update server settings: {ex}");
            }
            finally
            {
                settingsLock.Release();
            }
        }

        /// <summary>
        /// Starts recurring timers (seen, heartbeat)
        /// </summary>
        private void StartTimers()
        {
            if (this.Config.SeenEndpoint != null)
            {
                seenTimer = new Timer(SeenTimerAsync, null, 60000, 60000);
            }

            heartbeatTimer = new Timer(HeartbeatTimerAsync, null, 60000, 60000);
            throttleTimer = new Timer(ThrottleTimer, null, 10000, 10000);
        }

        private void ThrottleTimer(object state)
        {
            this.commandsIssued.Clear();
        }

        /// <summary>
        /// Timer callback to handle persisting seen data.
        /// </summary>
        /// <param name="state">State object (unused).</param>
        private async void SeenTimerAsync(object state)
        {
            var seenCopy = new Dictionary<string, SeenUserData>(seenUsers);
            seenUsers.Clear();
            if (seenCopy.Count > 0)
            {
                try
                {
                    await this.Config.SeenEndpoint.PostJsonAsync(seenCopy.Values);
                }
                catch (Exception ex)
                {
                    this.Logger.Log(LogType.Error, $"Error in seen callback {ex}");
                }
            }
        }

        /// <summary>
        /// Timer callback to push heartbeat data.
        /// </summary>
        /// <param name="state">State object (unused).</param>
        private async void HeartbeatTimerAsync(object state)
        {
            this.Logger.Log(LogType.Debug, "Heartbeat");

            PhrasesConfig.Instance.Reset();
            CommandsConfig.Instance.Reset();
            BotConfig.Instance.Reset();
            this.Logger.Log(LogType.Info, "Config reloaded.");

            if (this is DiscordBot discordBot)
            {
                var metric = new MetricTelemetry
                {
                    Name = "Guilds",
                    Count = discordBot.Client.Guilds.Count(),
                };

                this.AppInsights?.TrackMetric(metric);

                DogStatsd.Gauge("guildsCount", discordBot.Client.Guilds.Count(), tags: new[] { $"shard:{this.Shard}", $"{this.BotType}" });
            }

            if (this.Config.HeartbeatEndpoint != null && !this.Config.IsDevMode)
            {
                var heartbeatData = this.GetHeartbeatData();
                heartbeatData.BotType = this.BotType.ToString();
                heartbeatData.Shard = this.Shard;
                heartbeatData.StartTime = Bot.startTime;

                try
                {
                    var result = await this.Config.HeartbeatEndpoint.PostJsonAsync(heartbeatData);
                }
                catch (Exception ex)
                {
                    this.Logger.Log(LogType.Error, "Error sending heartbeat data: {0}", ex);
                }
            }

            if (this.messageCount == 0 && this is DiscordBot)
            {
                this.missedHeartbeats++;

                if (missedHeartbeats >= 3)
                {
                    this.missedHeartbeats = 0;
                    this.exitCode = (int)ExitCode.UnexpectedError;
                    if (this.Config.AlertEndpoint != null)
                    {
                        string messageContent = $"\U0001F501 {this.BotType} Shard {this.Shard} triggered automatic restart due to inactivity";
                        try
                        {
                            await this.Config.AlertEndpoint.PostJsonAsync(new { content = messageContent });
                        }
                        catch (Exception ex)
                        {
                            this.Logger.Log(LogType.Error, "Heartbeat failure alert error: {0}", ex);
                        }
                    }
                }
            }

            // reset message count
            messageCount = 0;
        }

        // Whether or not the message author is the bot owner (will only return true in Discord scenarios).
        private bool IsAuthorOwner(BotMessageData messageData)
        {
            return !string.IsNullOrEmpty(messageData.UserId) && messageData.UserId == this.Config.Discord?.OwnerId.ToString();
        }

        /// <summary>
        /// Configures ApplicationInsights for telemetry.
        /// </summary>
        private void SetupAppInsights()
        {
            if (!string.IsNullOrEmpty(Config.InstrumentationKey))
            {
                this.AppInsights = new TelemetryClient(new TelemetryConfiguration
                {
                    InstrumentationKey = Config.InstrumentationKey,
                });

                TelemetryConfiguration.Active.TelemetryChannel.DeveloperMode = this.Config.IsDevMode;
                this.AppInsights.Context.Properties.Add("Shard", this.Shard.ToString());
                this.AppInsights.Context.Properties.Add("BotType", this.BotType.ToString());
            }

            var dogstatsdConfig = new StatsdConfig
            {
                StatsdServerName = "127.0.0.1"
            };

            DogStatsd.Configure(dogstatsdConfig);
        }
    }
}
