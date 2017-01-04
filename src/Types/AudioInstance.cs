namespace UB3RB0T
{
    using Discord.Audio;
    using System.IO;

    public class AudioInstance
    {
        public IAudioClient AudioClient { get; set; }
        public Stream Stream { get; set; }
    }
}