namespace UB3RB0T
{
    using System;
    using System.Threading.Tasks;

    [AttributeUsage(AttributeTargets.Class)]
    public abstract class PermissionsAttribute : Attribute
    {
        public string FailureString { get; protected set; }
        public abstract bool CheckPermissions(IDiscordBotContext botContext);
    }

    [AttributeUsage(AttributeTargets.Class)]
    public abstract class FluxerPermissionsAttribute : Attribute
    {
        public string FailureString { get; protected set; }
        public abstract Task<bool> CheckPermissions(IFluxerBotContext botContext);
    }
}
