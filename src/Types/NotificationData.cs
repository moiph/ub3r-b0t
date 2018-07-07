namespace UB3RB0T
{
    using System;

    [Flags]
    public enum NotificationType
    {
        Generic = 0,
        Rss = 1,
        Twitter = 2,
        Twitch = 4,
        Beam = 8,
        Trello = 16,
        Picarto = 32,
        Reminder = 64,
        System = 128, // used for internal system notifications
    }

    public enum SubType
    {
        SettingsUpdate,
    }

    public class NotificationData
    {
        public string Id { get; set; }
        public string Channel { get; set; }
        public string Server { get; set; }
        public NotificationType Type { get; set; } = NotificationType.Generic;
        public SubType SubType { get; set; }
        public string Text { get; set; }
        public BotType BotType { get; set; } = BotType.Discord;
        public EmbedData Embed { get; set; }
    }
}
