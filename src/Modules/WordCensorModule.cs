namespace UB3RB0T.Modules
{
    using System.Collections.Generic;
    using System.Net;
    using System.Threading.Tasks;
    using Discord;
    using Discord.Net;

    [BotPermissions(GuildPermission.ManageMessages)]
    public class WordCensorModule : BaseDiscordModule
    {
        private readonly HashSet<ulong> blockedDMUsers = new HashSet<ulong>();

        public override async Task<ModuleResult> ProcessDiscordModule(IDiscordBotContext context)
        {
            // check for word censors; ignore if we can't delete messages
            var canDeleteMessages = (context.GuildChannel as ITextChannel).GetCurrentUserPermissions().ManageMessages;

            if (context.Message != null && context.Settings.TriggersCensor(context.Message.Content, out string offendingWord) && canDeleteMessages)
            {
                offendingWord = offendingWord != null ? $"`{offendingWord}`" : "*FANCY lanuage filters*";
                bool messageDeleted = false;

                try
                {
                    await context.Message.DeleteAsync();
                    messageDeleted = true;
                }
                catch (HttpException ex) when (ex.HttpCode == HttpStatusCode.NotFound)
                {
                    // ignore if the message isn't found (may have been deleted already)
                }

                if (!this.blockedDMUsers.Contains(context.Message.Author.Id) && messageDeleted)
                {
                    var dmChannel = await context.Message.Author.GetOrCreateDMChannelAsync();
                
                    try
                    {
                        await dmChannel.SendMessageAsync($"hi uh sorry but your most recent message was tripped up by {offendingWord} and thusly was deleted. complain to management, i'm just the enforcer");
                    }
                    catch (HttpException)
                    {
                        this.blockedDMUsers.Add(context.Message.Author.Id);
                    }
                }
                
                return ModuleResult.Stop;
            }

            return ModuleResult.Continue;
        }
    }
}
