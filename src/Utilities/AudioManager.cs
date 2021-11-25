﻿namespace UB3RB0T
{
    using Discord;
    using Serilog;
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Runtime.InteropServices;
    using System.Threading.Tasks;

    public class AudioManager : IDisposable
    {
        private readonly ConcurrentDictionary<ulong, AudioInstance> audioInstances = new ConcurrentDictionary<ulong, AudioInstance>();
        private readonly ConcurrentDictionary<string, byte[]> audioBytes = new ConcurrentDictionary<string, byte[]>();

        public async Task<bool> JoinAudioAsync(IVoiceChannel voiceChannel)
        {
            bool joinedAudio = false;

            var currentUser = await voiceChannel.Guild.GetCurrentUserAsync();
            if (!audioInstances.TryGetValue(voiceChannel.GuildId, out AudioInstance audioInstance) || currentUser.VoiceChannel == null)
            {
                audioInstance = new AudioInstance
                {
                    GuildId = voiceChannel.GuildId,
                    AudioClient = await voiceChannel.ConnectAsync(selfDeaf: true).ConfigureAwait(false)
                };

                audioInstances[voiceChannel.GuildId] = audioInstance;
                audioInstance.Stream = audioInstance.AudioClient.CreatePCMStream(Discord.Audio.AudioApplication.Voice, null, 400);

                joinedAudio = true;
            }
            else
            {
                Log.Information($"{{Indicator}} Already in a voice channel for {voiceChannel.GuildId}", "[audio]");
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
                audioInstance.AudioClient.Disconnected += (Exception ex) =>
                {
                    Log.Error(ex, "{{Indicator}} Disconnected from audio", "[audio]");
                    return Task.CompletedTask;
                };
            }

            return joinedAudio;
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
                    Log.Error(ex, "{Indicator} Failed to send audio on leave", "[audio]");
                }

                audioInstance.Dispose();
            }
            else
            {
                Log.Information($"{{Indicator}} Not in a voice channel for {guildId}", "[audio]");
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
                        string[] voiceFileNames;
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
                Log.Verbose($"{{Indicator}} [{filename}] reading data", "[audio]");
                p = Process.Start(new ProcessStartInfo
                {
                    FileName = "ffmpeg",
                    Arguments = $"-i \"{filePath}\" -f s16le -ar 48000 -ac 2 pipe:1 -loglevel error",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardInput = true,
                });
            }
            else
            {
                Log.Verbose($"{{Indicator}} [{filename}] using cached bytes", "[audio]");
            }

            await audioInstance.streamLock.WaitAsync();

            Log.Verbose($"{{Indicator}} [{filename}] lock obtained", "[audio]");

            try
            {
                if (audioInstance.Stream != null)
                {
                    Log.Verbose($"{{Indicator}} [{filename}] stream copy", "[audio]");

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
                                ScaleVolumeSpan(data, .75f);
                                audioBytes[filename] = data;
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
                        Log.Debug($"{{Indicator}} [{filename}] timeout occurred", "[audio]");
                        throw new TimeoutException();
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "{Indicator} Error sending audio clip", "[audio]");
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

        // from https://gist.github.com/Joe4evr/e102d8d8627989a61624237e44210838
        private static void ScaleVolumeSpan(Span<byte> audioSamples, float volume)
        {
            // 16-bit precision for the multiplication
            int volumeFixed = (int)Math.Round(volume * 65536d);

            // Reinterpret the bytes as shorts
            var asShorts = MemoryMarshal.Cast<byte, short>(audioSamples);
            for (int i = 0; i < asShorts.Length; i++)
            {
                asShorts[i] = (short)((asShorts[i] * volumeFixed) >> 16);
            }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool isDisposing)
        {
            foreach (var kvp in this.audioInstances)
            {
                kvp.Value.Dispose();
            }
            this.audioInstances.Clear();
        }
    }
}
