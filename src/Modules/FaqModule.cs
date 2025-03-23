namespace UB3RB0T.Modules
{
    using System;
    using System.Collections.Generic;
    using System.Net;
    using System.Threading.Tasks;
    using Discord;
    using Discord.WebSocket;
    using Flurl.Http;
    using Newtonsoft.Json;
    using Serilog;

    [BotPermissions(ChannelPermission.SendMessages)]
    public class FaqModule : BaseDiscordModule
    {
        public override async Task<ModuleResult> ProcessDiscordModule(IDiscordBotContext context)
        {
            // special case FAQ channel
            var message = context.Message;
            var messageCommand = context.Interaction as SocketMessageCommand;
            if (message != null && BotConfig.Instance.FaqEndpoints != null && BotConfig.Instance.FaqEndpoints.TryGetValue(message.Channel.Id, out var faq) &&
                faq.Endpoint != null && (context.Reaction == faq.Reaction ||  messageCommand != null && messageCommand.CommandName.IEquals(faq.Command) || string.IsNullOrEmpty(context.Reaction) && !string.IsNullOrEmpty(faq.EndsWith) && message.Content.EndsWith(faq.EndsWith)))
            {
                SocketThreadChannel thread = null;
                if (messageCommand != null)
                {
                    await messageCommand.DeferAsync();
                }
                else if (BotConfig.Instance.FaqThreadNames?.Length > 0 && faq.Type == "bot")
                {
                    var threadname = BotConfig.Instance.FaqThreadNames.Random();
                    thread = await (message.Channel as SocketTextChannel).CreateThreadAsync($"{threadname} question from {message.Author} ", message: message);
                    await thread.SendMessageAsync("Someone will be with you shortly. Or longly. There's no real SLA here. Sorry. I'll try to figure out an automated answer for you but my brains are only as big as my ego. Which is huge. Also if someone helps you in here, please react to their helpful message with a 🏅 emoji. It'll give them warm fuzzies.");
                }

                await message.AddReactionAsync(new Emoji(faq.Reaction));

                string content = message.Content.Replace("<@85614143951892480>", "ub3r-b0t");
                IFlurlResponse result = null;
                try
                {
                    result = await faq.Endpoint.ToString().WithHeader("Ocp-Apim-Subscription-Key", BotConfig.Instance.FaqKey).PostJsonAsync(new { question = content, top = 2 });
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Failure in FAQ module");
                }

                if (result != null && result.IsSuccessStatusCode())
                {
                    var response = await result.GetStringAsync();
                    var qnaData = JsonConvert.DeserializeObject<QnAMakerData>(response);

                    var responses = new List<string>();
                    foreach (var answer in qnaData.Answers)
                    {
                        var score = Math.Floor(answer.Score * 100);
                        var answerText = WebUtility.HtmlDecode(answer.Answer);
                        responses.Add($"{answerText} ({score}% match)");
                    }

                    var messageResponse = string.Join("\n\n", responses);
                    if (messageCommand != null)
                    {
                        await messageCommand.FollowupAsync(messageResponse);
                    }
                    else
                    {
                        if (thread != null)
                        {
                            await thread.SendMessageAsync(messageResponse);
                        }
                        else
                        {
                            await message.Channel.SendMessageAsync(messageResponse);
                        }
                    }
                }
                else
                {
                    if (messageCommand != null)
                    {
                        await messageCommand.FollowupAsync("An error occurred while fetching data");
                    }
                    else
                    {
                        await message.Channel.SendMessageAsync("An error occurred while fetching data");
                    }
                }

                return ModuleResult.Stop;
            }

            return ModuleResult.Continue;
        }
    }
}
