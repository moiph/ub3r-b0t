namespace UB3RB0T.Modules
{
    using System.Threading.Tasks;

    public abstract class BaseDiscordModule : IModule
    {
        public Task<ModuleResult> Process(IBotContext botContext) => 
            botContext is IDiscordBotContext discordContext ? 
                this.ProcessDiscordModule(discordContext) : 
                Task.FromResult(ModuleResult.Continue);
        public abstract Task<ModuleResult> ProcessDiscordModule(IDiscordBotContext moduleContext);
    }
}
