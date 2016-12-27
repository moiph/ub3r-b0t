namespace UB3RB0T
{
    using System.Collections.Generic;
    using Newtonsoft.Json;

    public class CommandsConfig : JsonConfig<CommandsConfig>
    {
        protected override string FileName => "commandsconfig.json";

        public HashSet<string> Commands { get; set; }

        public string[] UserInfoSnippets { get; set; }

        public string[] AutoTitleMatches { get; set; }
    }
}
