
namespace UB3RB0T
{
    using Newtonsoft.Json;

    public class QnAMakerData
    {
            /// <summary>
            /// The top answer found in the QnA Service.
            /// </summary>
            [JsonProperty(PropertyName = "answer")]
            public string Answer { get; set; }

            /// <summary>
            /// The score in range [0, 100] corresponding to the top answer found in the QnA Service.
            /// </summary>
            [JsonProperty(PropertyName = "score")]
            public double Score { get; set; }
    }
}
