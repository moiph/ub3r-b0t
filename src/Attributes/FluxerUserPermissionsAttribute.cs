namespace UB3RB0T
{
    using Fluxer.Net;
    using System;
    using System.Threading.Tasks;

    [AttributeUsage(AttributeTargets.Class)]
    public sealed class FluxerUserPermissionsAttribute : FluxerPermissionsAttribute
    {
        public ChannelPermission? ChannelPermission { get; }
        public GuildPermission? GuildPermission { get; }

        public FluxerUserPermissionsAttribute(ChannelPermission channelPermission, string failureString = null)
        {
            this.ChannelPermission = channelPermission;
            this.FailureString = failureString;
        }

        public FluxerUserPermissionsAttribute(GuildPermission guildPermission, string failureString = null)
        {
            this.GuildPermission = guildPermission;
            this.FailureString = failureString;
        }

        public override async Task<bool> CheckPermissions(IFluxerBotContext context)
        {
            if (context.MessageData.FluxerMessageData is not SocketMessage message || message.Guild?.Id == null)
            {
                return false;
            }

            if (context.Author.Id.ToString() == BotConfig.Instance.Fluxer.OwnerId)
            {
                return true;
            }

            var guild = context.Client.Gateway.GetGuild((ulong)message.Guild.Id);

            if (guild.OwnerId == context.Author.Id)
            {
                return true;
            }

            if (this.GuildPermission.HasValue)
            {
                return message.Member?.HasPermission(this.GuildPermission.Value) ?? false;
            }

            if (this.ChannelPermission.HasValue)
            {
                return message.Member?.GetPermissions(message.Channel).RawValue.HasFlag(this.ChannelPermission.Value) ?? false;
            }

            return false;
        }
    }
}
