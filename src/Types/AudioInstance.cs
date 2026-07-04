namespace UB3RB0T
{
    using Discord;
    using Discord.Audio;
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;

    public class AudioInstance : IDisposable
    {
        internal bool isDisposed;

        internal readonly SemaphoreSlim streamLock = new SemaphoreSlim(1, 1);

        public Dictionary<ulong, AudioUserState> Users { get; } = new Dictionary<ulong, AudioUserState>();
        public ulong GuildId { get; set; }
        public IVoiceChannel VoiceChannel { get; set; }
        public IAudioClient AudioClient { get; set; }
        public Stream Stream { get; set; }
        public bool SentJoinGreeting { get; set; }
        public bool AllowReconnect { get; set; }
        public DateTime DisconnectedTime { get; set; }

        public async Task<int> GetUserCountAsync()
        {
            return (await this.VoiceChannel.GetUsersAsync().FlattenAsync()).Count();
        }

        public void Dispose()
        {
            this.Dispose(true);
        }

        protected virtual void Dispose(bool isDisposing)
        {
            if (!this.isDisposed)
            {
                this.isDisposed = true;

                // wait up to 5 seconds in case we're already flushing a stream.
                this.streamLock.Wait(5000);

                this.Stream.Dispose();
                this.Stream = null;
                // Seems to be an issue in Discord.Net.WebSockets.DefaultWebSocketClient.DisconnectAsync on disposing SemaphoreSlim;
                // need to investigate further.
                // this.AudioClient?.Dispose();
                this.AudioClient?.StopAsync();
                this.streamLock.Release();
                this.streamLock.Dispose();
            }
        }
    }
}