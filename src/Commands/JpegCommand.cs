namespace UB3RB0T.Commands
{
    using System;
    using System.Linq;
    using System.Net.Http;
    using System.Threading.Tasks;
    using Discord;

    public class JpegCommand : IDiscordCommand
    {
        public async Task<CommandResponse> Process(IDiscordBotContext context)
        {
            var messageParts = context.Message.Content.Split(new[] { ' ' }, 2);
            var fileName = "moar.jpeg";
            var url = string.Empty;
            if (messageParts.Length == 2 && Uri.IsWellFormedUriString(messageParts[1], UriKind.Absolute))
            {
                url = messageParts[1];
            }
            else
            {
                Attachment img = context.Message.Attachments.FirstOrDefault();
                if (img != null || DiscordBot.imageUrls.TryGetValue(context.Message.Channel.Id.ToString(), out img))
                {
                    url = img.Url;
                    fileName = img.Filename;
                }
            }

            if (!string.IsNullOrEmpty(url))
            {
                using (var httpClient = new HttpClient())
                {
                    var response = await httpClient.GetAsync(CommandsConfig.Instance.JpegEndpoint.AppendQueryParam("url", url));
                    var stream = await response.Content.ReadAsStreamAsync();

                    return new CommandResponse
                    {
                        Attachment = new FileResponse
                        {
                            Name = fileName,
                            Stream = stream,
                        }
                    };
                }
            }

            return null;
        }
    }
}
