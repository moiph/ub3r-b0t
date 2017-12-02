namespace UB3RB0T
{
    using System;
    using System.Linq;
    using System.Threading.Tasks;
    using Discord;
    using Flurl.Http;
    using Newtonsoft.Json;

    // OCR for fun if requested (patrons only)
    // TODO: need to drive this via config
    // TODO: Need to generalize even further due to more reaction types
    // TODO: oh my god stop writing TODOs and just make the code less awful
    [BotPermissions(ChannelPermission.SendMessages | ChannelPermission.AddReactions)]
    public class OcrModule : BaseDiscordModule
    {
        public override async Task<ModuleResult> ProcessDiscordModule(IDiscordBotContext context)
        {
            var message = context.Message;
            if (!string.IsNullOrEmpty(context.Reaction) || BotConfig.Instance.OcrAutoIds.Contains(message.Channel.Id))
            {
                string newMessageContent = null;

                if (context.Reaction == "💬" || context.Reaction == "🗨️")
                {
                    var quote = context.MessageData.Content.Replace("\"", "&quot;");
                    newMessageContent = $"{context.Settings.Prefix}quote add \"{quote}\" - userid:{message.Author.Id} {message.Author.Username}";
                    await message.AddReactionAsync(new Emoji("💬"));
                }
                else if (string.IsNullOrEmpty(message.Content) || BotConfig.Instance.OcrAutoIds.Contains(message.Channel.Id))
                {
                    var attachmentUrl = message.Attachments?.FirstOrDefault()?.Url;
                    if (attachmentUrl == null)
                    {
                        if (Uri.TryCreate(message.Content, UriKind.Absolute, out Uri attachmentUri))
                        {
                            attachmentUrl = attachmentUri.ToString();
                            if (!attachmentUrl.EndsWith(".jpg") && !attachmentUrl.EndsWith(".png"))
                            {
                                attachmentUrl = null;
                            }
                        }
                    }

                    if (attachmentUrl != null)
                    {
                        if (context.Reaction == "👁" || BotConfig.Instance.OcrAutoIds.Contains(message.Channel.Id))
                        {
                            var result = await BotConfig.Instance.OcrEndpoint.ToString()
                            .WithHeader("Ocp-Apim-Subscription-Key", BotConfig.Instance.VisionKey)
                            .PostJsonAsync(new { url = attachmentUrl });

                            if (result.IsSuccessStatusCode)
                            {
                                var response = await result.Content.ReadAsStringAsync();
                                var ocrData = JsonConvert.DeserializeObject<OcrData>(response);
                                if (!string.IsNullOrEmpty(ocrData.GetText()))
                                {
                                    newMessageContent = ocrData.GetText();

                                    if (newMessageContent.ToLowerInvariant().Contains("unknown guild channel type"))
                                    {
                                        await message.Channel.SendMessageAsync($"update to 1.0.2");
                                    }
                                }
                            }
                        }
                        else if (context.Reaction == "🖼")
                        {
                            var analyzeResult = await BotConfig.Instance.AnalyzeEndpoint.ToString()
                                .WithHeader("Ocp-Apim-Subscription-Key", BotConfig.Instance.VisionKey)
                                .PostJsonAsync(new { url = attachmentUrl });

                            if (analyzeResult.IsSuccessStatusCode)
                            {
                                var response = await analyzeResult.Content.ReadAsStringAsync();
                                var analyzeData = JsonConvert.DeserializeObject<AnalyzeImageData>(response);
                                if (analyzeData.Description.Tags.Contains("ball"))
                                {
                                    newMessageContent = $"{context.Settings.Prefix}8ball foo";
                                }
                                else if (analyzeData.Description.Tags.Contains("outdoor"))
                                {
                                    newMessageContent = $"{context.Settings.Prefix}fw";
                                }
                            }
                        }
                    }
                }

                if (newMessageContent != null)
                {
                    context.MessageData.Content = newMessageContent;
                }
            }

            return ModuleResult.Continue;
        }
    }

}
