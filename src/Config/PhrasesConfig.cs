namespace UB3RB0T
{
    using System.Collections.Generic;
    using Newtonsoft.Json;

    public class PhrasesConfig : JsonConfig<PhrasesConfig>
    {
        protected override string FileName => "phrasesconfig.json";

        [JsonRequired]
        public Dictionary<string, string> Phrases { get; set; }

        [JsonRequired]
        public Dictionary<string, string[]> Responses { get; set; }
    }
}
