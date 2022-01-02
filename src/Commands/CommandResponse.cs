namespace UB3RB0T.Commands
{
    using System.Collections.Generic;
    using System.IO;
    using Discord;

    public class CommandResponse
    {
        public string Text { get; set; }
        public List<string> MultiText { get; set; }
        public Embed Embed { get; set; }
        public FileResponse Attachment { get; set; }
        public bool IsHandled { get; set; }
    }

    public class FileResponse
    {
        public string Name { get; set; }
        public Stream Stream { get; set; }
    }
}
