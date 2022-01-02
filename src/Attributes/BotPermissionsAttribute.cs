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

        public BotPermissionsAttribute(ChannelPermission channelPermission, string failureString = null)
        {
            this.ChannelPermission = channelPermission;
            this.FailureString = failureString;
        }

        public BotPermissionsAttribute(GuildPermission guildPermission, string failureString = null)
        {
            this.GuildPermission = guildPermission;
            this.FailureString = failureString;
        }

        public override bool CheckPermissions(IDiscordBotContext context)
        {
            var botGuildUser = context.CurrentUser;

            if (this.GuildPermission.HasValue)
            {
                return botGuildUser?.GuildPermissions.Has(this.GuildPermission.Value) ?? false;
            }

            if (this.ChannelPermission.HasValue)
            {
                var textChannel = context.Channel as ITextChannel;
                return botGuildUser?.GetPermissions(textChannel).Has(this.ChannelPermission.Value) ?? false;
            }

            return false;
        }
    }
}
