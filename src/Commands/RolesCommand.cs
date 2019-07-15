namespace UB3RB0T.Commands
{
    using System.Collections.Generic;
    using System.Linq;
    using System.Text;
    using System.Threading.Tasks;
    using Discord.WebSocket;

    public class RolesCommand : IDiscordCommand
    {
        public Task<CommandResponse> Process(IDiscordBotContext context)
        {
            if (context.Message.Channel is SocketGuildChannel guildChannel)
            {
                var settings = SettingsConfig.GetSettings(guildChannel.Guild.Id.ToString());
                var roles = guildChannel.Guild.Roles.Where(r => settings.SelfRoles.Contains(r.Id)).Select(r => $"``{r.Name.Replace("`", @"\`")}``").OrderBy(r => r);

                var multiText = new List<string>();
                var sb = new StringBuilder();
                sb.Append("The following roles are available to self-assign: ");

                foreach (var role in roles)
                {
                    if (sb.Length + role.Length + 2 < Discord.DiscordConfig.MaxMessageSize)
                    {
                        sb.Append($"{role}, ");
                    }
                    else
                    {
                        multiText.Add(sb.ToString());
                        sb.Clear();
                        sb.Append($"{role}, ");
                    }
                }

                multiText.Add(sb.ToString());

                return Task.FromResult(new CommandResponse { MultiText = multiText });
            }

            return Task.FromResult(new CommandResponse { Text = "role command does not work in private channels" });
        }
    }
}
