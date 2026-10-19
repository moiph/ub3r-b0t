namespace UB3RB0T.Commands
{
    using System.Threading.Tasks;
    using Discord;

    [UserPermissions(GuildPermission.ManageGuild, "RequireUserManageGuild")]
    public class DiscordAdminCommand : AdminCommand
    {
        public override BotType botType => BotType.Discord;

        public async Task<CommandResponse?> Process(IDiscordBotContext context)
        {
            return await base.Process(context);
        }
    }
}
