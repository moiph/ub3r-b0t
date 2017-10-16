namespace UB3RB0T.Commands
{
    using System.Linq;
    using System.Threading.Tasks;
    using Discord;

    public class SeenCommand : IDiscordCommand
    {
        public async Task<CommandResponse> Process(IDiscordBotContext context)
        {
            if (context.Message.Channel is IGuildChannel guildChannel)
            {
                var settings = SettingsConfig.GetSettings(guildChannel.GuildId.ToString());
                if (!settings.SeenEnabled)
                {
                    return new CommandResponse { Text = "Seen data is not being tracked for this server.  Enable it in the admin settings panel." };
                }

                string[] parts = context.Message.Content.Split(new[] { ' ' }, 2);
                if (parts.Length != 2)
                {
                    return new CommandResponse { Text = "Usage: .seen username" };
                }

                var targetUser = (await guildChannel.Guild.GetUsersAsync()).Find(parts[1]).FirstOrDefault();
                if (targetUser != null)
                {
                    if (targetUser.Id == context.Client.CurrentUser.Id)
                    {
                        return new CommandResponse { Text = $"I was last seen...wait...seriously? Ain't no one got time for your shit, {context.Message.Author.Username}." };
                    }

                    if (targetUser.Id == context.Message.Author.Id)
                    {
                        return new CommandResponse { Text = $"You were last seen now, saying: ... god DAMN it {context.Message.Author.Username}, quit wasting my time" };
                    }

                    context.MessageData.Content = $"{settings.Prefix}seen {targetUser.Id} {targetUser.Username}";

                    var response = (await context.BotApi.IssueRequestAsync(context.MessageData)).Responses.FirstOrDefault();

                    if (response != null)
                    {
                        return new CommandResponse { Text = response };
                    }
                }

                return new CommandResponse { Text = $"I...omg...I have not seen {parts[1]} in this channel :X I AM SOOOOOO SORRY" };
            }

            return null;
        }
    }
}
