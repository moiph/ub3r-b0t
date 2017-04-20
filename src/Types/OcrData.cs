
namespace UB3RB0T
{
    using Newtonsoft.Json;
    using System.Collections.Generic;
    using System.Text.RegularExpressions;

    public class OcrData
    {
        private readonly Regex botName = new Regex("(.*ub3r-b)o(t.*)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        [JsonProperty(PropertyName = "language")]
        public string Language { get; set; }

        [JsonProperty(PropertyName = "orientation")]
        public string Orientation { get; set; }

        [JsonProperty(PropertyName = "regions")]
        public Region[] Regions { get; set; }

        public string GetText()
        {
            var words = new List<string>();
            foreach (var r in this.Regions)
            {
                foreach (var l in r.Lines)
                {
                    foreach (var w in l.Words)
                    {
                        // HACK: map ub3r-bot to ub3r-b0t to account for possible misdetection 
                        var text = w.Text;
                        if (botName.IsMatch(text))
                        {
                            text = botName.Replace(text, "${1}0${2}");
                        }
                        words.Add(text);
                    }
                }
            }

            return string.Join(" ", words);
        }
    }

    public class Region
    {
        [JsonProperty(PropertyName = "boundingBox")]
        public string BoundingBox { get; set; }

        [JsonProperty(PropertyName = "lines")]
        public Line[] Lines { get; set; }
    }

    public class Line
    {
        [JsonProperty(PropertyName = "boundingBox")]
        public string BoundingBox { get; set; }

        [JsonProperty(PropertyName = "words")]
        public Word[] Words { get; set; }
    }

    public class Word
    {
        [JsonProperty(PropertyName = "boundingBox")]
        public string BoundingBox { get; set; }

        [JsonProperty(PropertyName = "text")]
        public string Text { get; set; }
    }
}
