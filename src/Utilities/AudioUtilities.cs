namespace UB3RB0T
{
    using Discord;
    using Discord.Audio;
    using System;
    using System.Collections.Concurrent;
    using System.Diagnostics;
    using System.IO;
    using System.Threading.Tasks;

    public class AudioUtilities
    {
        private static object lockObject = new object();

        private static bool isShuttingDown = false;

        // UB3R-B0T just broadcasts voice clips; memory footprint is low so keep them stored.  TODO: change this assumption if need be :)
        private static ConcurrentDictionary<string, Stream> voiceClips = new ConcurrentDictionary<string, Stream>();

        public static ConcurrentDictionary<ulong, IAudioClient> audioClients = new ConcurrentDictionary<ulong, IAudioClient>();
        public static ConcurrentDictionary<ulong, Stream> streams = new ConcurrentDictionary<ulong, Stream>();

        public static async Task JoinAudioAsync(IVoiceChannel voiceChannel)
        {
            if (isShuttingDown)
            {
                return;
            }

            if (!audioClients.TryGetValue(voiceChannel.GuildId, out IAudioClient audioClient))
            {
                lock (lockObject)
                {
                    if (!audioClients.TryGetValue(voiceChannel.GuildId, out audioClient))
                    {
                        audioClient = voiceChannel.ConnectAsync().Result;
                        var audioStream = audioClient.CreatePCMStream(2880, bitrate: voiceChannel.Bitrate);
                        audioClients.TryAdd(voiceChannel.GuildId, audioClient);
                        streams.TryAdd(voiceChannel.GuildId, audioStream);
                    }
                }
            }

            streams.TryGetValue(voiceChannel.GuildId, out Stream stream);

            if (audioClient.ConnectionState == ConnectionState.Connected)
            {
                await AudioUtilities.SendAudioAsync(stream, "hello.mp3");
            }
            else
            {
                audioClient.Connected += async () =>
                {
                    await AudioUtilities.SendAudioAsync(stream, "hello.mp3");
                };
                audioClient.Disconnected += async (Exception ex) =>
                {
                    await AudioUtilities.LeaveAudioAsync(voiceChannel.GuildId);
                    Console.WriteLine(ex);
                };
            }
        }

        public static async Task LeaveAllAudioAsync()
        {
            isShuttingDown = true;
            foreach (var key in audioClients.Keys)
            {
                await LeaveAudioAsync(key);
            }
        }

        public static async Task LeaveAudioAsync(IGuildChannel guildChannel)
        {
            await LeaveAudioAsync(guildChannel.GuildId);
        }

        public static async Task LeaveAudioAsync(ulong guildId)
        {
            if (streams.TryRemove(guildId, out Stream stream))
            {
                // say our goodbyes
                try
                {
                    await AudioUtilities.SendAudioAsync(stream, "goodbye.mp3");
                }
                catch (Exception ex)
                {
                    // TODO: proper logging
                    Console.WriteLine(ex);
                }

                stream.Dispose();
                stream = null;
            }

            if (audioClients.TryRemove(guildId, out IAudioClient audioClient))
            {
                try
                {
                    await audioClient.DisconnectAsync();
                }
                catch (Exception ex)
                {
                    // TODO: proper logging
                    Console.WriteLine(ex);
                }

                audioClient.Dispose();
                audioClient = null;
            }
        }

        public static async Task SendAudioAsync(IVoiceChannel voiceChannel, string filename)
        {
            if (voiceChannel is IGuildChannel guildChannel)
            {
                var botGuildUser = await guildChannel?.Guild.GetCurrentUserAsync();

                if (voiceChannel != null && botGuildUser.VoiceChannel == voiceChannel)
                {
                    if (streams.TryGetValue(voiceChannel.GuildId, out Stream stream))
                    {
                        await AudioUtilities.SendAudioAsync(stream, filename);
                    }
                }
            }
        }

        public static async Task SendAudioAsync(Stream stream, string filename)
        {
            if (!voiceClips.ContainsKey(filename))
            {
                Console.WriteLine("reading new stream data");

                var filePath = PhrasesConfig.Instance.VoiceFilePath;
                var p = Process.Start(new ProcessStartInfo
                {
                    FileName = "c:\\audio\\ffmpeg",
                    Arguments = $"-i {filePath}{filename} -f s16le -ar 48000 -ac 2 pipe:1 -loglevel warning",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                });

                var memstream = new MemoryStream();
                await p.StandardOutput.BaseStream.CopyToAsync(memstream);
                voiceClips.TryAdd(filename, memstream);
                p.WaitForExit();
            }
            else
            {
                Console.WriteLine("using cached stream data");
            }

            voiceClips[filename].Seek(0, SeekOrigin.Begin);
            await voiceClips[filename].CopyToAsync(stream);
            await stream.FlushAsync();
            Console.WriteLine("flushing audio stream");
        }
    }
}
