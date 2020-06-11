using System.Collections.Generic;

namespace UB3RB0T
{
    public class GuildPermisssionsData
    {
        public Dictionary<ulong, GuildChannelPermissions> Channels { get; set; } = new Dictionary<ulong, GuildChannelPermissions>();
    }

    public class GuildChannelPermissions
    {
        public bool CanSend { get; set; }
        public bool CanRead { get; set; }
        public bool CanEmbed { get; set; }
    }
}
