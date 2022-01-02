namespace UB3RB0T.Commands
{
    using System.Collections.Generic;
    using System.Linq;
    using System.Text;
    using System.Threading.Tasks;
    using Discord;
    using Discord.WebSocket;

    public class RolesCommand : IDiscordCommand
    {
        public async Task<CommandResponse> Process(IDiscordBotContext context)
        {
            if (context.Interaction is SocketSlashCommand slashCommand)
            {
                var settings = SettingsConfig.GetSettings(context.GuildChannel.Guild.Id);

                if (settings.SelfRoles.Count > 0)
                {
                    var roles = context.GuildChannel.Guild.Roles.OrderByDescending(r => r.Position).Where(r => settings.SelfRoles.ContainsKey(r.Id));
                    var roleCount = roles.Count();
                    if (roleCount > 0)
                    {
                        var menuBuilder = new SelectMenuBuilder()
                            .WithPlaceholder("Select a role")
                            .WithCustomId("role")
                            .WithMinValues(0)
                            .WithMaxValues(roleCount);

                        foreach (var role in roles)
                        {
                            menuBuilder.AddOption(role.Name, role.Id.ToString(), null, string.IsNullOrEmpty(role.Emoji.Name) ? null : role.Emoji);
                        }
                        
                        if (roleCount == 1)
                        {
                            menuBuilder.AddOption("[clear role]", "clear");
                        }

                        var component = new ComponentBuilder().WithSelectMenu(menuBuilder).Build();
                        await slashCommand.RespondAsync("choose a role", components: component, ephemeral: true);
                    }
                }
                else
                {
                    await slashCommand.RespondAsync("No roles are self assignable", ephemeral: true);
                }

                return new CommandResponse { IsHandled = true };
            }

            if (context.Message.Channel is SocketGuildChannel guildChannel)
            {
                var settings = SettingsConfig.GetSettings(guildChannel.Guild.Id.ToString());
                var roles = guildChannel.Guild.Roles.OrderByDescending(r => r.Position).Where(r => settings.SelfRoles.ContainsKey(r.Id)).Select(r => $"``{r.Name.Replace("`", @"\`")}``");

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

                return new CommandResponse { MultiText = multiText };
            }

            return new CommandResponse { Text = "role command does not work in private channels" };
        }
    }
}
