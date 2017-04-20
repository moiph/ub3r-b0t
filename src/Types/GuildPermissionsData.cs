using System.Collections.Generic;

namespace UB3RB0T
{
    public class GuildPermisssionsData
    {
        public Dictionary<ulong, ChannelPermissions> Channels { get; set; } = new Dictionary<ulong, ChannelPermissions>();
    }

    public class ChannelPermissions
    {
        public bool CanSend { get; set; }
        public bool CanRead { get; set; }
    }
}
