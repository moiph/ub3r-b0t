namespace UB3RB0T.Commands
{
    using System.IO;
    using Discord;

    public class CommandResponse
    {
        public string Text { get; set; }
        public Embed Embed { get; set; }
        public FileResponse Attachment { get; set; }
    }

    public class FileResponse
    {
        public string Name { get; set; }
        public Stream Stream { get; set; }
    }
}
