namespace UB3RB0T
{
    using System.Threading.Tasks;
    using Discord;

    [BotPermissions(GuildPermission.ManageMessages)]
    public class WordCensorModule : BaseDiscordModule
    {
        public override async Task<ModuleResult> ProcessDiscordModule(IDiscordBotContext context)
        {
            // check for word censors
            if (context.Message != null && context.Settings.TriggersCensor(context.Message.Content, out string offendingWord))
            {
                offendingWord = offendingWord != null ? $"`{offendingWord}`" : "*FANCY lanuage filters*";
                await context.Message.DeleteAsync();
                var dmChannel = await context.Message.Author.GetOrCreateDMChannelAsync();
                await dmChannel.SendMessageAsync($"hi uh sorry but your most recent message was tripped up by {offendingWord} and thusly was deleted. complain to management, i'm just the enforcer");
                return ModuleResult.Stop;
            }

            return ModuleResult.Continue;
        }
    }
}
