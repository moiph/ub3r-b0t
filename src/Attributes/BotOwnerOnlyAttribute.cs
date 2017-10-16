namespace UB3RB0T
{
    using System;
    using Discord.WebSocket;

    [AttributeUsage(AttributeTargets.Class)]
    public class BotOwnerOnlyAttribute : PermissionsAttribute
    {
        public BotOwnerOnlyAttribute(string failureMessage = null)
        {
            this.FailureMessage = failureMessage;
        }

        public override bool CheckPermissions(IDiscordBotContext context) =>
            (context.Message.Author as SocketGuildUser)?.Id == BotConfig.Instance.Discord.OwnerId;
    }
}
