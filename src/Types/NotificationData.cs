namespace UB3RB0T
{
    using System;

    [Flags]
    public enum NotificationType
    {
        Rss = 1,
        Twitter = 2,
        Twitch = 4,
        Beam = 8,
        Trello = 16,
        Picarto = 32,
    }

    public class NotificationData
    {
        public string Id { get; set; }
        public string Channel { get; set; }
        public string Server { get; set; }
        public NotificationType Type { get; set; } = NotificationType.Rss;
        public string Text { get; set; }
        public BotType BotType { get; set; } = BotType.Discord;
        public EmbedData Embed { get; set; }
    }
}
