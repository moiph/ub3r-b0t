namespace UB3RB0T.Commands
{
    using System.Linq;
    using System.Threading.Tasks;
    using Discord;
    using Discord.WebSocket;

    public class SeenCommand : IDiscordCommand
    {
        public async Task<CommandResponse> Process(IDiscordBotContext context)
        {
            if (context.Message.Channel is SocketGuildChannel guildChannel)
            {
                if (!context.Settings.SeenEnabled)
                {
                    return new CommandResponse { Text = "Seen data is not being tracked for this server.  Enable it in the admin settings panel." };
                }

                string[] parts = context.Message.Content.Split(new[] { ' ' }, 2);
                if (parts.Length != 2)
                {
                    return new CommandResponse { Text = $"Usage: {context.Settings.Prefix}seen username" };
                }

                IUser targetUser = context.SocketMessage?.MentionedUsers?.FirstOrDefault();

                if (targetUser == null)
                {
                    targetUser = guildChannel.Guild.Users.Find(parts[1]).FirstOrDefault();
                }

                if (targetUser == null)
                {
                    targetUser = (await guildChannel.Guild.SearchUsersAsync(parts[1], limit: 1)).FirstOrDefault();
                }

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

                    // check local store
                    var seenData = context.Bot.GetSeen($"{guildChannel.Guild.Id}{targetUser.Id}");
                    if (seenData != null)
                    {
                        var foundResponse = $"{parts[1]} was JUST here moments ago...";
                        if (context.Message.Channel.Id.ToString() != seenData.Channel)
                        {
                            foundResponse += " (okay well in a different channel)";
                        }

                        return new CommandResponse { Text = foundResponse };
                    }

                    context.MessageData.Content = $"{context.Settings.Prefix}seen {targetUser.Id} {targetUser.Username}";

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
