using System.Collections.Generic;

namespace UB3RB0T
{
    public class GuildPermisssionsData
    {
        public Dictionary<ulong, GuildChannelPermissions> Channels { get; set; } = new Dictionary<ulong, GuildChannelPermissions>();
        public Dictionary<ulong, EmojiData> Emoji { get; set; } = new Dictionary<ulong, EmojiData>();
        public int HighestRolePosition { get; set; }
    }

    public class GuildChannelPermissions
    {
        public bool CanSend { get; set; }
        public bool CanRead { get; set; }
        public bool CanEmbed { get; set; }
        public bool CanSpeak { get; set; }
    }

    public class EmojiData
    {
        public string Name { get; set; }
        public string Url { get; set; }
    }
}