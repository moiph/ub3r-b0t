namespace UB3RB0T
{
    using System.Collections.Generic;
    using System;

    public class CommandsConfig : JsonConfig<CommandsConfig>
    {
        protected override string FileName => "commandsconfig.json";

        public Dictionary<string, string> Commands { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public Uri RemindersEndpoint { get; set; }

        public Uri NotificationsEndpoint { get; set; }

        public Uri PackagesEndpoint { get; set; }

        public string[] UserInfoSnippets { get; set; }

        public string[] AutoTitleMatches { get; set; }
    }
}
