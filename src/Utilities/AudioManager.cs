namespace UB3RB0T
{
    using Discord;
    using Discord.Audio;
    using Serilog;
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Linq;
    using System.Runtime.InteropServices;
    using System.Threading;
    using System.Threading.Tasks;

    public class AudioManager : IDisposable
    {
        private readonly ConcurrentDictionary<ulong, AudioInstance> audioInstances = new ConcurrentDictionary<ulong, AudioInstance>();
        private readonly ConcurrentDictionary<string, byte[]> audioBytes = new ConcurrentDictionary<string, byte[]>();
        private bool isMonitoring;

        public async Task<bool> JoinAudioAsync(IVoiceChannel voiceChannel, bool allowReconnect)
        {
            var currentUser = await voiceChannel.Guild.GetCurrentUserAsync();
            if (!audioInstances.TryGetValue(voiceChannel.GuildId, out AudioInstance audioInstance) || currentUser.VoiceChannel == null)
            {
                await this.CreateAudioInstance(voiceChannel, allowReconnect);
                Log.Information($"{{Indicator}} Joined voice channel for {voiceChannel.GuildId}", "[audio]");
                return true;
            }

            Log.Information($"{{Indicator}} Already in a voice channel for {voiceChannel.GuildId}", "[audio]");
            return false;
        }

        private async Task<AudioInstance> CreateAudioInstance(IVoiceChannel voiceChannel, bool allowReconnect)
        {
            var audioInstance = new AudioInstance
            {
                GuildId = voiceChannel.GuildId,
                VoiceChannel = voiceChannel,
                AllowReconnect = allowReconnect,
            };
            
            audioInstances[voiceChannel.GuildId] = audioInstance;
            audioInstance.AudioClient = await voiceChannel.ConnectAsync(selfDeaf: true);
            audioInstance.Stream = audioInstance.AudioClient.CreatePCMStream(Discord.Audio.AudioApplication.Voice, null, 400);

            if (audioInstance.AudioClient.ConnectionState == ConnectionState.Connected && audioInstance.Stream.CanWrite)
            {
                await this.SendAudioAsync(audioInstance, BotConfig.Instance.GetVoiceFileNames(VoicePhraseType.BotJoin).Random());
                audioInstance.SentJoinGreeting = true;
            }

            audioInstance.AudioClient.Connected += async () =>
            {
                Log.Information("{Indicator} Connected to audio, creating stream", "[audio]");
                audioInstance.Stream = audioInstance.AudioClient.CreatePCMStream(Discord.Audio.AudioApplication.Voice, null, 400);

                if (!audioInstance.SentJoinGreeting)
                {
                    await this.SendAudioAsync(audioInstance, BotConfig.Instance.GetVoiceFileNames(VoicePhraseType.BotJoin).Random());
                    audioInstance.SentJoinGreeting = true;
                }
            };

            audioInstance.AudioClient.Disconnected += async ex =>
            {
                Log.Warning(ex, $"{{Indicator}} Disconnected on {voiceChannel.GuildId}", "[audio]");

                if (audioInstance.AllowReconnect)
                {
                    audioInstance.NeedsReconnect = true;
                    Log.Information(ex, $"{{Indicator}} Requesting reconnect on {voiceChannel.GuildId}", "[audio]");
                }
            };

            return audioInstance;
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
            if (audioInstances.TryGetValue(guildId, out AudioInstance audioInstance))
            {
                // say our goodbyes
                await this.SendAudioAsync(audioInstance, BotConfig.Instance.GetVoiceFileNames(VoicePhraseType.BotLeave).Random());

                try
                {
                    Log.Debug($"{{Indicator}} Disposing audio instance for {guildId}", "[audio]");
                    audioInstances.TryRemove(guildId, out _);
                    audioInstance.Dispose();
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "{Indicator} Failed to dispose audio instance on leave", "[audio]");
                }
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
                            voiceFileNames = BotConfig.Instance.GetVoiceFileNames(VoicePhraseType.UserJoin);
                            if (!audioInstance.Users.ContainsKey(guildUser.Id))
                            {
                                audioInstance.Users[guildUser.Id] = AudioUserState.SeenOnce;
                            }
                            else if (audioInstance.Users[guildUser.Id] == AudioUserState.SeenOnce)
                            {
                                audioInstance.Users[guildUser.Id] = AudioUserState.SeenMultiple;
                                voiceFileNames = BotConfig.Instance.GetVoiceFileNames(VoicePhraseType.UserRejoin);
                            }
                        }
                        else
                        {
                            voiceFileNames = BotConfig.Instance.GetVoiceFileNames(VoicePhraseType.UserLeave);
                        }

                        await this.SendAudioAsync(audioInstance, voiceFileNames.Random());
                    }
                }
            }
        }

        public async Task SendAudioAsync(AudioInstance audioInstance, string filename)
        {
            try
            {
                await this.SendAudioAsyncInternalAsync(audioInstance, filename);
            }
            catch (Exception ex)
            {
                Log.Error(ex, $"{{Indicator}} Error sending audio clip for {audioInstance.GuildId}", "[audio]");
            }
        }

        public async Task Monitor()
        {
            if (!this.isMonitoring)
            {
                this.isMonitoring = true;

                while (true)
                {
                    var reconnects = audioInstances.Where(a => a.Value.NeedsReconnect).Select(a => a.Key);
                    if (reconnects.Count() > 0)
                    {
                        foreach (ulong guildId in reconnects)
                        {
                            Log.Information($"{{Indicator}} Reconnecting audio for {guildId}", "[audio]");
                            try
                            {
                                audioInstances.TryRemove(guildId, out var audioInstance);
                                var voiceChannel = audioInstance.VoiceChannel;
                                audioInstance.Dispose();

                                await this.CreateAudioInstance(voiceChannel, allowReconnect: true);
                            }
                            catch (Exception ex)
                            {
                                Log.Error(ex, $"{{Indicator}} Failed to reonnect audio instance on {guildId}", "[audio]");
                            }
                        }
                    }

                    await Task.Delay(10000);
                }
            }
        }

        private async Task SendAudioAsyncInternalAsync(AudioInstance audioInstance, string filePath)
        {
            var filename = Path.GetFileName(filePath);
            string cacheFilePath = null;
            if (!string.IsNullOrEmpty(BotConfig.Instance.VoiceCachePath))
            {
                cacheFilePath = Path.Combine(BotConfig.Instance.VoiceCachePath, Path.ChangeExtension(filename, ".cache"));
            }

            Log.Verbose($"{{Indicator}} [{filename}] waiting on stream lock", "[audio]");
            if (audioInstance == null || audioInstance.isDisposed)
            {
                Log.Warning($"{{Indicator}} [{filename}] audio instance is disposed, aborting send", "[audio]");
                return;
            }

            await audioInstance.streamLock.WaitAsync();
            Log.Verbose($"{{Indicator}} [{filename}] lock obtained", "[audio]");

            // if not in memory cache, check disk cache or run ffmpeg
            if (!audioBytes.ContainsKey(filename))
            {
                Log.Verbose($"{{Indicator}} [{filename}] reading data", "[audio]");

                if (cacheFilePath != null && File.Exists(cacheFilePath))
                {
                    Log.Verbose($"{{Indicator}} [{filename}] reading data from disk cache", "[audio]");
                    var bytes = await File.ReadAllBytesAsync(cacheFilePath);
                    audioBytes[filename] = bytes;
                }
                else
                {
                    Log.Verbose($"{{Indicator}} [{filename}] running ffmpeg", "[audio]");

                    var p = Process.Start(new ProcessStartInfo
                    {
                        FileName = "ffmpeg",
                        Arguments = $"-hide_banner -re -i \"{filePath}\" -f s16le -ar 48000 -ac 2 -loglevel error pipe:1",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardInput = true,
                    });

                    using var audioStream = new MemoryStream();
                    await p.StandardOutput.BaseStream.CopyToAsync(audioStream);
                    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(8));
                    await p.WaitForExitAsync(cts.Token);

                    Log.Verbose($"{{Indicator}} [{filename}] ffmpeg complete", "[audio]");

                    using var binaryReader = new BinaryReader(audioStream);
                    binaryReader.BaseStream.Position = 0;
                    
                    var data = binaryReader.ReadBytes((int)audioStream.Length);
                    ScaleVolumeSpan(data, .75f);

                    if (cacheFilePath != null)
                    {
                        await File.WriteAllBytesAsync(cacheFilePath, data);
                        Log.Verbose($"{{Indicator}} [{filename}] saved to disk cache", "[audio]");
                        audioBytes[filename] = data;
                    }
                }
            }
            else
            {
                Log.Verbose($"{{Indicator}} [{filename}] using cached bytes", "[audio]");
            }

            try
            {
                if (audioInstance.Stream != null)
                {
                    Log.Verbose($"{{Indicator}} [{filename}] copying audio bytes to audio stream", "[audio]");
                    using var memoryStream = new MemoryStream(audioBytes[filename]);    
                    await memoryStream.CopyToAsync(audioInstance.Stream);

                    Log.Verbose($"{{Indicator}} [{filename}] flushing audio stream", "[audio]");
                    var cts = new CancellationTokenSource(TimeSpan.FromSeconds(8));
                    await audioInstance.Stream.FlushAsync(cts.Token);

                    Log.Verbose($"{{Indicator}} [{filename}] audio send complete", "[audio]");
                }
                else
                {
                    Log.Warning($"{{Indicator}} [{filename}] audio stream is null", "[audio]");
                }
            }
            catch (TaskCanceledException ex)
            {
                // TODO: Track to trigger a reconnect if stream writes are timing out

                Log.Warning(ex, $"{{Indicator}} [{filename}] timeout occurred", "[audio]");
            }
            catch (Exception ex)
            {
                Log.Error(ex, "{Indicator} Error sending audio clip", "[audio]");
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
