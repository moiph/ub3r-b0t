namespace UB3RB0T
{
    using System;

    [AttributeUsage(AttributeTargets.Class)]
    public abstract class PermissionsAttribute : Attribute
    {
        public string FailureString { get; protected set; }
        public abstract bool CheckPermissions(IDiscordBotContext botContext);
    }
}
