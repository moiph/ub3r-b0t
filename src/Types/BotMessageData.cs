namespace UB3RB0T
{
    using System.Linq;
    using Discord;
    using Discord.WebSocket;
    using UB3RIRC;
    using Newtonsoft.Json;

    /// <summary>
    /// Common message data for generalized use.  Massages data between various client libraries (IRC, Discord, etc)
    /// </summary>
    public class BotMessageData
    {
        public BotType BotType;

        [JsonIgnore]
        public SocketUserMessage DiscordMessageData { get; private set; }
        [JsonIgnore]
        public MessageData IrcMessageData { get; private set; }

        public string UserName { get; set; }
        public string UserId { get; set; }
        public string UserHost { get; set; }
        public string Channel { get; set; }
        public string Server { get; set; }
        public string Content { get; set; }
        public string Command => Query.Split(new[] { ' ' }, 2)?[0];
        public string Prefix { get; set; }
        public string Query
        {
            get
            {
                string query = string.Empty;
                int argPos = 0;
                if (this.Content.HasMentionPrefix(BotConfig.Instance.Discord.BotId, ref argPos) == true)
                {
                    query = this.Content.Substring(argPos);
                }
                else if (this.Content.StartsWith(this.Prefix))
                {
                    query = this.Content.Substring(this.Prefix.Length);
                }

                return query;
            }
        }
        public string Format { get; set; }
        public bool RateLimitChecked { get; set; }

        public BotMessageData(BotType botType)
        {
            this.BotType = botType;
        }

        public bool MentionsBot(string botName, ulong? id)
        {
            if (this.BotType == BotType.Discord)
            {
                return this.DiscordMessageData.MentionedUsers.Count == 1 && this.DiscordMessageData.MentionedUsers.First().Id == id ||
                    this.Content.ToLowerInvariant().Contains(botName.ToLowerInvariant());
            }

            return this.IrcMessageData.Text.Contains(botName);
        }

        public static BotMessageData Create(MessageData ircMessageData, IrcClient ircClient, Settings serverSettings)
        {
            return new BotMessageData(BotType.Irc)
            {
                IrcMessageData = ircMessageData,
                UserName = ircMessageData.Nick,
                UserHost = ircMessageData.Host,
                Channel = ircMessageData.Target,
                Server = ircClient.Host,
                Content = ircMessageData.Text,
                Prefix = serverSettings.Prefix,
            };
        }

        public static BotMessageData Create(SocketUserMessage message, Settings serverSettings)
        {
            return new BotMessageData(BotType.Discord)
            {
                DiscordMessageData = message,
                UserName = message.Author.Username,
                UserId = message.Author.Id.ToString(),
                UserHost = message.Author.Id.ToString(),
                Channel = message.Channel.Id.ToString(),
                Server = (message.Channel as IGuildChannel)?.GuildId.ToString() ?? "private",
                Content = message.Content,
                Format = serverSettings.PreferEmbeds ? "embed" : string.Empty,
                Prefix = serverSettings.Prefix,
            };
        }
    }
}
