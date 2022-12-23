namespace UB3RB0T.Modules
{
    using System;
    using System.IO;
    using System.Linq;
    using System.Threading.Tasks;
    using Discord;
    using Discord.WebSocket;
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
            var messageCommand = context.Interaction as SocketMessageCommand;
            var isQuote = messageCommand != null && messageCommand.CommandName.IEquals("quote");
            // don't auto process if the message was edited
            if (message != null && (!string.IsNullOrEmpty(context.Reaction) || isQuote || (BotConfig.Instance.OcrAutoIds.Contains(message.Channel.Id) && context.Message.EditedTimestamp == null)))
            {
                string newMessageContent = null;

                if (context.Reaction == "💬" || context.Reaction == "🗨️" || isQuote)
                {
                    var quote = context.MessageData.Content.ReplaceMulti(new[] { "\"", "”", "“" }, "&quot;");
                    newMessageContent = $"{context.Settings.Prefix}quote add \"{quote}\" - userid:{message.Author.Id} {message.Author.Username}";
                    await message.AddReactionAsync(new Emoji("💬"));
                }
                else if (context.Reaction == "🏅")
                {
                    newMessageContent = $"{context.Settings.Prefix}rep add {message.Author.Id}";
                    context.MessageData.UserId = context.ReactionUser.Id.ToString();
                    context.MessageData.TargetUserId = message.Author.Id.ToString();
                }
                else if (string.IsNullOrEmpty(message.Content) || BotConfig.Instance.OcrAutoIds.Contains(message.Channel.Id))
                {
                    var imageUrl = context.SocketMessage?.ParseImageUrl();

                    if (imageUrl != null)
                    {
                        if (context.Reaction == "👁" || BotConfig.Instance.OcrAutoIds.Contains(message.Channel.Id))
                        {
                            IUserMessage sentMessage = null;
                            if (BotConfig.Instance.OcrAutoRespondIds.Contains(message.Channel.Id))
                            {
                                sentMessage = await message.Channel.SendMessageAsync("Processing...");
                            }

                            Log.Information("Running OCR");
                            string ocrText = null;

                            var result = await BotConfig.Instance.OcrEndpoint.ToString()
                                .WithHeader("Ocp-Apim-Subscription-Key", BotConfig.Instance.VisionKey)
                                .PostJsonAsync(new { url = imageUrl });

                            if (result.IsSuccessStatusCode())
                            {
                                Log.Information("OCR success, waiting on operation");

                                var operationUrl = result.Headers.FirstOrDefault("Operation-Location");

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
                                    textData.AuthorId = message.Author.Id;
                                    textData.ChannelId = message.Channel.Id;
                                    textData.CommandType = context.Reaction ?? "auto";
                                    textData.MessageText = message.Content;
     
                                    var response = await new Uri($"{BotConfig.Instance.ApiEndpoint}/ocr").PostJsonAsync(textData);
                                    var ocrProcessResponse = JsonConvert.DeserializeObject<OcrProcessResponse>(await response.GetStringAsync());

                                    if (!string.IsNullOrEmpty(ocrProcessResponse.Response))
                                    {
                                        if (context.Reaction == "👁")
                                        {
                                            await message.AddReactionAsync(new Emoji("👁"));
                                        }

                                        // clear out the processed OCR text and use the API response
                                        if (Uri.TryCreate(ocrProcessResponse.AttachmentUrl, UriKind.Absolute, out var attachmentUri))
                                        {
                                            Stream fileStream = await attachmentUri.GetStreamAsync();
                                            if (sentMessage != null)
                                            {
                                                _ = sentMessage.DeleteAsync();
                                            }
                                            await message.Channel.SendFileAsync(fileStream, "stash.txt", ocrProcessResponse.Response);
                                        }
                                        else
                                        {
                                            if (sentMessage != null)
                                            {
                                                await sentMessage.ModifyAsync(m => m.Content = ocrProcessResponse.Response);
                                            }
                                            else
                                            {
                                                await message.Channel.SendMessageAsync(ocrProcessResponse.Response);
                                            }
                                        }
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
                if (newMessageContent != null && (!string.IsNullOrEmpty(context.Reaction) || isQuote))
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

            if (result.IsSuccessStatusCode())
            {
                var response = await result.GetStringAsync();
                data = JsonConvert.DeserializeObject<T>(response);
            }

            return data;
        }
    }
}
