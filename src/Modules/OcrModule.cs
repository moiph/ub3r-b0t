namespace UB3RB0T
{
    using System;
    using System.Linq;
    using System.Threading.Tasks;
    using Discord;
    using Discord.WebSocket;
    using Flurl.Http;
    using Newtonsoft.Json;

    // OCR for fun if requested (patrons only)
    // TODO: need to drive this via config
    // TODO: Need to generalize even further due to more reaction types
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
                    var imageUrl = this.ParseImageUrl(context.Message);

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
                            var ocrData = await this.GetVisionData<OcrData>(imageUrl);
                            var ocrText = ocrData?.GetText();

                            if (!string.IsNullOrEmpty(ocrText))
                            {
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

                if (newMessageContent != null)
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
            Uri endpoint = typeof(T) == typeof(OcrData) ? BotConfig.Instance.OcrEndpoint : BotConfig.Instance.AnalyzeEndpoint;

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

        /// <summary>
        /// Grabs the attachment URL from the message attachment, if present, else tries to parse an image URL out of the message contents.
        /// </summary>
        /// <param name="message">The message to parse</param>
        /// <returns>Image URL</returns>
        private string ParseImageUrl(SocketUserMessage message)
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

            return attachmentUrl;
        }
    }
}
