namespace UB3RB0T.Commands
{
    using System.Threading.Tasks;
    using System.Text.RegularExpressions;
    using System.Linq;

    [SpecialUserOnly]
    public class FeedbackCommand : IDiscordCommand
    {
        private static Regex FeedbackMessageRx = new Regex("^.*\\[server:([0-9]+) chan:([0-9]+) user:([0-9]+)\\]$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        public async Task<CommandResponse> Process(IDiscordBotContext context)
        {
            var message = context.Message;

            // grab the message being referenced
            var parts = message.Content.Split(new[] { ' ' }, 3);

            if (parts.Length != 3)
            {
                return new CommandResponse { Text = ".fr id message" };
            }

            if (!ulong.TryParse(parts[1], out ulong targetMessageId))
            {
                return new CommandResponse { Text = "invalid message id" };
            }

            var targetMessage = await message.Channel.GetMessageAsync(targetMessageId);

            if (targetMessage == null)
            {
                return new CommandResponse { Text = "message not found" };
            }

            var match = FeedbackMessageRx.Match(targetMessage.Content);

            context.MessageData.Content = $"{context.Settings.Prefix}feedback reply {match.Groups[1]} {match.Groups[2]} {match.Groups[3]} {parts[2]}";
            var response = (await context.BotApi.IssueRequestAsync(context.MessageData)).Responses.FirstOrDefault();

            if (response != null)
            {
                return new CommandResponse { Text = response };
            }

            return null;
        }
    }
}
