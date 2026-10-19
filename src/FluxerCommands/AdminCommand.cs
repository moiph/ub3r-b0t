namespace UB3RB0T.Commands
{
    using Fluxer.Net;
    using System.Threading.Tasks;

    [FluxerUserPermissions(GuildPermission.ManageGuild, "RequireUserManageGuild")]
    public class FluxerAdminCommand : AdminCommand, IFluxerCommand
    {
        public override BotType botType => BotType.Fluxer;

        public async Task<CommandResponse?> Process(IFluxerBotContext context)
        {
            return await base.Process(context);
        }
    }
}
