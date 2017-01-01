namespace UB3RB0T
{
    public class HeartbeatData
    {
        public string BotType { get; set; }
        public int Shard { get; set; }
        public int ServerCount { get; set; }
        public int UserCount { get; set; }
        public int VoiceChannelCount { get; set; }
        public long StartTime { get; set; }
    }
}
