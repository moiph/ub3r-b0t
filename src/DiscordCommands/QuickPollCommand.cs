namespace UB3RB0T.Commands
{
    using System.Text.RegularExpressions;
    using System.Threading.Tasks;
    using Discord;

    [BotPermissions(ChannelPermission.AddReactions, "RequireReactionAdd")]
    public class QuickPollCommand : IDiscordCommand
    {
        private readonly Regex optionsRx = new Regex("--options? #?(?<count>[0-9]+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        public async Task<CommandResponse> Process(IDiscordBotContext context)
        {
            var guildChannel = context.GuildChannel;
            if (guildChannel == null)
            {
                return null;
            }

            var parts = context.Message.Content.Split(new[] { ' ' }, 2);
            if (parts.Length == 1)
            {
                return new CommandResponse { Text = "Ask a question for your poll, jeez... (add --options # to use a numbered list instead of yes/no)" };
            }

            var currentPermissions = (guildChannel as ITextChannel).GetCurrentUserPermissions();

            if (!currentPermissions.AddReactions)
            {
                return new CommandResponse { Text = "oy barfbag I don't have permissions to add reactions in here." };
            }

            var match = optionsRx.Match(parts[1]);
            if (match.Success && int.TryParse(match.Groups["count"].Value, out int optionCount))
            {
                if (optionCount > 10)
                {
                    return new CommandResponse { Text = "sorry i can only count to 10. math is HARD especially for computers ok?" };
                }

                for (var i = 0; i < optionCount; i++)
                {
                    var codepoint = i < 9 ? $"{(char)(49 + i)}\U000020e3" : "\U0001f51f";
                    await context.Message.AddReactionAsync(new Emoji($"{codepoint}"));
                }
            }
            else if (currentPermissions.UseExternalEmojis)
            {
                await context.Message.AddReactionAsync(Emote.Parse("<:check:363764608969867274>"));
                await context.Message.AddReactionAsync(Emote.Parse("<:xmark:363764632160043008>"));
            }
            else
            {
                await context.Message.AddReactionAsync(new Emoji("✅"));
                await context.Message.AddReactionAsync(new Emoji("❌"));
            }

            return null;
        }
    }
}
