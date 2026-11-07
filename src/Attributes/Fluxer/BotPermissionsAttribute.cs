namespace UB3RB0T
{
    using Fluxer.Net;
    using System;
    using System.Threading.Tasks;

    [AttributeUsage(AttributeTargets.Class)]
    public sealed class FluxerBotPermissionsAttribute : FluxerPermissionsAttribute
    {
        public ChannelPermission? ChannelPermission { get; }
        public GuildPermission? GuildPermission { get; }

        public FluxerBotPermissionsAttribute(ChannelPermission channelPermission, string? failureString = null)
        {
            this.ChannelPermission = channelPermission;
            this.FailureString = failureString;
        }

        public FluxerBotPermissionsAttribute(GuildPermission guildPermission, string? failureString = null)
        {
            this.GuildPermission = guildPermission;
            this.FailureString = failureString;
        }

        public override async Task<bool> CheckPermissions(IFluxerBotContext context)
        {
            if (context.MessageData.FluxerMessageData is not SocketMessage message || message.Guild == null)
            {
                return false;
            }

            var botGuildUser = message.Guild.CurrentMember;

            if (this.GuildPermission.HasValue)
            {
                return botGuildUser.HasPermission(this.GuildPermission.Value);
            }

            if (this.ChannelPermission.HasValue)
            {
                return botGuildUser.GetPermissions(message.Channel).RawValue.HasFlag(this.ChannelPermission.Value);
            }

            return false;
        }
    }
}
