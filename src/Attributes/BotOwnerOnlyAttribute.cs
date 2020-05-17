namespace UB3RB0T
{
    using System;
    using Discord.WebSocket;

    [AttributeUsage(AttributeTargets.Class)]
    public sealed class BotOwnerOnlyAttribute : PermissionsAttribute
    {
        public BotOwnerOnlyAttribute(string failureString = null)
        {
            this.FailureString = failureString;
        }

        public override bool CheckPermissions(IDiscordBotContext context) =>
            (context.Message.Author as SocketGuildUser)?.Id == BotConfig.Instance.Discord.OwnerId;
    }
}
