
namespace UB3RB0T
{
    using Newtonsoft.Json;
    using System.Collections.Generic;
    using System.Text.RegularExpressions;

    public class RecognitionResultData
    { 
        private readonly Regex botName = new Regex("(.*ub3r-b)o(t.*)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        [JsonProperty(PropertyName = "recognitionResult")]
        public RecognitionResult Result { get; set; }

        [JsonProperty(PropertyName = "status")]
        public string Status { get; set; }

        public string AuthorName { get; set; }

        public string GetText()
        {
            var words = new List<string>();
            foreach (var l in this.Result.Lines)
            {
                // HACK: map ub3r-bot to ub3r-b0t to account for possible misdetection 
                var text = l.Text;
                if (botName.IsMatch(text))
                {
                    text = botName.Replace(text, "${1}0${2}");
                }
                words.Add(text);
            }

            return string.Join(" ", words);
        }
    }

    public class RecognitionResult
    {
        [JsonProperty(PropertyName = "lines")]
        public RecognitionLine[] Lines { get; set; }
    }

    public class RecognitionLine
    {
        [JsonProperty(PropertyName = "boundingBox")]
        public int[] BoundingBox { get; set; }

        [JsonProperty(PropertyName = "text")]
        public string Text { get; set; }
    }

    public class OcrProcessResponse
    {
        public string Response { get; set; }
    }
}
