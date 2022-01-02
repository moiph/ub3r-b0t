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
        IUserMessage Message { get; }
        SocketInteraction Interaction { get; }
        SocketUserMessage SocketMessage { get; }
        IMessageChannel Channel { get; }
        SocketGuildChannel GuildChannel { get; }
        SocketGuildUser CurrentUser { get; }
        IUser Author { get; }
        Settings Settings { get; }
        string Reaction { get; }
        IUser ReactionUser { get; }
        DiscordBot Bot { get; }
    }

    public class DiscordBotContext : IDiscordBotContext
    {
        public DiscordBotContext(DiscordSocketClient client, IUserMessage message)
        {
            this.Client = client;
            this.Message = message;
            this.MessageData = BotMessageData.Create(message, this.Settings);
        }

        public DiscordBotContext(DiscordSocketClient client, SocketInteraction interaction, IUserMessage message)
        {
            this.Client = client;
            this.Interaction = interaction;
            this.MessageData = BotMessageData.Create(interaction, message, this.Settings);
            this.Message = message;
        }

        public DiscordSocketClient Client { get; }
        public AudioManager AudioManager { get; set; }
        public BotApi BotApi { get; set; }
        public DiscordBot Bot { get; set; }

        public IUserMessage Message { get; }
        public SocketInteraction Interaction { get; }
        public SocketUserMessage SocketMessage => Message as SocketUserMessage;
        public SocketGuildChannel GuildChannel => Message?.Channel as SocketGuildChannel ?? Interaction?.Channel as SocketGuildChannel;
        public IMessageChannel Channel => Message?.Channel ?? Interaction?.Channel;
        public SocketGuildUser CurrentUser => this.GuildChannel?.Guild.CurrentUser;
        public IUser Author => this.Message?.Author ?? this.Interaction?.User;
        public string Reaction { get; set; }
        public IUser ReactionUser { get; set; }

        public Settings Settings => SettingsConfig.GetSettings(GuildChannel?.Guild.Id.ToString());
        public BotMessageData MessageData { get; }
    }
}
