
namespace UB3RB0T
{
    using Newtonsoft.Json;

    public class AnalyzeImageData
    {
        [JsonProperty(PropertyName = "description")]
        public Description Description { get; set; }
    }

    public class Description
    {
        [JsonProperty(PropertyName = "tags")]
        public string[] Tags { get; set; }
    }
}
