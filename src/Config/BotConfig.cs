namespace UB3RB0T
{
    using System;
    using System.Collections.Generic;
    using Newtonsoft.Json;
    using Serilog.Events;
    using UB3RIRC;

    public class BotConfig : JsonConfig<BotConfig>
    {
        protected override string FileName => "botconfig.json";

        [JsonRequired]
        public string Name { get; set; }

        // Set to true if this is a local dev instance
        public bool IsDevMode { get; set; }
        
        // Bot api endpoint, if applicable.
        public Uri ApiEndpoint { get; set; }

        // Server settings endpoint, if applicable.
        public Uri SettingsEndpoint { get; set; }

        public Uri PhrasesEndpoint { get; set; }
        public Uri CommandsEndpoint { get; set; }

        public Uri SeenEndpoint { get; set; }

        public Dictionary<ulong, Faq> FaqEndpoints { get; set; }
        public string FaqKey { get; set; }

        public string VisionKey { get; set; }
        public Uri OcrEndpoint { get; set; }
        public Uri AnalyzeEndpoint { get; set; }
        public ulong[] OcrAutoIds { get; set; } = Array.Empty<ulong>();

        public DiscordConfig Discord { get; set; }
        public Irc Irc { get; set; }

        // endpoint to send heartbeat data to
        public Uri HeartbeatEndpoint { get; set; }
        public int MissedHeartbeatLimit { get; set; } = 10;
        // endpoint to send alerts (e.g. bot restarts) to
        public Uri AlertEndpoint { get; set; }

        // Connection string for Azure Service Bus (for notifications)
        public string ServiceBusConnectionString { get; set; }
        public string QueueNamePrefix { get; set; }

        /// Hostname to listen on for incoming http requests (for monitoring)
        public string WebListenerHostName { get; set; }
        // List of allowed incoming IPs for web queries
        public List<string> WebListenerInboundAddresses { get; set; } = new List<string>();

        // Instrumentation key for application insights
        public string InstrumentationKey { get; set; }

        public string CertStoreName { get; set; }
        public string CertThumbprint { get; set; }

        public LogEventLevel LogEventLevel { get; set; } = LogEventLevel.Warning;

        // Whether or not to log outgoing messages
        public bool LogOutgoing { get; set; }

        public string LogsPath { get; set; }
        public int LogsRetainedFileCount { get; set; } = 5;

        /// Settings for voice support
        public string VoiceFilePath { get; set; }

        public Dictionary<ThrottleType, Throttle> Throttles { get; set; } = new Dictionary<ThrottleType, Throttle>();

        public AprilFoolsConfig AprilFools { get; set; } = new AprilFoolsConfig();
    }

    public class AprilFoolsConfig
    {
        public int Chance { get; set; } = 0;
        public int Delay { get; set; } = 60000;
        public HashSet<ulong> IgnoreIds { get; set; } = new HashSet<ulong>();
        public string[] Responses { get; set; }
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

        public int EventQueueSize { get; set; } = 10;
        public int VoiceEventQueueSize { get; set; } = 2;
        public int MessageCacheSize { get; set; } = 20;

        // Servers that are blocked from normal usage (owner being exempt)
        // Useful for things like large test servers.
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
        public BotStatData[] BotStats { get; set; } = Array.Empty<BotStatData>();

        /// <summary>
        /// Set to true to trigger typing state when command usage is detected
        /// Typing state will be exited after the command is processed and reply sent.
        /// </summary>
        public bool TriggerTypingOnCommands { get; set; }

        /// <summary>
        /// Modules processed before commanding. Order matters.
        /// </summary>
        public List<string> PreProcessModuleTypes { get; set; }
        
        /// <summary>
        /// Modules processed after a command is successfully handled.
        /// </summary>
        public List<string> PostProcessModuleTypes { get; set; }

        /// <summary>
        /// Native commands
        /// </summary>
        public Dictionary<string, string> CommandTypes { get; set; }
    }

    public class BotStatData
    {
        public string Name { get; set; }
        public string Key { get; set; }
        public string Endpoint { get; set; }
        public string UserAgent { get; set; }
        public dynamic Payload { get; set; }
        public bool Enabled { get; set; }
        // Mapping of known property to payload property name, e.g. guildCount => server_count or guildCount => guilds
        public Dictionary<string, string> PayloadProps { get; set; }
    }

    public class Throttle
    {
        public uint Limit { get; set; }
        public uint PeriodInMinutes { get; set; }
        public uint PeriodInSeconds { get; set; }
    }

    public class Faq
    {
        public Uri Endpoint { get; set; }
        public string Reaction { get; set; }
        public string EndsWith { get; set; }
        public string Command { get; set; }
    }

    public class Irc
    {
        [JsonRequired]
        public IrcServer[] Servers { get; set; }

        public LogType LogType { get; set; } = LogType.Info;
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

        public InvalidConfigException()
        {
        }

        public InvalidConfigException(string message, Exception innerException) : base(message, innerException)
        {
        }
    }
}
