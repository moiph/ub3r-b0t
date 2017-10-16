namespace UB3RB0T.Commands
{
    using System.Linq;
    using System.Threading.Tasks;
    using Discord.WebSocket;

    public class RolesCommand : IDiscordCommand
    {
        public Task<CommandResponse> Process(IDiscordBotContext context)
        {
            if (context.Message.Channel is SocketGuildChannel guildChannel)
            {
                var settings = SettingsConfig.GetSettings(guildChannel.Guild.Id.ToString());
                var roles = guildChannel.Guild.Roles.Where(r => settings.SelfRoles.Contains(r.Id)).Select(r => $"``{r.Name.Replace("`", @"\`")}``");

                return Task.FromResult(new CommandResponse { Text = "The following roles are available to self-assign: " + string.Join(", ", roles) });
            }

            return Task.FromResult(new CommandResponse { Text = "role command does not work in private channels" });
        }
    }
}
