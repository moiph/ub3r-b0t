namespace UB3RB0T
{
    using System;
    using System.Threading.Tasks;
    using Discord;
    using Flurl.Http;
    using Newtonsoft.Json;

    [BotPermissions(ChannelPermission.SendMessages)]
    public class FaqModule : BaseDiscordModule
    {
        public override async Task<ModuleResult> ProcessDiscordModule(IDiscordBotContext context)
        {
            // special case FAQ channel
            var message = context.Message;
            if (message.Channel.Id == BotConfig.Instance.FaqChannel && BotConfig.Instance.FaqEndpoint != null && (message.Content.EndsWith("?") || context.Reaction == "❓"))
            {
                await message.AddReactionAsync(new Emoji("❓"));

                string content = message.Content.Replace("<@85614143951892480>", "ub3r-b0t");
                var result = await BotConfig.Instance.FaqEndpoint.ToString().WithHeader("Authorization", BotConfig.Instance.FaqKey).PostJsonAsync(new { question = content });
                if (result.IsSuccessStatusCode)
                {
                    var response = await result.Content.ReadAsStringAsync();
                    var qnaData = JsonConvert.DeserializeObject<QnAMakerData>(response).Answers[0];
                    var score = Math.Floor(qnaData.Score);
                    var answer = System.Net.WebUtility.HtmlDecode(qnaData.Answer);
                    await message.Channel.SendMessageAsync($"{answer} ({score}% match)");
                }
                else
                {
                    await message.Channel.SendMessageAsync("An error occurred while fetching data");
                }

                return ModuleResult.Stop;
            }

            return ModuleResult.Continue;
        }
    }
}
