namespace UB3RB0T
{
    using Discord;
    using Discord.WebSocket;

    public interface IBotContext
    {
        BotMessageData MessageData { get; }
        BotApi BotApi { get; }
    }

    public class BotContext
    {
        public BotMessageData MessageData { get; }
        public BotApi BotApi { get; }
    }

    public interface IDiscordBotContext : IBotContext
    {
        DiscordSocketClient Client { get; }
        AudioManager AudioManager { get; }
        SocketUserMessage Message { get; }
        SocketGuildChannel GuildChannel { get; }
        Settings Settings { get; }
        string Reaction { get; }
        IUser ReactionUser { get; }
    }

    public class DiscordBotContext : IDiscordBotContext
    {
        public DiscordBotContext(DiscordSocketClient client, SocketUserMessage message)
        {
            this.Client = client;
            this.Message = message;
            this.MessageData = BotMessageData.Create(Message, this.Settings);
        }

        public DiscordSocketClient Client { get; }
        public AudioManager AudioManager { get; set; }
        public BotApi BotApi { get; set; }

        public SocketUserMessage Message { get; }
        public SocketGuildChannel GuildChannel => Message.Channel as SocketGuildChannel;
        public string Reaction { get; set; }
        public IUser ReactionUser { get; set; }

        public Settings Settings => SettingsConfig.GetSettings((Message.Channel as SocketGuildChannel)?.Guild.Id.ToString());
        public BotMessageData MessageData { get; }
    }
}
