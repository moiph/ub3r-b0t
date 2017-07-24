namespace UB3RB0T
{
    using Discord;
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Threading.Tasks;

    public class AudioManager : IDisposable
    {
        private ConcurrentDictionary<ulong, AudioInstance> audioInstances = new ConcurrentDictionary<ulong, AudioInstance>();
        private ConcurrentDictionary<string, byte[]> audioBytes = new ConcurrentDictionary<string, byte[]>();

        public async Task JoinAudioAsync(IVoiceChannel voiceChannel)
        {
            var currentUser = await voiceChannel.Guild.GetCurrentUserAsync();
            if (!audioInstances.TryGetValue(voiceChannel.GuildId, out AudioInstance audioInstance) || currentUser.VoiceChannel == null)
            {
                audioInstance = new AudioInstance
                {
                    GuildId = voiceChannel.GuildId,
                    AudioClient = await voiceChannel.ConnectAsync().ConfigureAwait(false)
                };

                audioInstances[voiceChannel.GuildId] = audioInstance;
                audioInstance.Stream = audioInstance.AudioClient.CreatePCMStream(Discord.Audio.AudioApplication.Voice, null, 250);
            }
            else
            {
                Console.WriteLine($"Already in a voice channel for {voiceChannel.GuildId}");
            }

            if (audioInstance.AudioClient.ConnectionState == ConnectionState.Connected && audioInstance.Stream.CanWrite)
            {
                await this.SendAudioAsync(audioInstance, PhrasesConfig.Instance.GetVoiceFileNames(VoicePhraseType.BotJoin).Random());
            }
            else
            {
                audioInstance.AudioClient.Connected += async () =>
                {
                    await this.SendAudioAsync(audioInstance, PhrasesConfig.Instance.GetVoiceFileNames(VoicePhraseType.BotJoin).Random());
                };
                audioInstance.AudioClient.Disconnected += async (Exception ex) =>
                {
                    await this.LeaveAudioAsync(voiceChannel.GuildId);
                    Console.WriteLine(ex);
                };
            }
        }

        public async Task LeaveAllAudioAsync()
        {
            var leaveTasks = new List<Task>();
            foreach (var key in audioInstances.Keys)
            {
                leaveTasks.Add(this.LeaveAudioAsync(key));
            }

            await Task.WhenAll(leaveTasks);
        }

        public async Task LeaveAudioAsync(IGuildChannel guildChannel)
        {
            await this.LeaveAudioAsync(guildChannel.GuildId);
        }

        public async Task LeaveAudioAsync(ulong guildId)
        {
            if (audioInstances.TryRemove(guildId, out AudioInstance audioInstance))
            {
                // say our goodbyes
                try
                {
                    await this.SendAudioAsyncInternalAsync(audioInstance, PhrasesConfig.Instance.GetVoiceFileNames(VoicePhraseType.BotLeave).Random());
                }
                catch (Exception ex)
                {
                    // TODO: proper logging
                    Console.WriteLine(ex);
                }

                audioInstance.Dispose();
            }
            else
            {
                Console.WriteLine($"Not in a voice channel for {guildId}");
            }
        }

        public async Task SendAudioAsync(IGuildUser guildUser, IVoiceChannel voiceChannel, VoicePhraseType voicePhraseType)
        {
            if (voiceChannel is IGuildChannel guildChannel)
            {
                var botGuildUser = await guildChannel.Guild.GetCurrentUserAsync();

                if (voiceChannel != null && botGuildUser.VoiceChannel == voiceChannel)
                {
                    if (audioInstances.TryGetValue(voiceChannel.GuildId, out AudioInstance audioInstance))
                    {
                        string[] voiceFileNames = null;
                        if (voicePhraseType == VoicePhraseType.UserJoin)
                        {
                            // if it's a first time rejoin, let's make it special
                            voiceFileNames = PhrasesConfig.Instance.GetVoiceFileNames(VoicePhraseType.UserJoin);
                            if (!audioInstance.Users.ContainsKey(guildUser.Id))
                            {
                                audioInstance.Users[guildUser.Id] = AudioUserState.SeenOnce;
                            }
                            else if (audioInstance.Users[guildUser.Id] == AudioUserState.SeenOnce)
                            {
                                audioInstance.Users[guildUser.Id] = AudioUserState.SeenMultiple;
                                voiceFileNames = PhrasesConfig.Instance.GetVoiceFileNames(VoicePhraseType.UserRejoin);
                            }
                        }
                        else
                        {
                            voiceFileNames = PhrasesConfig.Instance.GetVoiceFileNames(VoicePhraseType.UserLeave);
                        }

                        await this.SendAudioAsync(audioInstance, voiceFileNames.Random());
                    }
                }
            }
        }

        public async Task SendAudioAsync(AudioInstance audioInstance, string filename)
        {
            await this.SendAudioAsyncInternalAsync(audioInstance, filename);
        }

        private async Task SendAudioAsyncInternalAsync(AudioInstance audioInstance, string filePath)
        {
            var filename = Path.GetFileName(filePath);

            Process p = null;
            if (!audioBytes.ContainsKey(filename))
            {
                Console.WriteLine($"[audio] [{filename}] reading data");
                p = Process.Start(new ProcessStartInfo
                {
                    FileName = "c:\\audio\\ffmpeg",
                    Arguments = $"-i {filePath} -f s16le -ar 48000 -ac 2 pipe:1 -loglevel error",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardInput = true,
                });
            }
            else
            {
                Console.WriteLine($"[audio] [{filename}] using cached bytes");
            }

            await audioInstance.streamLock.WaitAsync();

            Console.WriteLine($"[audio] [{filename}] lock obtained");

            try
            {
                if (audioInstance.Stream != null)
                {
                    Console.WriteLine($"[audio] [{filename}] stream copy");

                    if (p != null)
                    {
                        using (var memoryStream = new MemoryStream())
                        {
                            await p.StandardOutput.BaseStream.CopyToAsync(memoryStream);
                            byte[] data;
                            using (var binaryReader = new BinaryReader(memoryStream))
                            {
                                binaryReader.BaseStream.Position = 0;
                                data = binaryReader.ReadBytes((int)memoryStream.Length);
                            }

                            if (!audioBytes.ContainsKey(filename))
                            {
                                audioBytes[filename] = AdjustVolume(data, .8f);
                            }
                        }
                    }

                    using (var memoryStream = new MemoryStream(audioBytes[filename]))
                    { 
                        await memoryStream.CopyToAsync(audioInstance.Stream);
                    }

                    p?.WaitForExit(8000);
                    var flushTask = audioInstance.Stream.FlushAsync();
                    var timeoutTask = Task.Delay(8000);

                    if (await Task.WhenAny(flushTask, timeoutTask) == timeoutTask)
                    {
                        Console.WriteLine($"[audio] [{filename}] timeout occurred");
                        throw new TimeoutException();
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
                p?.Dispose();
            }
            finally
            {
                if (audioInstance != null && !audioInstance.isDisposed)
                {
                    audioInstance?.streamLock?.Release();
                }
            }
        }

        private static byte[] AdjustVolume(byte[] audioSamples, float volume)
        {
            var array = new byte[audioSamples.Length];
            for (var i = 0; i < array.Length; i += 2)
            {
                short buf1 = audioSamples[i + 1];
                short buf2 = audioSamples[i];

                buf1 = (short)((buf1 & 0xff) << 8);
                buf2 = (short)(buf2 & 0xff);

                var res = (short)(buf1 | buf2);
                res = (short)(res * volume);

                array[i] = (byte)res;
                array[i + 1] = (byte)(res >> 8);
            }

            return array;
        }

        public void Dispose() => Dispose(true);

        public void Dispose(bool isDisposing)
        {
            foreach (var kvp in this.audioInstances)
            {
                kvp.Value.Dispose();
            }
            this.audioInstances.Clear();
        }
    }
}
