using Discord.Audio;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace UB3RB0T
{
    public class AudioUtilities
    {
        private static object lockObject = new object();
        private static Dictionary<string, byte[]> voiceClips = new Dictionary<string, byte[]>();

        public static async Task SendAudio(IAudioClient audioClient, string filename)
        {
            if (filename.Contains(","))
            {
                var files = filename.Split(new[] { ',' });
                filename = files[new Random().Next() % files.Count()];
            }

            try
            {
                int blockSize = 4000;

                if (!voiceClips.ContainsKey(filename))
                {
                    voiceClips.Add(filename, ReadAudioFile(filename));
                }

                byte[] fullbuffer = voiceClips[filename];
                int fullbufferLength = fullbuffer.Length;

                int writepos = 0;
                int count = 0;

                var stream = audioClient.CreateOpusStream(100);

                lock (lockObject)
                {
                    while (true)
                    {
                        byte[] tempBuffer = new byte[blockSize];
                        count = (fullbufferLength - writepos - blockSize) > 0 ? blockSize : (fullbufferLength - writepos);
                        Buffer.BlockCopy(fullbuffer, writepos, tempBuffer, 0, count);
                        stream.Write(tempBuffer, writepos, count);

                        writepos += count;

                        if (writepos >= fullbufferLength)
                        {
                            break;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                try
                {
                    await audioClient.DisconnectAsync();
                }
                catch
                {
                    // ignore 
                }

                Console.Write(ex);
            }
        }

        private static byte[] ReadAudioFile(string filename)
        {
            var p = Process.Start(new ProcessStartInfo
            {
                FileName = "s:\\uberconfig\\ffmpeg",
                Arguments = "-i S:\\uberconfig\\obv\\" + filename + " -f s16le -ar 48000 -ac 2 pipe:1 -loglevel warning",
                UseShellExecute = false,
                RedirectStandardOutput = true,
            });

            return AdjustVolume(ReadAllBytes(p.StandardOutput.BaseStream), .9f);
        }

        private static byte[] ReadAllBytes(Stream source)
        {
            var buffer = new byte[1024 * 16];
            using (var memStream = new MemoryStream())
            {
                int read;
                while ((read = source.Read(buffer, 0, buffer.Length)) > 0)
                {
                    memStream.Write(buffer, 0, read);
                }

                return memStream.ToArray();
            }
        }

        private static byte[] AdjustVolume(byte[] audioSamples, float volume)
        {
            if (Math.Abs(volume - 1.0f) < 0.01f)
                return audioSamples;
            var array = new byte[audioSamples.Length];
            for (var i = 0; i < array.Length; i += 2)
            {

                // convert byte pair to int
                short buf1 = audioSamples[i + 1];
                short buf2 = audioSamples[i];

                buf1 = (short)((buf1 & 0xff) << 8);
                buf2 = (short)(buf2 & 0xff);

                var res = (short)(buf1 | buf2);
                res = (short)(res * volume);

                // convert back
                array[i] = (byte)res;
                array[i + 1] = (byte)(res >> 8);

            }
            return array;
        }
    }
}
