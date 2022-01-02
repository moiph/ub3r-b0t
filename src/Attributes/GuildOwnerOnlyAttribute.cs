namespace UB3RB0T
{
    using System;

    [AttributeUsage(AttributeTargets.Class)]
    public class GuildOwnerOnlyAttribute : PermissionsAttribute
    {
        public GuildOwnerOnlyAttribute(string failureString = null)
        {
            this.FailureString = failureString;
        }

        public override bool CheckPermissions(IDiscordBotContext context) =>
            context.Author.Id == context.GuildChannel?.Guild.OwnerId;
    }
}
