namespace UB3RB0T
{
    public class EmbedData
    {
        public string Author { get; set; }
        public string AuthorUrl { get; set; }
        public string AuthorIconUrl { get; set; }

        public string Title { get; set; }
        public string Description { get; set; }
        public string Url { get; set; }
        public string Footer { get; set; }
        public string FooterIconUrl { get; set; }

        public string ThumbnailUrl { get; set; }

        public string Color { get; set; }

        public EmbedFieldData[] EmbedFields { get; set; }
    }

    public class EmbedFieldData
    {
        public bool IsInline { get; set; }
        public string Name { get; set; }
        public string Value { get; set; }
    }
}
