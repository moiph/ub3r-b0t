
namespace UB3RB0T.Modules
{
    using System.Threading.Tasks;

    public abstract class WordCensorModule : IModule
    {
        protected abstract Task DeleteMessage(IBotContext context, string message);

        public async Task<ModuleResult> Process(IBotContext context)
        {
            // check for word censors; ignore if we can't delete messages
            if (context.MessageData.Content != null && context.Settings.TriggersCensor(context.MessageData.Content, out string? offendingWord))
            {
                offendingWord = offendingWord != null ? $"`{offendingWord}`" : "*FANCY lanuage filters*";
                var message = $"hi uh sorry but your most recent message was tripped up by {offendingWord} and thusly was deleted. complain to management, i'm just the enforcer";

                await this.DeleteMessage(context, message);

                return ModuleResult.Stop;
            }

            return ModuleResult.Continue;
        }
    }
}
