using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Fluxer.Net;

namespace UB3RB0T.Modules.FluxerModules
{
    [FluxerBotPermissions(ChannelPermission.ManageMessages)]
    internal class FluxerWordCensorModule : WordCensorModule
    {
        private readonly HashSet<ulong> blockedDMUsers = [];

        protected override async Task DeleteMessage(IBotContext botContext, string responseMessage)
        {
            if (botContext is IFluxerBotContext context && context.MessageData.FluxerMessageData is SocketMessage message)
            {
                var messageDeleted = false;
                try
                {
                    await message.DeleteAsync();
                    messageDeleted = true;
                }
                catch (Exception)
                {
                    // ignore if the message isn't found (may have been deleted already)
                }

                if (message.Author != null && !this.blockedDMUsers.Contains(message.Author.Id) && messageDeleted)
                {
                    var dmChannel = await message.Author.GetOrCreateDMChannelAsync();

                    try
                    {
                        await dmChannel.SendMessageAsync(responseMessage);
                    }
                    catch (Exception)
                    {
                        this.blockedDMUsers.Add(message.Author.Id);
                    }
                }
            }
        }
    }
}
