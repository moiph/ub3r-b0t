namespace UB3RB0T
{
    /// <summary>
    /// Internal API response; implemntation details up to consumer.
    /// </summary>
    public class BotApiResponse
    {
        public string Msg { get; set; }
        public string[] Msgs { get; set; } = new string[] { };
        public string Error { get; set; }
        public EmbedData Embed { get; set; }
    }
}
