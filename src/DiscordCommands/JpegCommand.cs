namespace UB3RB0T.Commands
{
    using System;
    using System.Linq;
    using System.Threading.Tasks;
    using Discord;
    using Flurl.Http;

    public class JpegCommand : IDiscordCommand
    {
        public async Task<CommandResponse?> Process(IDiscordBotContext context)
        {
            if (context.Message == null)
            {
                return null;
            }

            var messageParts = context.Message.Content.Split([' '], 2);
            var fileName = "moar.jpeg";
            var url = string.Empty;
            if (messageParts.Length == 2 && Uri.IsWellFormedUriString(messageParts[1], UriKind.Absolute))
            {
                url = messageParts[1];
            }
            else
            {
                Attachment? img = context.SocketMessage?.Attachments.FirstOrDefault();
                if (img != null || context.Bot?.ImageUrls.TryGetValue(context.Message.Channel.Id.ToString(), out img) == true)
                {
                    url = img.Url;
                    fileName = img.Filename;
                }
            }

            if (!string.IsNullOrEmpty(url) && CommandsConfig.Instance.JpegEndpoint != null)
            {
                var stream = await CommandsConfig.Instance.JpegEndpoint.AppendQueryParam("url", url).GetStreamAsync();

                return new CommandResponse
                {
                    Attachment = new FileResponse
                    {
                        Name = fileName,
                        Stream = stream,
                    }
                };
            }

            return null;
        }
    }
}
