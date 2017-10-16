namespace UB3RB0T
{
    using System.Threading.Tasks;

    public abstract class BaseDiscordModule : IModule
    {
        public Task<ModuleResult> Process(IBotContext botContext) => this.ProcessDiscordModule(botContext as IDiscordBotContext);
        public abstract Task<ModuleResult> ProcessDiscordModule(IDiscordBotContext moduleContext);
    }
}
