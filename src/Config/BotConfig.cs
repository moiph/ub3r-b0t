namespace UB3RB0T
{
    using System;
    using Newtonsoft.Json;
    using System.Collections.Generic;

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

        public DiscordConfig Discord { get; set; }
        public Irc Irc { get; set; }

        // endpoint to send heartbeat data to
        public Uri HeartbeatEndpoint { get; set; }
        // endpoint to send alerts (e.g. bot restarts) to
        public Uri AlertEndpoint { get; set; }

        /// Hostname to listen on for incoming http requests (for monitoring)
        public string WebListenerHostName { get; set; }

        // Instrumentation key for application insights
        public string InstrumentationKey { get; set; }
    }

    public class DiscordConfig
    {
        [JsonRequired]
        public string Token { get; set; }

        [JsonRequired]
        public ulong OwnerId { get; set; }

        public int ShardCount { get; set; } = 1;

        public string Status { get; set; }

        // Servers that are blocked from normal usage (owner being exempt)
        // Useful for things liek large test servers.
        public HashSet<ulong> BlockedServers { get; set; }

        public HashSet<ulong> Patrons { get; set; }

        /// <summary>
        /// Key for statistics on https://www.carbonitex.net
        /// </summary>
        public string CarbonStatsKey { get; set; }

        /// <summary>
        /// Key for statistics on https://bots.discord.pw
        /// </summary>
        public string DiscordBotsKey { get; set; }

        /// <summary>
        /// Key for statistics on https://bots.discordlist.net/
        /// </summary>
        public string DiscordListKey { get; set; }

        /// <summary>
        /// Key for statistics on https://discordbots.org/
        /// </summary>
        public string DiscordBotsOrgKey { get; set; }

        /// <summary>
        /// Outgoing webhooks, to send messages for particular channels to...wherever
        /// </summary>
        public Dictionary<ulong, OutgoingWebhook> OutgoingWebhooks { get; set; } = new Dictionary<ulong, OutgoingWebhook>();
    }

    public class OutgoingWebhook
    {
        public Uri Endpoint { get; set; }
        public string UserName { get; set; }
        public ulong MentionUserId { get; set; }
        public string MentionText { get; set; }
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
