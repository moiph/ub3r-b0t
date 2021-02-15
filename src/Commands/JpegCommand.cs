namespace UB3RB0T.Commands
{
    using System;
    using System.Linq;
    using System.Threading.Tasks;
    using Discord;
    using Flurl.Http;

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
                Attachment img = context.SocketMessage?.Attachments.FirstOrDefault();
                if (img != null || context.Bot.ImageUrls.TryGetValue(context.Message.Channel.Id.ToString(), out img))
                {
                    url = img.Url;
                    fileName = img.Filename;
                }
            }

            if (!string.IsNullOrEmpty(url))
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
