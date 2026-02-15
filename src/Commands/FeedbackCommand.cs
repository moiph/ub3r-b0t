namespace UB3RB0T.Commands
{
    using System.Threading.Tasks;
    using System.Text.RegularExpressions;
    using System.Linq;

    [SpecialUserOnly]
    public class FeedbackCommand : IDiscordCommand
    {
        private static Regex FeedbackMessageRx = new Regex("^.*\\[server:([0-9]+) chan:([0-9]+) user:([0-9]+) type:([A-Z]+)\\] \\(mid: [0-9]+\\)$", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Multiline);
        private static Regex FeedbackEmbedRx = new Regex("^S: ([A-Z0-9]+) \\| C: ([A-Z0-9]+) \\| U: ([A-Z0-9]+) \\| T: ([A-Z]+)$", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Multiline);

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

            var targetMessage = await context.Channel.GetMessageAsync(targetMessageId);

            if (targetMessage == null)
            {
                return new CommandResponse { Text = "message not found" };
            }

            Match match;
            var targetEmbed = targetMessage.Embeds?.FirstOrDefault()?.Footer?.Text;
            if (!string.IsNullOrEmpty(targetEmbed))
            {
                match = FeedbackEmbedRx.Match(targetEmbed);
            }
            else
            {
                match = FeedbackMessageRx.Match(targetMessage.Content);
            }

            if (!match.Success)
            {
                return new CommandResponse { Text = "failed to find match" };
            }

            context.MessageData.Content = $"{context.Settings.Prefix}feedback reply {match.Groups[1]} {match.Groups[2]} {match.Groups[3]} {match.Groups[4]} {parts[2]}";
            var response = (await context.BotApi.IssueRequestAsync(context.MessageData)).Responses.FirstOrDefault();

            if (response != null)
            {
                return new CommandResponse { Text = response };
            }

            return null;
        }
    }
}
