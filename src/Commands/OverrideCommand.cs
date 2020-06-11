namespace UB3RB0T.Commands
{
    using Discord.WebSocket;
    using System.Linq;
    using System.Threading.Tasks;

    [GuildOwnerOnly("RequireUserGuildOwner")]
    public class OverrideCommand : IDiscordCommand
    {
        public async Task<CommandResponse> Process(IDiscordBotContext context)
        {
            if (context.GuildChannel != null)
            {
                var settings = SettingsConfig.GetSettings(context.GuildChannel.Guild.Id.ToString());

                string[] parts = context.Message.Content.Split(new[] { ' ' }, 4);

                if (parts.Length < 4)
                {
                    return new CommandResponse { Text = "Usage: .override user command"};
                }

                var overrideCommand = parts[2].ToLowerInvariant();
                if (overrideCommand != "bday")
                {
                    return new CommandResponse { Text = "Only `bday` is supported for overrides right now" };
                }

                SocketUser targetUser = context.Message.MentionedUsers?.FirstOrDefault();

                if (targetUser == null)
                {
                    targetUser = context.GuildChannel.Guild.Users.Find(parts[1]).FirstOrDefault() as SocketUser;
                }

                if (targetUser == null)
                {
                    return new CommandResponse { Text = "User not found. Try a direct mention." };
                }

                if (targetUser != null)
                {
                    if (targetUser.Id == context.Client.CurrentUser.Id)
                    {
                        return new CommandResponse { Text = $"How DARE you try to override my own settings? goodness gracious" };
                    }

                    if (targetUser.Id == context.Message.Author.Id)
                    {
                        return new CommandResponse { Text = $"Don't override your own settings, just do it normally ffs" };
                    }
                }

                context.MessageData.Content = $"{settings.Prefix}override {CommandsConfig.Instance.HelperKey} {targetUser.Id} {overrideCommand} {parts[3]}";
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
