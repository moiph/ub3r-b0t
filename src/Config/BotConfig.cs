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

        public DiscordConfig Discord { get; set; }
        public Irc Irc { get; set; }

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


        /// <summary>
        /// Key for statistics on https://www.carbonitex.net
        /// </summary>
        public string CarbonStatsKey { get; set; }

        /// <summary>
        /// Key for statistics on https://bots.discord.pw
        /// </summary>
        public string DiscordBotsKey { get; set; }
    }

    public class Irc
    {
        [JsonRequired]
        public IrcServer[] Servers { get; set; }
    }

    public class IrcServer
    {
        /// <summary>
        /// Unique identifier for internal use.
        /// </summary>
        [JsonRequired]
        public string Id { get; set; }

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
