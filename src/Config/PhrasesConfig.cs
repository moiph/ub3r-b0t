namespace UB3RB0T
{
    using System.Collections.Generic;
    using Newtonsoft.Json;

    public class PhrasesConfig : JsonConfig<PhrasesConfig>
    {
        protected override string FileName => "phrasesconfig.json";

        [JsonRequired]
        // These must be exact matches to trigger
        public Dictionary<string, string> ExactPhrases { get; set; }

        // These will do partial matches, provided the bot is mentioned.
        public Dictionary<string, string> PartialMentionPhrases { get; set; }

        [JsonRequired]
        public Dictionary<string, string[]> Responses { get; set; }

        /// Settings for voice support
        public string VoiceFilePath { get; set; }
        public string[] VoiceGreetingFileNames { get; set; }
        public string[] VoiceFarewellFileNames { get; set; }
    }
}
