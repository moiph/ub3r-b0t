namespace UB3RB0T
{
    using System;
    using Discord;
    using Discord.WebSocket;

    [AttributeUsage(AttributeTargets.Class)]
    public sealed class UserPermissionsAttribute : PermissionsAttribute
    {
        public ChannelPermission? ChannelPermission { get; }
        public GuildPermission? GuildPermission { get; }

        public UserPermissionsAttribute(ChannelPermission channelPermission, string failureMessage = null)
        {
            this.ChannelPermission = channelPermission;
            this.FailureMessage = failureMessage;
        }

        public UserPermissionsAttribute(GuildPermission guildPermission, string failureMessage = null)
        {
            this.GuildPermission = guildPermission;
            this.FailureMessage = failureMessage;
        }

        public override bool CheckPermissions(IDiscordBotContext context)
        {
            var guildUser = context.Message.Author as SocketGuildUser;

            if (guildUser.Id == BotConfig.Instance.Discord.OwnerId)
            {
                return true;
            }

            if (this.GuildPermission.HasValue)
            {
                return guildUser?.GuildPermissions.Has(this.GuildPermission.Value) ?? false;
            }

            if (this.ChannelPermission.HasValue)
            {
                var textChannel = context.Message.Channel as ITextChannel;
                return guildUser?.GetPermissions(textChannel).Has(this.ChannelPermission.Value) ?? false;
            }

            return false;
        }
    }
}
