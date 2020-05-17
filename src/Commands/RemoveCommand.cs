namespace UB3RB0T.Commands
{
    using System.Linq;
    using System.Threading.Tasks;

    [GuildOwnerOnly("RequireUserGuildOwner")]
    public class RemoveCommand : IDiscordCommand
    {
        public async Task<CommandResponse> Process(IDiscordBotContext context)
        {
            if (context.GuildChannel != null)
            {
                var settings = SettingsConfig.GetSettings(context.GuildChannel.Guild.Id.ToString());

                string[] parts = context.Message.Content.Split(new[] { ' ' }, 3);

                if (parts.Length != 3)
                {
                    return new CommandResponse { Text = "Usage: .remove type #; valid types are timer, wc, user, or bday" };
                }

                var type = parts[1].ToLowerInvariant();
                var id = parts[2];

                context.MessageData.Content = $"{settings.Prefix}remove {CommandsConfig.Instance.HelperKey} {type} {id}";
                var response = (await context.BotApi.IssueRequestAsync(context.MessageData)).Responses.FirstOrDefault();

                if (response != null)
                {
                    return new CommandResponse { Text = response };
                }
            }

            return null;
        }
    }
}
