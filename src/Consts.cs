
namespace UB3RB0T
{
    using System.Text.RegularExpressions;

    public static class Consts
    {
        public const int MaxMessageLength = 2000;

        public static readonly Regex UrlRegex = new Regex("(https?://[^ ]+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        public static readonly Regex ChannelRegex = new Regex("#([a-zA-Z0-9_\\-]+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        public static readonly Regex HttpRegex = new Regex("(?(<http)|https?://([^\\s>]+))", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        public static readonly Regex RedditRegex = new Regex("(^| )/?r/[^ ]+");
        public static readonly Regex TimerRegex = new Regex(".*?remind (?<target>.+?) in (?<rand>a while)? ?(?<years>[0-9]+ year)?s? ?(?<weeks>([0-9]+|a) week)?s? ?(?<days>([0-9]+|a) day)?s? ?(?<hours>([0-9]+|an) hour)?s? ?(?<minutes>[0-9]+ minute)?s? ?(?<seconds>[0-9]+ seconds)?.*?(?<prep>[^ ]+) (?<reason>.+)", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Singleline);
        public static readonly Regex Timer2Regex = new Regex(".*?remind (?<target>.+?) (?<prep>[^ ]+) (?<reason>.+?) in (?<years>[0-9]+ year)?s? ?(?<weeks>[0-9]+ week)?s? ?(?<days>[0-9]+ day)?s? ?(?<hours>[0-9]+ hour)?s? ?(?<minutes>[0-9]+ minute)?s? ?(?<seconds>[0-9]+ seconds)?", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Singleline);
        public static readonly Regex TimerOnRegex = new Regex(".*?remind (?<target>.+?) (?<prep>[^ ]+) (?<reason>.+?) at (?<time>[0-9]+:[0-9]{2} ?(am|pm)? ?(\\+[0-9]+|\\-[0-9]+)?)( on (?<date>[0-9]+(/|-)[0-9]+(/|-)[0-9]+))?", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Singleline);
        public static readonly Regex TimerOn2Regex = new Regex(".*?remind (?<target>.+?) at (?<time>[0-9]+:[0-9]{2} ?(am|pm)? ?(\\+[0-9]+|\\-[0-9]+)?)( on (?<date>[0-9]+(/|-)[0-9]+(/|-)[0-9]+))? ?(?<prep>[^ ]+) (?<reason>.+)", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Singleline);
    }
}
