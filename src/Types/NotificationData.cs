namespace UB3RB0T
{
    using System;

    [Flags]
    public enum NotificationType
    {
        Generic = 0,
        Rss = 1 << 0,
        Twitter = 1 << 1,
        Twitch = 1 << 2,
        Beam = 1 << 3,
        Trello = 1 << 4,
        Picarto = 1 << 5,
        Reminder = 1 << 6,
        System = 1 << 7, // used for internal system notifications
        Feedback = 1 << 8,
        Bluesky = 1 << 9,
    }

    public enum SubType
    {
        SettingsUpdate,
        Shutdown,
        Reply,
        Restart,
    }

    public class NotificationData
    {
        public string Id { get; set; }
        public string Channel { get; set; }
        public string Server { get; set; }
        public string MessageId { get; set; }
        public string UserId { get; set; }
        public NotificationType Type { get; set; } = NotificationType.Generic;
        public SubType SubType { get; set; }
        public string Text { get; set; }
        public BotType BotType { get; set; } = BotType.Discord;
        public EmbedData Embed { get; set; }
        public bool AllowMentions { get; set; } = true;
    }
}
