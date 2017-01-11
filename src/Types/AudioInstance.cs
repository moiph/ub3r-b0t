namespace UB3RB0T
{
    using Discord.Audio;
    using System;
    using System.IO;
    using System.Threading;

    public class AudioInstance : IDisposable
    {
        private bool isDisposed;

        internal readonly SemaphoreSlim streamLock = new SemaphoreSlim(1, 1);

        public ulong GuildId { get; set; }
        public IAudioClient AudioClient { get; set; }
        public Stream Stream { get; set; }

        public void Dispose() => Dispose(true);

        public void Dispose(bool isDisposing)
        {
            if (!this.isDisposed)
            {
                this.AudioClient.Dispose();
                this.Stream?.Dispose();
                this.streamLock.Dispose();
                this.isDisposed = true;
            }
        }
    }
}