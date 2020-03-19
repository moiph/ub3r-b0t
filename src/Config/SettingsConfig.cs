namespace UB3RB0T
{
    using Serilog;
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text.RegularExpressions;
    using System.Threading.Tasks;

    /// <summary>
    /// Settings for individual servers
    /// </summary>
    public class SettingsConfig : JsonConfig<SettingsConfig>
    {
        protected override string FileName => "settingsconfig.json";

        public Uri ManagementEndpoint { get; set; }
        public Uri CreateEndpoint { get; set; }
        public int SinceToken { get; set; }

        public Dictionary<string, Settings> Settings { get; set; } = new Dictionary<string, Settings>();

        public override async Task OverrideAsync(Uri uri)
        {
            var config = await Utilities.GetApiResponseAsync<SettingsConfig>(uri);
            if (config != null)
            {
                foreach (var serverSetting in this.Settings)
                {
                    if (!config.Settings.ContainsKey(serverSetting.Key))
                    {
                        config.Settings.Add(serverSetting.Key, serverSetting.Value);
                    }
                }

                JsonConfig.AddOrSetInstance<SettingsConfig>(instanceKey, config);
            }
            else
            {
                Log.Error($"Config overide for {uri} was null");
            }
        }

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

        public static void RemoveSettings(string serverId)
        {
            SettingsConfig.Instance.Settings.Remove(serverId);
        }
    }

    // TODO: Remove moderation logging functionality once Discord audit log features are complete
    [Flags]
    public enum ModOptions
    {
        Mod_LogEdit = 1,
        Mod_LogDelete = 2,
        Mod_LogUserBan = 4,
        Mod_LogUserNick = 8,
        Mod_LogUserRole = 16,
        Mod_LogUserJoin = 32,
        Mod_LogUserLeave = 64,
        Mod_LogUserJoinVoice = 128,
        Mod_LogUserLeaveVoice = 256,
    }

    public class CustomCommand
    {
        public string Command { get; set; }
        public string Response { get; set; }
        public bool IsExactMatch { get; set; }
    }

    public class NotificationText
    {
        public NotificationType Type { get; set; }
        public string Text { get; set; }
    }

    public class Settings
    {
        private List<Regex> regexWordCensors;

        public ulong Id { get; set; }

        public string Greeting { get; set; }
        public ulong GreetingId { get; set; }
        public string Farewell { get; set; }
        public ulong FarewellId { get; set; }
        public ulong VoiceId { get; set; }
        public ulong UpdateId { get; set; }
        public ulong JoinRoleId { get; set; }

        public List<NotificationText> NotificationText = new List<NotificationText>();

        public HashSet<string> WordCensors { get; set; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> RegexCensors { get; set; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public HashSet<ulong> SelfRoles { get; set; } = new HashSet<ulong>();
        public HashSet<string> DisabledCommands { get; set; } = new HashSet<string>();
        public List<CustomCommand> CustomCommands = new List<CustomCommand>();
        public string Prefix { get; set; } = ".";

        public bool Mod_ImgLimit { get; set; }
        public ulong Mod_LogId { get; set; }
        public ModOptions Mod_LogOptions { get; set; }

        public ulong RoleAddEmoteId { get; set; }
        public ulong RoleRemoveEmoteId { get; set; }

        public bool FunResponsesEnabled { get; set; }
        public int FunResponseChance { get; set; } = 100;
        public bool AutoTitlesEnabled { get; set; }
        public bool SeenEnabled { get; set; }

        public bool PreferEmbeds { get; set; }
        public NotificationType Notif_EmbedOptions { get; set; }

        public bool DisableLinkParsing { get; set; }

        public ulong PatronSponsor { get; set; } = 0;

        public bool HasFlag(ModOptions flag)
        {
            return (this.Mod_LogOptions & flag) == flag;
        }

        public bool HasFlag(NotificationType flag)
        {
            return (this.Notif_EmbedOptions & flag) == flag;
        }

        public bool IsCommandDisabled(CommandsConfig commandsConfig, string command)
        {
            if (string.IsNullOrEmpty(command))
            {
                return false;
            }

            return commandsConfig.Commands.ContainsKey(command) && this.DisabledCommands.Contains(commandsConfig.Commands[command]);
        }

        public bool TriggersCensor(string text, out string offendingWord)
        {
            offendingWord = null;
            // check boring word censors first, then regex
            if (this.WordCensors.Count > 0)
            {
                var words = text.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                offendingWord = words.FirstOrDefault(w => this.WordCensors.Contains(w, StringComparer.OrdinalIgnoreCase));

                if (!string.IsNullOrEmpty(offendingWord))
                {
                    return true;
                }
            }

            if (this.RegexCensors.Count > 0)
            {
                if (this.regexWordCensors == null)
                {
                    this.regexWordCensors = this.RegexCensors.Where(r => !string.IsNullOrWhiteSpace(r)).Select(r =>
                    {
                        try
                        {
                            return new Regex(r, RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(250)); 
                        }
                        catch (ArgumentException)
                        {
                            // TODO: Handle logging to alert the server owner
                            Log.Warning("Censor regex failure on guild {{Guild}} for {{Regex}}", this.Id, r);
                        }

                        return null;
                    }).ToList();
                }

                return this.regexWordCensors.Any(r =>
                {
                    try
                    {
                        return r.IsMatch(text);
                    }
                    catch (RegexMatchTimeoutException)
                    {
                        // TODO: Handle logging to alert server owner
                        Log.Warning("Censor regex timeout on guild {{Guild}} for {{Regex}}", this.Id, r);
                    }

                    return false;
                });
            }

            return false;
        }
    }
}
