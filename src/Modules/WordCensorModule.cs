namespace UB3RB0T
{
    using System.Collections.Generic;
    using System.Threading.Tasks;
    using Discord;
    using Discord.Net;

    [BotPermissions(GuildPermission.ManageMessages)]
    public class WordCensorModule : BaseDiscordModule
    {
        private HashSet<ulong> blockedDMUsers = new HashSet<ulong>();

        public override async Task<ModuleResult> ProcessDiscordModule(IDiscordBotContext context)
        {
            // check for word censors
            if (context.Message != null && context.Settings.TriggersCensor(context.Message.Content, out string offendingWord))
            {
                offendingWord = offendingWord != null ? $"`{offendingWord}`" : "*FANCY lanuage filters*";
                await context.Message.DeleteAsync();

                if (!this.blockedDMUsers.Contains(context.Message.Author.Id))
                {
                    var dmChannel = await context.Message.Author.GetOrCreateDMChannelAsync();
                
                    try
                    {
                        await dmChannel.SendMessageAsync($"hi uh sorry but your most recent message was tripped up by {offendingWord} and thusly was deleted. complain to management, i'm just the enforcer");
                    }
                    catch (HttpException)
                    {
                    }
                }
                
                return ModuleResult.Stop;
            }

            return ModuleResult.Continue;
        }
    }
}
