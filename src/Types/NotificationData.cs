namespace UB3RB0T
{
    public enum NotificationType
    {
        Rss,
        Twitter,
        Twitch,
        Beam,
        Trello,
    }

    public class NotificationData
    {
        public string Id { get; set; }
        public string Channel { get; set; }
        public string Server { get; set; }
        public NotificationType Type { get; set; }
        public string Text { get; set; }
        public BotType BotType { get; set; }
        public EmbedData Embed { get; set; }
    }
}
