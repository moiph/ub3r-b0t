namespace UB3RB0T
{
    using System;
    using System.Linq;
    using System.Threading.Tasks;
    using Discord;
    using Flurl.Http;
    using Newtonsoft.Json;
    using Serilog;

    // OCR for fun if requested (patrons only)
    // TODO: need to drive this via config
    // TODO: Need to generalize even further due to more reaction types
    [BotPermissions(ChannelPermission.SendMessages | ChannelPermission.AddReactions)]
    public class OcrModule : BaseDiscordModule
    {
        public override async Task<ModuleResult> ProcessDiscordModule(IDiscordBotContext context)
        {
            var message = context.Message;
            // don't auto process if the message was edited
            if (!string.IsNullOrEmpty(context.Reaction) || (BotConfig.Instance.OcrAutoIds.Contains(message.Channel.Id) && context.Message.EditedTimestamp == null))
            {
                string newMessageContent = null;

                if (context.Reaction == "💬" || context.Reaction == "🗨️")
                {
                    var quote = context.MessageData.Content.ReplaceMulti(new[] { "\"", "”", "“" }, "&quot;");
                    newMessageContent = $"{context.Settings.Prefix}quote add \"{quote}\" - userid:{message.Author.Id} {message.Author.Username}";
                    await message.AddReactionAsync(new Emoji("💬"));
                }
                else if (string.IsNullOrEmpty(message.Content) || BotConfig.Instance.OcrAutoIds.Contains(message.Channel.Id))
                {
                    var imageUrl = context.SocketMessage?.ParseImageUrl();

                    if (imageUrl != null)
                    {
                        if (context.Reaction == "🖼")
                        {
                            var analyzeData = await this.GetVisionData<AnalyzeImageData>(imageUrl);

                            if (analyzeData != null)
                            {
                                var command = CommandsConfig.Instance.CommandPatterns.FirstOrDefault(c => analyzeData.Description.Tags.Contains(c.AnalysisTag));
                                if (command != null)
                                {
                                    newMessageContent = $"{context.Settings.Prefix}{command.Replacement}";
                                }
                            }
                        }
                        else if (context.Reaction == "👁" || BotConfig.Instance.OcrAutoIds.Contains(message.Channel.Id))
                        {
                            Log.Information("Running OCR");
                            string ocrText = null;

                            var result = await BotConfig.Instance.OcrEndpoint.ToString()
                                .WithHeader("Ocp-Apim-Subscription-Key", BotConfig.Instance.VisionKey)
                                .PostJsonAsync(new { url = imageUrl });

                            if (result.IsSuccessStatusCode)
                            {
                                Log.Information("OCR success, waiting on operation");

                                var operationUrl = result.GetHeaderValue("Operation-Location");

                                // The actual process runs as a seprate operation that we need to query.
                                // Unfortunately we just need to poll, so query a few times and give up if it takes too long.
                                RecognitionResultData textData = null;
                                for (var i = 0; i < 5; i++)
                                {
                                    await Task.Delay(3000);
                                    textData = await operationUrl.WithHeader("Ocp-Apim-Subscription-Key", BotConfig.Instance.VisionKey).GetJsonAsync<RecognitionResultData>();

                                    if (textData.Status == "Succeeded")
                                    {
                                        break;
                                    }
                                }

                                ocrText = textData.Status == "Succeeded" ? textData.GetText() : null;

                                if (!string.IsNullOrEmpty(ocrText))
                                {
                                    // Run some external processing and prioritize that over replacing existing content
                                    textData.AuthorName = message.Author.Username;
                                    textData.CommandType = context.Reaction ?? "auto";
     
                                    var response = await new Uri($"{BotConfig.Instance.ApiEndpoint}/ocr").PostJsonAsync(textData);
                                    var ocrProcessResponse = JsonConvert.DeserializeObject<OcrProcessResponse>(await response.Content.ReadAsStringAsync());

                                    if (ocrProcessResponse.Response != null)
                                    {
                                        if (context.Reaction == "👁")
                                        {
                                            await message.AddReactionAsync(new Emoji("👁"));
                                        }

                                        // clear out the processed OCR text and use the API response
                                        await message.Channel.SendMessageAsync(ocrProcessResponse.Response);
                                        ocrText = null;
                                    }
                                }
                            }

                            if (string.IsNullOrEmpty(ocrText))
                            {
                                Log.Information($"Failed to parse OCR text for {imageUrl}");
                            }
                            else
                            {
                                Log.Information("OCR text found, looking for match");
                                newMessageContent = ocrText;
                                var ocrTextLower = ocrText.ToLowerInvariant();

                                var ocrPhrase = PhrasesConfig.Instance.OcrPhrases.FirstOrDefault(o => ocrTextLower.Contains(o.Key)).Value;

                                if (ocrPhrase != null)
                                {
                                    await message.Channel.SendMessageAsync(ocrPhrase);
                                }
                            }
                        }
                    }
                }

                // only update the message content if it was a reaction
                if (newMessageContent != null && !string.IsNullOrEmpty(context.Reaction))
                {
                    context.MessageData.Content = newMessageContent;
                }
            }

            return ModuleResult.Continue;
        }

        /// <summary>
        /// Gets OCR/Analysis data from the given image url
        /// </summary>
        /// <typeparam name="T">The type of data to process (OCR/Analyze)</typeparam>
        /// <param name="imageUrl">The image url to process</param>
        /// <returns>Vision data of type T</returns>
        private async Task<T> GetVisionData<T>(string imageUrl)
        {
            var data = default(T);

            Uri endpoint = BotConfig.Instance.AnalyzeEndpoint;

            var result = await endpoint.ToString()
                .WithHeader("Ocp-Apim-Subscription-Key", BotConfig.Instance.VisionKey)
                .PostJsonAsync(new { url = imageUrl });

            if (result.IsSuccessStatusCode)
            {
                var response = await result.Content.ReadAsStringAsync();
                data = JsonConvert.DeserializeObject<T>(response);
            }

            return data;
        }
    }
}
