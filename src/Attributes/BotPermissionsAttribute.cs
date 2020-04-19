namespace UB3RB0T
{
    using System;
    using Discord;
    using Discord.WebSocket;

    [AttributeUsage(AttributeTargets.Class)]
    public sealed class BotPermissionsAttribute : PermissionsAttribute
    {
        public ChannelPermission? ChannelPermission { get; }
        public GuildPermission? GuildPermission { get; }

        public BotPermissionsAttribute(ChannelPermission channelPermission, string failureMessage = null)
        {
            this.ChannelPermission = channelPermission;
            this.FailureMessage = failureMessage;
        }

        public BotPermissionsAttribute(GuildPermission guildPermission, string failureMessage = null)
        {
            this.GuildPermission = guildPermission;
            this.FailureMessage = failureMessage;
        }

        public override bool CheckPermissions(IDiscordBotContext context)
        {
            var botGuildUser = (context.Message.Channel as SocketGuildChannel)?.Guild.CurrentUser;

            if (this.GuildPermission.HasValue)
            {
                return botGuildUser?.GuildPermissions.Has(this.GuildPermission.Value) ?? false;
            }

            if (this.ChannelPermission.HasValue)
            {
                var textChannel = context.Message.Channel as ITextChannel;
                return botGuildUser?.GetPermissions(textChannel).Has(this.ChannelPermission.Value) ?? false;
            }

            return false;
        }
    }
}
