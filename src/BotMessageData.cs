namespace UB3RB0T
{
    using Discord;
    using Discord.WebSocket;
    using UB3RIRC;

    /// <summary>
    /// Common message data for generalized use.  Massages data between various client libraries (IRC, Discord, etc)
    /// </summary>
    public class BotMessageData
    {
        public BotType BotType;

        public SocketUserMessage DiscordMessageData { get; private set; }
        public MessageData IrcMessageData { get; private set; }

        public string UserName { get; set; }
        public string UserId { get; set; }
        public string UserHost { get; set; }
        public string Channel { get; set; }
        public string Server { get; set; }
        public string Content { get; set; }

        public BotMessageData(BotType botType)
        {
            this.BotType = botType;
        }

        public static BotMessageData Create(MessageData ircMessageData, IrcClient ircClient)
        {
            return new BotMessageData(BotType.Irc)
            {
                IrcMessageData = ircMessageData,
                UserName = ircMessageData.Nick,
                UserHost = ircMessageData.Host,
                Channel = ircMessageData.Target,
                Server = ircClient.Host,
                Content = ircMessageData.Text,
            };
        }

        public static BotMessageData Create(SocketUserMessage discordMessageData)
        {
            return new BotMessageData(BotType.Discord)
            {
                DiscordMessageData = discordMessageData,
                UserName = discordMessageData.Author.Username,
                UserId = discordMessageData.Author.Id.ToString(),
                Channel = discordMessageData.Channel.Id.ToString(),
                Server = (discordMessageData.Channel as IGuildChannel)?.GuildId.ToString() ?? "private",
                Content = discordMessageData.Content,
            };
        }
    }
}
