namespace UB3RB0T
{
    using System;

    [AttributeUsage(AttributeTargets.Class)]
    public class GuildOwnerOnlyAttribute : PermissionsAttribute
    {
        public GuildOwnerOnlyAttribute(string failureMessage = null)
        {
            this.FailureMessage = failureMessage;
        }

        public override bool CheckPermissions(IDiscordBotContext context) =>
            context.Message.Author.Id == context.GuildChannel?.Guild.OwnerId;
    }
}
