using System.Collections.Generic;

namespace UB3RB0T
{
    public class BotResponseData
    {
        public List<string> Responses { get; set; } = new List<string>();
        public EmbedData Embed;
        public string AttachmentUrl;
        public bool AllowMentions { get; set; }
    }
}
