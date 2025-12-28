namespace UB3RB0T
{
    using System;
    using System.Collections.Generic;
    using Newtonsoft.Json;

    public class PhrasesConfig : JsonConfig<PhrasesConfig>
    {
        protected override string FileName => "phrasesconfig.json";

        [JsonRequired]
        // These must be exact matches to trigger
        public Dictionary<string, string> ExactPhrases { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // These will do partial matches, provided the bot is mentioned.
        public Dictionary<string, string> PartialMentionPhrases { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        [JsonRequired]
        public Dictionary<string, string[]> Responses { get; set; }

        public Dictionary<string, string> OcrPhrases { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    }
}
