namespace UB3RB0T.Modules
{
    using System.Collections.Generic;
    using System.Net;
    using System.Threading.Tasks;
    using Discord;
    using Discord.Net;

    [BotPermissions(ChannelPermission.ManageMessages)]
    public class DiscordWordCensorModule : WordCensorModule
    {
        private readonly HashSet<ulong> blockedDMUsers = [];

        protected override async Task DeleteMessage(IBotContext botContext, string message)
        {
            if (botContext is IDiscordBotContext context && context.Message != null)
            {
                var messageDeleted = false;
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
                    var dmChannel = await context.Message.Author.CreateDMChannelAsync();

                    try
                    {
                        await dmChannel.SendMessageAsync(message);
                    }
                    catch (HttpException)
                    {
                        this.blockedDMUsers.Add(context.Message.Author.Id);
                    }
                }
            }
        }
    }
}
