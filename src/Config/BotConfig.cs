namespace UB3RB0T
{
    using System;
    using Newtonsoft.Json;
    using System.Collections.Generic;
    using Serilog.Events;

    public class BotConfig : JsonConfig<BotConfig>
    {
        protected override string FileName => "botconfig.json";

        [JsonRequired]
        public string Name { get; set; }

        // Set to true if this is a local dev instance
        public bool IsDevMode { get; set; }
        
        // Bot api endpoint, if applicable.
        public Uri ApiEndpoint { get; set; }
        public string ApiKey { get; set; }

        // Server settings endpoint, if applicable.
        public Uri SettingsEndpoint { get; set; }

        // Server pruning endpoint, if applicable.
        // Called upon guild leave (useful to clear out service state, e.g. settings, reminders, etc)
        public Uri PruneEndpoint { get; set; }

        public Uri SeenEndpoint { get; set; }

        public ulong FaqChannel { get; set; }
        public Uri FaqEndpoint { get; set; }
        public string FaqKey { get; set; }

        public string VisionKey { get; set; }
        public Uri OcrEndpoint { get; set; }
        public Uri AnalyzeEndpoint { get; set; }
        public ulong[] OcrAutoIds { get; set; } = new ulong[] { };

        public DiscordConfig Discord { get; set; }
        public Irc Irc { get; set; }

        // endpoint to send heartbeat data to
        public Uri HeartbeatEndpoint { get; set; }
        // endpoint to send alerts (e.g. bot restarts) to
        public Uri AlertEndpoint { get; set; }

        // Connection string for Azure Service Bus (for notifications)
        public string ServiceBusConnectionString { get; set; }
        public string QueueNamePrefix { get; set; }

        /// Hostname to listen on for incoming http requests (for monitoring)
        public string WebListenerHostName { get; set; }

        // Instrumentation key for application insights
        public string InstrumentationKey { get; set; }

        public string CertThumbprint { get; set; }

        public LogEventLevel LogEventLevel { get; set; } = LogEventLevel.Warning;

        // Whether or not to log outgoing messages
        public bool LogOutgoing { get; set; }

        public string LogsPath { get; set; }

        public Dictionary<ThrottleType, Throttle> Throttles { get; set; } = new Dictionary<ThrottleType, Throttle>();
    }

    public class DiscordConfig
    {
        [JsonRequired]
        public string Token { get; set; }

        [JsonRequired]
        public ulong OwnerId { get; set; }

        [JsonRequired]
        public ulong BotId { get; set; }

        public string Status { get; set; }

        // Servers that are blocked from normal usage (owner being exempt)
        // Useful for things liek large test servers.
        public HashSet<ulong> BlockedServers { get; set; } = new HashSet<ulong>();

        // Globally blocked users (e.g. rule breakers, bot abusers, rustlers, cut throats, murderers, bounty hunters, desperados, mugs, pugs, thugs, nitwits, halfwits, dimwits, vipers, snipers, con men, and so on)
        public HashSet<ulong> BlockedUsers { get; set; } = new HashSet<ulong>();

        // Webhooks to allow through processing
        public HashSet<ulong> AllowedWebhooks { get; set; } = new HashSet<ulong>();

        public HashSet<ulong> Patrons { get; set; } = new HashSet<ulong>();

        public HashSet<ulong> SpecialUsers { get; set; } = new HashSet<ulong>();

        /// <summary>
        /// Endpoints for bot lists
        /// </summary>
        public BotStatData[] BotStats { get; set; } = new BotStatData[] { };

        /// <summary>
        /// Outgoing webhooks, to send messages for particular channels to...wherever
        /// </summary>
        public Dictionary<ulong, OutgoingWebhook> OutgoingWebhooks { get; set; } = new Dictionary<ulong, OutgoingWebhook>();

        /// <summary>
        /// Set to true to trigger typing state when command usage is detected
        /// Typing state will be exited after the command is processed and reply sent.
        /// </summary>
        public bool TriggerTypingOnCommands { get; set; }
    }

    public class OutgoingWebhook
    {
        public Uri Endpoint { get; set; }
        public string UserName { get; set; }
        public ulong MentionUserId { get; set; }
        public string MentionText { get; set; }
    }

    public class BotStatData
    {
        public string Name { get; set; }
        public string Key { get; set; }
        public string Endpoint { get; set; }
        public dynamic Payload { get; set; }
        public bool Enabled { get; set; }
        // Mapping of known property to payload property name, e.g. guildCount => server_count or guildCount => guilds
        public Dictionary<string, string> PayloadProps { get; set; }
    }

    public class Throttle
    {
        public uint Limit { get; set; }
        public uint PeriodInMinutes { get; set; }
    }

    public class Irc
    {
        [JsonRequired]
        public IrcServer[] Servers { get; set; }
    }

    public class IrcServer
    {
        /// <summary>
        /// The host for this server (e.g. irc.freenode.net)
        /// </summary>
        [JsonRequired]
        public string Host { get; set; }

        /// <summary>
        /// The port for the server (defaults to 6667)
        /// </summary>
        public int Port { get; set; } = 6667;

        /// <summary>
        /// Whether or not to use SSL when connecting to this server.
        /// </summary>
        public bool UseSsl { get; set; }

        /// <summary>
        /// List of channels to join.
        /// </summary>
        public string[] Channels { get; set; }

        /// <summary>
        /// Nickname to use; if empty, uses the global bot Name.
        /// </summary>
        public string Nick { get; set; } = null;

        /// <summary>
        /// Password to connect to the server (optional)
        /// </summary>
        public string Password { get; set; } = null;

        /// <summary>
        /// Flags whether or not this server is enabled.
        /// </summary>
        public bool Enabled { get; set; } = true;
    }

    public class InvalidConfigException : Exception
    {
        public InvalidConfigException(string message)
            : base(message)
        {
        }
    }
}
