namespace UB3RB0T
{
    using Discord;
    using System;
    using System.Collections.Concurrent;
    using System.Diagnostics;
    using System.IO;
    using System.Threading;
    using System.Threading.Tasks;

    public class AudioUtilities
    {
        internal static readonly SemaphoreSlim joinLock = new SemaphoreSlim(1, 1);
        internal static readonly SemaphoreSlim streamLock  = new SemaphoreSlim(1, 1);

        // UB3R-B0T just broadcasts voice clips; memory footprint is low so keep them stored.  TODO: change this assumption if need be :)
        private static ConcurrentDictionary<string, Stream> voiceClips = new ConcurrentDictionary<string, Stream>();
        private static ConcurrentDictionary<ulong, AudioInstance> audioInstances = new ConcurrentDictionary<ulong, AudioInstance>();

        public static async Task JoinAudioAsync(IVoiceChannel voiceChannel)
        {
            if (!audioInstances.TryGetValue(voiceChannel.GuildId, out AudioInstance audioInstance))
            {
                await joinLock.WaitAsync().ConfigureAwait(false);
                try
                {
                    if (!audioInstances.TryGetValue(voiceChannel.GuildId, out audioInstance))
                    {
                        audioInstance = new AudioInstance
                        {
                            GuildId = voiceChannel.GuildId,
                            AudioClient = await voiceChannel.ConnectAsync().ConfigureAwait(false)
                        };
                        audioInstance.Stream = audioInstance.AudioClient.CreatePCMStream(2880, bitrate: voiceChannel.Bitrate);
                        audioInstances.TryAdd(voiceChannel.GuildId, audioInstance);
                    }
                }
                finally
                {
                    joinLock.Release();
                }
            }

            if (audioInstance.AudioClient.ConnectionState == ConnectionState.Connected)
            {
                await AudioUtilities.SendAudioAsync(audioInstance, "hello.mp3");
            }
            else
            {
                audioInstance.AudioClient.Connected += async () =>
                {
                    await AudioUtilities.SendAudioAsync(audioInstance, "hello.mp3");
                    await Task.CompletedTask;
                };
                audioInstance.AudioClient.Disconnected += async (Exception ex) =>
                {
                    await AudioUtilities.LeaveAudioAsync(voiceChannel.GuildId);
                    Console.WriteLine(ex);
                };
            }
        }

        public static async Task LeaveAllAudioAsync()
        {
            foreach (var key in audioInstances.Keys)
            {
                await AudioUtilities.LeaveAudioAsync(key);
            }
        }

        public static async Task LeaveAudioAsync(IGuildChannel guildChannel)
        {
            await AudioUtilities.LeaveAudioAsync(guildChannel.GuildId);
        }

        public static async Task LeaveAudioAsync(ulong guildId)
        {
            if (audioInstances.TryRemove(guildId, out AudioInstance audioInstance))
            {
                // say our goodbyes
                try
                {
                    await AudioUtilities.SendAudioAsync(audioInstance, "goodbye.mp3");
                    await Task.Delay(1000);
                }
                catch (Exception ex)
                {
                    // TODO: proper logging
                    Console.WriteLine(ex);
                }

                audioInstance.Stream.Dispose();

                try
                {
                    await audioInstance.AudioClient.DisconnectAsync();
                }
                catch (Exception ex)
                {
                    // TODO: proper logging
                    Console.WriteLine(ex);
                }

                audioInstance.AudioClient.Dispose();
            }
        }

        public static async Task SendAudioAsync(IVoiceChannel voiceChannel, string filename)
        {
            if (voiceChannel is IGuildChannel guildChannel)
            {
                var botGuildUser = await guildChannel.Guild.GetCurrentUserAsync();

                if (voiceChannel != null && botGuildUser.VoiceChannel == voiceChannel)
                {
                    if (audioInstances.TryGetValue(voiceChannel.GuildId, out AudioInstance audioInstance))
                    {
                        await AudioUtilities.SendAudioAsync(audioInstance, filename);
                    }
                }
            }
        }

        public static async Task SendAudioAsync(AudioInstance audioInstance, string filename)
        {
            Task.Run(async () =>
            {
                try
                {
                    await AudioUtilities.SendAudioAsyncInternalAsync(audioInstance, filename);
                }
                catch (Exception ex)
                {
                    // TODO: proper logging
                    Console.WriteLine(ex);
                }
            }).Forget();

            await Task.CompletedTask;
        }

        private static async Task SendAudioAsyncInternalAsync(AudioInstance audioInstance, string filename)
        {
            if (!voiceClips.ContainsKey(filename))
            {
                await streamLock.WaitAsync().ConfigureAwait(false);
                try
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
                    }
                }
                finally
                {
                    streamLock.Release();
                }
            }
            else
            {
                Console.WriteLine("using cached stream data");
            }

            await streamLock.WaitAsync().ConfigureAwait(false);
            try
            { 
                voiceClips[filename].Seek(0, SeekOrigin.Begin);
                await voiceClips[filename].CopyToAsync(audioInstance.Stream);
                await audioInstance.Stream.FlushAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
                audioInstance.Stream.Dispose();
                audioInstance.AudioClient.Dispose();
                audioInstances.TryRemove(audioInstance.GuildId, out AudioInstance oldInstance);
            }
            finally
            {
                streamLock.Release();
            }

            Console.WriteLine("flushing audio stream");
        }
    }
}
