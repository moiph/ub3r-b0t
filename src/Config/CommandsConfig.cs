namespace UB3RB0T
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text.RegularExpressions;

    public class CommandsConfig : JsonConfig<CommandsConfig>
    {
        protected override string FileName => "commandsconfig.json";

        public Dictionary<string, string> Commands { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public Uri JpegEndpoint { get; set; }

        public int CommandLengthLimit { get; set; }

        public string HelperKey { get; set; }

        public string[] UserInfoSnippets { get; set; }

        public string[] AutoTitleMatches { get; set; }

        public List<CommandPattern> CommandPatterns { get; set; }

        public bool TryParseForCommand(string text, bool mentionsBot, out string command, out string query)
        {
            query = string.Empty;
            command = string.Empty;

            // if the text is greater than a configurable number of characters, just bail out. not a valid command to parse.
            if (text.Length > this.CommandLengthLimit)
            {
                return false;
            }

            CommandPattern commandPattern = null;
            try
            {
                commandPattern = this.CommandPatterns.FirstOrDefault(t => t.Regex.Value.IsMatch(text));
            }
            catch (RegexMatchTimeoutException)
            {
                // ignore
            }

            if (commandPattern != null && (!commandPattern.RequiresMention || mentionsBot))
            {
                query = commandPattern.Regex.Value.Replace(text, commandPattern.Replacement);
                command = commandPattern.Command;
                return true;
            }

            return false;
        }
    }

    public class CommandPattern
    {
        public Lazy<Regex> Regex { get; }

        public string Pattern { get; set; }
        public string Replacement { get; set; }
        public string Command { get; set; }
        /// <summary>
        /// Set to true if the match only applies if the bot's name is directly mentioned.
        /// </summary>
        public bool RequiresMention { get; set; }

        /// <summary>
        /// Tag to match for OcrModule
        /// </summary>
        public string AnalysisTag { get; set; }

        public CommandPattern()
        {
            this.Regex = new Lazy<Regex>(() => new Regex(this.Pattern, RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(100)));
        }
    }
}
