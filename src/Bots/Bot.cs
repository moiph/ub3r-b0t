﻿
namespace UB3RB0T
{
    using Azure.Messaging.ServiceBus;
    using Microsoft.ApplicationInsights;
    using Microsoft.ApplicationInsights.DataContracts;
    using Microsoft.ApplicationInsights.Extensibility;
    using Microsoft.AspNetCore.Hosting;
    using Newtonsoft.Json;
    using Serilog;
    using StatsdClient;
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Linq;
    using System.Reflection;
    using System.Security.Cryptography.X509Certificates;
    using System.Text.RegularExpressions;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// It's...UB3R-B0T
    /// </summary>
    public abstract class Bot : IDisposable
    {
        private bool isDisposed;
        private int instanceCount;
        private int exitCode = 0;
        private int messageCount = 0;
        private int missedHeartbeats = 0;
        private bool isShuttingDown;
        private static long startTime;

        private IWebHost listenerHost;
        private readonly string queueName;

        private Timer heartbeatTimer;
        private Timer seenTimer;

        private readonly ConcurrentDictionary<string, string> urls = new ConcurrentDictionary<string, string>();
        private readonly ConcurrentDictionary<string, RepeatData> repeatData = new ConcurrentDictionary<string, RepeatData>();
        private readonly ConcurrentDictionary<string, SeenUserData> seenUsers = new ConcurrentDictionary<string, SeenUserData>();

        private readonly SemaphoreSlim settingsLock = new SemaphoreSlim(1, 1);

        protected int Shard { get; private set; } = 0;
        protected TelemetryClient AppInsights { get; private set; }
        protected DogStatsdService DogStats { get; private set; }
        protected BotApi BotApi { get; }

        protected virtual string UserId { get; }
        protected Throttler Throttler = new Throttler();
        protected Random Random = new Random();

        public int TotalShards { get; private set; } = 1;
        public BotConfig Config => BotConfig.Instance;
        public abstract BotType BotType { get; }

        protected Bot(int shard, int totalShards)
        {
            this.Shard = shard;
            this.TotalShards = totalShards;
            if (!string.IsNullOrEmpty(this.Config.QueueNamePrefix))
            {
                var suffix = "";
                switch (this.BotType)
                {
                    case BotType.Irc:
                        suffix = "irc";
                        break;
                    case BotType.Discord:
                        suffix = $"{shard}";
                        break;
                    case BotType.Guilded:
                        suffix = "guilded";
                        break;
                }

                this.queueName = $"{this.Config.QueueNamePrefix}{suffix}";
            }

            this.SetupAppInsights();

            if (this.Config.AlertEndpoint != null)
            {
                // this.Logger.AddLogger(new WebhookLog(this.BotType, this.Shard, this.Config.AlertEndpoint));
            }

            // If a custom API endpoint is supported...support it
            if (this.Config.ApiEndpoint != null)
            {
                this.BotApi = new BotApi(this.Config.ApiEndpoint);
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

                case BotType.Guilded:
                    bot = new GuildedBot(shard, totalShards);
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

            if (!string.IsNullOrEmpty(this.Config.WebListenerHostName))
            {
                this.StartWebListener();
            }

            if (!string.IsNullOrEmpty(this.Config.ServiceBusConnectionString))
            {
                _ = Task.Run(async () =>
                {
                    await this.StartServiceBusListener();
                });
            }

            while (this.exitCode == (int)ExitCode.Success)
            {
                await Task.Delay(10000);
            }

            await this.StopAsync(this.exitCode == (int)ExitCode.UnexpectedError);
            Log.Information("Exited.");
            return this.exitCode;
        }

        /// <summary>
        /// Creates a ServiceBusClient for azure service bus to listen for notifications.
        /// </summary>
        private async Task StartServiceBusListener()
        {
            await using var client = new ServiceBusClient(this.Config.ServiceBusConnectionString);
            var options = new ServiceBusProcessorOptions
            {
                ReceiveMode = ServiceBusReceiveMode.PeekLock,
            };

            await using ServiceBusProcessor processor = client.CreateProcessor(this.queueName, options);
            processor.ProcessMessageAsync += ServiceBusMessageHandler;
            processor.ProcessErrorAsync += (ProcessErrorEventArgs args) =>
            {
                Log.Error(args.Exception, $"ServiceBus message handler failure. Source: {args.ErrorSource}; Namespace: {args.FullyQualifiedNamespace}; EntityPath: {args.EntityPath}");
                return Task.CompletedTask;
            };
            
            await processor.StartProcessingAsync();
            await Task.Delay(-1);
        }

        /// <summary>
        /// Handles service bus messages.
        /// </summary>
        private async Task ServiceBusMessageHandler(ProcessMessageEventArgs args)
        {
            string body = args.Message.Body.ToString();
            NotificationData notificationData = null;

            try
            {
                notificationData = JsonConvert.DeserializeObject<NotificationData>(body);
            }
            catch (JsonSerializationException ex)
            {
                Log.Warning(ex, "Failed to deserialize notification");
            }

            if (notificationData != null)
            {
                // If it's a system notification, handle it directly, otherwise pass it along
                if (notificationData.Type == NotificationType.System)
                {
                    switch (notificationData.SubType)
                    {
                        case SubType.SettingsUpdate:
                            await this.UpdateSettingsAsync();
                            break;
                        case SubType.Restart:
                            Log.Information("Restart notification received");
                            this.exitCode = (int)ExitCode.ConnectionRestart;
                            break;
                        case SubType.Shutdown:
                            Log.Information("Shutdown notification received");
                            this.exitCode = (int)ExitCode.ExpectedShutdown;
                            break;
                        default:
                            Log.Error($"Error processing notification, unrecognized subtype: {notificationData.SubType}");
                            break;
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
                        Log.Error(ex, "Error processing notification");
                    }
                }
            }

            // For now, no retries. Regardless of notification send success, complete it.
            await args.CompleteMessageAsync(args.Message);
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

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!this.isDisposed && disposing)
            {
                Log.Debug("dispose start");
                this.heartbeatTimer?.Dispose();
                this.seenTimer?.Dispose();
                this.listenerHost?.Dispose();
                this.DogStats?.Dispose();
                Log.Debug("dispose end");
            }

            this.isDisposed = true;
        }

        protected void UpdateSeen(string key, SeenUserData seenUserData)
        {
            if (this.Config.SeenEndpoint != null && !string.IsNullOrEmpty(seenUserData.Name))
            {
                seenUserData.Timestamp = Utilities.Utime;
                this.seenUsers[key] = seenUserData;
            }
        }

        public SeenUserData GetSeen(string key)
        {
            return this.seenUsers.TryGetValue(key, out var seenData) ? seenData : null;
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

            if (settings.SeenEnabled)
            {
                var userKey = messageData.UserId ?? messageData.UserName;
                this.UpdateSeen(messageData.Server + userKey, new SeenUserData
                {
                    Name = userKey,
                    Channel = messageData.Channel,
                    Server = messageData.Server,
                });
            }

            var httpMatch = Consts.UrlRegex.Match(messageData.Content);
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

                    if (repeat.Nicks.Count == settings.RepeatCount)
                    {
                        var commandKey = $"{messageData.Channel}_{messageData.Server}";
                        this.Throttler.Increment(commandKey, ThrottleType.Repeat);

                        if (!this.Throttler.IsThrottled(commandKey, ThrottleType.Repeat))
                        {
                            Log.Debug($"Sending repeat response to message {messageData.MessageId} by {messageData.UserId} to {messageData.Channel} on guild {messageData.Server}");
                            await this.RespondAsync(messageData, messageData.Content);
                        }
                        else
                        {
                            Log.Debug("repeat throttled");
                        }

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
                if (CommandsConfig.Instance.TryParseForCommand(messageData.Content, mentionsBot, out _, out string query))
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

                        if (this.Throttler.IsThrottled(commandKey, ThrottleType.Command))
                        {
                            return responseData;
                        }

                        this.Throttler.Increment(commandKey, ThrottleType.Command);

                        // if we're now throttled after this increment, return a "rate limited" message
                        if (this.Throttler.IsThrottled(commandKey, ThrottleType.Command))
                        {
                            responses.Add("rate limited try later");
                            return responseData;
                        }

                        // increment the user/guild throttlers as well
                        this.Throttler.Increment(messageData.UserId, ThrottleType.User);
                        this.Throttler.Increment(messageData.Server, ThrottleType.Guild);

                        messageData.RateLimitChecked = true;
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

                    using (this.DogStats?.StartTimer("commandDuration", tags: new[] { $"shard:{this.Shard}", $"command:{command.ToLowerInvariant()}", $"{this.BotType}" }))
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

                if (response == null && PhrasesConfig.Instance.ExactPhrases.TryGetValue(messageData.Content, out var phrase) && (settings.FunResponsesEnabled && this.Random.Next(1, 100) <= settings.FunResponseChance || IsAuthorOwner(messageData)))
                {
                    if (settings.SasshatEnabled && PhrasesConfig.Instance.Responses.TryGetValue($"{phrase}_nice", out var phraseResponses) || PhrasesConfig.Instance.Responses.TryGetValue(phrase, out phraseResponses))
                    {
                        response = phraseResponses.Random();
                    }
                }

                if (response == null)
                {
                    response = settings.CustomCommands.FirstOrDefault(c => c.IsExactMatch && c.Command == messageData.Content || !c.IsExactMatch && messageData.Content.IContains(c.Command))?.Response;
                }

                if (response != null)
                {
                    response = response.Replace("%from%", messageData.UserName);
                    string[] resps = response.Split("||");
                    responseData.Responses.AddRange(resps);
                }
            }

            return responseData;
        }

        protected void TrackTimer(string eventName, double value, Dictionary<string, string> properties = null)
        {
            this.DogStats?.Timer(eventName, value, sampleRate: this.Config.Metrics.EventQueueSampleRate, tags: this.GetDogStatsTags(properties));
        }

        protected void TrackEvent(string eventName, Dictionary<string, string> properties = null)
        {
            this.DogStats?.Increment(eventName, tags: this.GetDogStatsTags(properties));
        }

        private string[] GetDogStatsTags(Dictionary<string, string> properties)
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


            return tags.ToArray();
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
                    using (var store = new X509Store(this.Config.CertStoreName, StoreLocation.LocalMachine))
                    {
                        store.Open(OpenFlags.ReadOnly);
                        var certificates = store.Certificates.Find(X509FindType.FindByThumbprint, this.Config.CertThumbprint, validOnly: false);
                        if (certificates?.Count > 0)
                        {
                            cert = certificates[0];
                        }
                    }
                }

                var protocol = cert == null ? "http" : "https";
                var listenerUrl = $"{protocol}://{this.Config.WebListenerHostName}:{port}";

                Log.Information($"Web listener configured on {listenerUrl}");

                this.listenerHost = new WebHostBuilder()
                    .UseKestrel(options =>
                    {
                        options.ListenAnyIP(port, l =>
                        {
                            if (cert != null)
                            {
                                l.UseHttps(h =>
                                {
                                    h.ServerCertificate = cert;
                                });
                            }
                        });
                    })
                    .UseUrls($"http://localhost:{port}", listenerUrl)
                    .UseStartup<Program>()
                    .Build();
                this.listenerHost.Start();
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
                Log.Debug("Fetching server settings...");
                var sinceToken = SettingsConfig.Instance.SinceToken;
                var configEndpoint = this.Config.SettingsEndpoint.AppendQueryParam("since", $"{sinceToken}").AppendQueryParam("shard", $"{this.Shard}").AppendQueryParam("shardcount", $"{this.TotalShards}");
                await SettingsConfig.Instance.OverrideAsync(configEndpoint);
                Log.Debug("Server settings updated.");
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to update server settings");
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
                seenTimer = new Timer(SeenTimerAsync, null, 120000, 120000);
            }

            heartbeatTimer = new Timer(HeartbeatTimerAsync, null, 60000, 60000 * 5);
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
                    await this.Config.SeenEndpoint.PostJsonAsync(new { Users = seenCopy.Values});
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Error in seen callback");
                }
            }
        }

        /// <summary>
        /// Timer callback to push heartbeat data and refresh configuration.
        /// </summary>
        /// <param name="state">State object (unused).</param>
        private async void HeartbeatTimerAsync(object state)
        {
            Log.Verbose("Heartbeat");

            if (this is DiscordBot discordBot)
            {
                var metric = new MetricTelemetry
                {
                    Name = "Guilds",
                    Count = discordBot.Client.Guilds.Count(),
                };

                this.AppInsights?.TrackMetric(metric);

                this.DogStats?.Gauge("guildsCount", discordBot.Client.Guilds.Count(), tags: new[] { $"shard:{this.Shard}", $"{this.BotType}" });
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
                    Log.Error(ex, "Error sending heartbeat data");
                }
            }

            if (this.messageCount == 0 && this is DiscordBot)
            {
                this.missedHeartbeats++;

                if (missedHeartbeats >= this.Config.MissedHeartbeatLimit)
                {
                    this.missedHeartbeats = 0;
                    this.exitCode = (int)ExitCode.ConnectionRestart;
                    if (this.Config.AlertEndpoint != null)
                    {
                        string messageContent = $"\U0001F501 {this.BotType} Shard {this.Shard} triggered automatic restart due to inactivity";
                        try
                        {
                            await this.Config.AlertEndpoint.PostJsonAsync(new { content = messageContent });
                        }
                        catch (Exception ex)
                        {
                            Log.Error(ex, "Heartbeat failure alert error");
                        }
                    }
                }
            }
            else
            {
                // reset message count
                this.messageCount = 0;
                this.missedHeartbeats = 0;
            }

            if (this.Config.CommandsEndpoint != null)
            {
                try
                {
                    Log.Debug("Fetching commands settings...");
                    await CommandsConfig.Instance.OverrideAsync(this.Config.CommandsEndpoint);
                    Log.Debug("Commands settings updated.");
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "Failed to update commands settings");
                }
            }

            if (this.Config.PhrasesEndpoint != null)
            {
                try
                {
                    Log.Debug("Fetching phrases settings...");
                    await PhrasesConfig.Instance.OverrideAsync(this.Config.PhrasesEndpoint);
                    Log.Debug("Phrases settings updated.");
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "Failed to update phrases settings");
                }
            }
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
            if (!string.IsNullOrEmpty(this.Config.InstrumentationKey))
            {
                var telemetryConfig = new TelemetryConfiguration
                {
                    InstrumentationKey = this.Config.InstrumentationKey,
                };

                telemetryConfig.TelemetryChannel.DeveloperMode = this.Config.IsDevMode;

                this.AppInsights = new TelemetryClient(telemetryConfig);
                this.AppInsights.Context.GlobalProperties.Add("Shard", this.Shard.ToString());
                this.AppInsights.Context.GlobalProperties.Add("BotType", this.BotType.ToString());
                this.AppInsights.Context.GlobalProperties.Add("Version", Assembly.GetEntryAssembly().GetName().Version.ToString());
            }

            var dogstatsdConfig = new StatsdConfig
            {
                StatsdServerName = "127.0.0.1"
            };

            this.DogStats = new DogStatsdService();
            this.DogStats.Configure(dogstatsdConfig);
        }
    }
}
