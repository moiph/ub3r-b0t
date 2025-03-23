
namespace UB3RB0T
{
    using Newtonsoft.Json;

    public class QnAMakerData
    {
        public QnAMakerAnswer[] Answers { get; set; }
    }

    public class QnAMakerAnswer
    { 
            /// <summary>
            /// The top answer found in the QnA Service.
            /// </summary>
            [JsonProperty(PropertyName = "answer")]
            public string Answer { get; set; }

            /// <summary>
            /// The score in range [0, 1] corresponding to the top answer found in the QnA Service.
            /// </summary>
            [JsonProperty(PropertyName = "confidenceScore")]
            public double Score { get; set; }
    }
}
