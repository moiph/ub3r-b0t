using System.Threading.Tasks;
using System.Linq;
using Fluxer.Net;

namespace UB3RB0T.Modules.FluxerModules
{
    internal class BotlessModule : IModule
    {
        private readonly string BotlessRole = "botless";

        public async Task<ModuleResult> Process(IBotContext botContext)
        {
            if (botContext is IFluxerBotContext context && context.MessageData.FluxerMessageData is SocketMessage message && message.Guild != null)
            {
                var botlessRoleId = message.Guild.Roles?.FirstOrDefault(r => r.Value.Name?.ToLowerInvariant() == BotlessRole).Key;
                if (botlessRoleId != null)
                {
                    var targetUser = (context.ReactionUser ?? context.Author);
                    if (targetUser != null)
                    {
                        var targetMember = await message.Guild.GetMemberAsync(targetUser.Id);
                        if (targetMember?.RoleIds.Contains(botlessRoleId.Value) ?? false)
                        {
                            return ModuleResult.Stop;
                        }
                    }
                }
            }

            return ModuleResult.Continue;
        }
    }
}
