namespace UB3RB0T
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Settings for individual servers
    /// </summary>
    public class SettingsConfig : JsonConfig<SettingsConfig>
    {
        protected override string FileName => "settingsconfig.json";

        public Uri ManagementEndpoint { get; set; }
        public Uri CreateEndpoint { get; set; }

        public Dictionary<string, Settings> Settings { get; set; } = new Dictionary<string, Settings>();

        public static Settings GetSettings(ulong serverId)
        {
            return SettingsConfig.GetSettings(serverId.ToString());
        }

        public static Settings GetSettings(string serverId)
        {
            if (serverId == null)
            {
                return new Settings();
            }

            if (!SettingsConfig.Instance.Settings.TryGetValue(serverId, out var settings))
            {
                settings = new Settings();
            }

            return settings;
        }
    }

    // TODO: Remove moderation logging functionality once Discord audit log features are complete
    [Flags]
    public enum ModOptions
    {
        Mod_LogEdit = 0x1,
        Mod_LogDelete = 0x2,
        Mod_LogUserBan = 0x4,
        Mod_LogUserNick = 0x8,
        Mod_LogUserRole = 0x16,
        Mod_LogUserJoin = 0x32,
        Mod_LogUserLeave = 0x64,
    }

    public class Settings
    {
        public ulong Id { get; set; }

        public string Greeting { get; set; }
        public ulong GreetingId { get; set; }
        public string Farewell { get; set; }
        public ulong FarewellId { get; set; }
        public ulong UpdateId { get; set; }

        public HashSet<string> WordCensors { get; set; } = new HashSet<string>();

        public HashSet<string> Channels { get; set; } = new HashSet<string>();
        public bool IsChannelListBlock { get; set; } = true;

        public HashSet<string> DisabledCommands { get; set; } = new HashSet<string>();
        public string Prefix { get; set; } = ".";

        public bool Mod_ImgLimit { get; set; }
        public ulong Mod_LogId { get; set; }
        public ModOptions Mod_LogOptions { get; set; }

        public bool FunResponsesEnabled { get; set; }
        public bool AutoTitlesEnabled { get; set; }

        public bool HasFlag(ModOptions flag)
        {
            return (this.Mod_LogOptions & flag) == flag;
        }

        public bool IsCommandDisabled(CommandsConfig commandsConfig, string command)
        {
            if (string.IsNullOrEmpty(command))
            {
                return false;
            }

            return commandsConfig.Commands.ContainsKey(command) && this.DisabledCommands.Contains(commandsConfig.Commands[command]);
        }
    }
}
