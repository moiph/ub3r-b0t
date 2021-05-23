namespace UB3RB0T.Modules
{
    using System;
    using System.Collections.Generic;
    using System.Net;
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
            if (BotConfig.Instance.FaqEndpoints != null && BotConfig.Instance.FaqEndpoints.TryGetValue(message.Channel.Id, out var faq) &&
                faq.Endpoint != null && (context.Reaction == faq.Reaction || string.IsNullOrEmpty(context.Reaction) && !string.IsNullOrEmpty(faq.EndsWith) && message.Content.EndsWith(faq.EndsWith)))
            {
                await message.AddReactionAsync(new Emoji(faq.Reaction));

                string content = message.Content.Replace("<@85614143951892480>", "ub3r-b0t");
                var result = await faq.Endpoint.ToString().WithHeader("Authorization", BotConfig.Instance.FaqKey).PostJsonAsync(new { question = content, top = 2 });
                if (result.IsSuccessStatusCode)
                {
                    var response = await result.Content.ReadAsStringAsync();
                    var qnaData = JsonConvert.DeserializeObject<QnAMakerData>(response);

                    var responses = new List<string>();
                    foreach (var answer in qnaData.Answers)
                    {
                        var score = Math.Floor(answer.Score);
                        var answerText = WebUtility.HtmlDecode(answer.Answer);
                        responses.Add($"{answerText} ({score}% match)");
                    }

                    await message.Channel.SendMessageAsync(string.Join("\n\n", responses));
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
