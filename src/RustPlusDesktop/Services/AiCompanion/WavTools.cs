using System;
using System.IO;
using NAudio.Wave;

namespace RustPlusDesk.Services.AiCompanion
{
    /// <summary>
    /// Gets a recording down to something worth uploading.
    ///
    /// The game track comes off the sound card at whatever the device runs at — 48 kHz in 32-bit
    /// float is normal, and five minutes of that is close to sixty megabytes. At 16 kHz in
    /// 16-bit mono the same five minutes is under ten, speech is every bit as intelligible, and
    /// it is inside the limits every provider sets on an upload.
    /// </summary>
    public static class WavTools
    {
        public const int SpeechSampleRate = 16000;

        /// <summary>
        /// Returns a 16 kHz 16-bit mono copy, or the original path when it is already that.
        ///
        /// The copy goes next to the original and is the caller's to delete — see
        /// <see cref="IsTemporaryCopy"/> for telling the two apart.
        /// </summary>
        public static string ToSpeechWav(string path)
        {
            try
            {
                using var reader = new WaveFileReader(path);

                var format = reader.WaveFormat;
                if (format.SampleRate == SpeechSampleRate &&
                    format.Channels == 1 &&
                    format.BitsPerSample == 16 &&
                    format.Encoding == WaveFormatEncoding.Pcm)
                    return path;

                var target = Path.Combine(
                    Path.GetDirectoryName(path)!,
                    Path.GetFileNameWithoutExtension(path) + SpeechSuffix + ".wav");

                var wanted = new WaveFormat(SpeechSampleRate, 16, 1);
                using (var resampler = new MediaFoundationResampler(reader, wanted) { ResamplerQuality = 60 })
                    WaveFileWriter.CreateWaveFile(target, resampler);

                return target;
            }
            catch
            {
                // Media Foundation is missing on N editions of Windows without the media pack.
                // The original still plays — it is only larger, and the provider will say so if
                // it is too large.
                return path;
            }
        }

        private const string SpeechSuffix = "-16k";

        /// <summary>Whether this path is a conversion and can be deleted once it has been sent.</summary>
        public static bool IsTemporaryCopy(string path) =>
            Path.GetFileNameWithoutExtension(path).EndsWith(SpeechSuffix, StringComparison.Ordinal);

        /// <summary>
        /// Puts a wav header on raw samples, with the real length in it.
        /// </summary>
        public static byte[] Wrap(byte[] pcm, int sampleRate, short channels = 1, short bits = 16)
        {
            using var buffer = new MemoryStream();
            using var writer = new BinaryWriter(buffer);

            int byteRate = sampleRate * channels * bits / 8;
            short blockAlign = (short)(channels * bits / 8);

            writer.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));
            writer.Write(36 + pcm.Length);
            writer.Write(System.Text.Encoding.ASCII.GetBytes("WAVE"));

            writer.Write(System.Text.Encoding.ASCII.GetBytes("fmt "));
            writer.Write(16);                 // chunk size for PCM
            writer.Write((short)1);           // format: PCM
            writer.Write(channels);
            writer.Write(sampleRate);
            writer.Write(byteRate);
            writer.Write(blockAlign);
            writer.Write(bits);

            writer.Write(System.Text.Encoding.ASCII.GetBytes("data"));
            writer.Write(pcm.Length);
            writer.Write(pcm);

            writer.Flush();
            return buffer.ToArray();
        }

        /// <summary>
        /// Rewrites a wav header that was written before its length was known.
        ///
        /// Speech is generated as it is spoken, so a provider streaming a wav has to commit to
        /// a header first and fills the length in with a placeholder — usually all ones, which
        /// is -1 once a reader takes it as a signed count. The reader then refuses the file
        /// outright, and the only visible symptom is an answer read by the wrong voice.
        ///
        /// The samples themselves are fine: this keeps the format the header declares and
        /// replaces the length with the number of bytes actually present. Anything that does
        /// not parse is handed back untouched, since a reader that can cope with it should
        /// still get the chance.
        /// </summary>
        public static byte[] EnsurePlayable(byte[] wav)
        {
            try
            {
                if (wav.Length < 44) return wav;

                var ascii = System.Text.Encoding.ASCII;
                if (ascii.GetString(wav, 0, 4) != "RIFF" || ascii.GetString(wav, 8, 4) != "WAVE")
                    return wav;

                int rate = 0, offset = 12;
                short channels = 1, bits = 16;

                while (offset + 8 <= wav.Length)
                {
                    var id = ascii.GetString(wav, offset, 4);
                    int size = BitConverter.ToInt32(wav, offset + 4);
                    int start = offset + 8;

                    if (id == "fmt " && start + 16 <= wav.Length)
                    {
                        channels = BitConverter.ToInt16(wav, start + 2);
                        rate = BitConverter.ToInt32(wav, start + 4);
                        bits = BitConverter.ToInt16(wav, start + 14);
                    }
                    else if (id == "data")
                    {
                        int actual = wav.Length - start;
                        if (rate <= 0 || actual <= 0) return wav;

                        // Already honest about its own length: leave it alone.
                        if (size > 0 && size <= actual) return wav;

                        var samples = new byte[actual];
                        Buffer.BlockCopy(wav, start, samples, 0, actual);
                        return Wrap(samples, rate, channels, bits);
                    }

                    // A chunk claiming an impossible size is the same corruption, one chunk
                    // earlier — there is nothing safe to skip to.
                    if (size <= 0 || start + size > wav.Length) return wav;

                    offset = start + size + (size % 2);   // chunks are word-aligned
                }

                return wav;
            }
            catch
            {
                return wav;
            }
        }

        /// <summary>How long a wav file runs, for the size checks and the tile's label.</summary>
        public static TimeSpan Duration(string path)
        {
            try
            {
                using var reader = new WaveFileReader(path);
                return reader.TotalTime;
            }
            catch
            {
                return TimeSpan.Zero;
            }
        }

        /// <summary>
        /// True when the track is silence. Saves an upload and a bill for a microphone that was
        /// muted in Windows, which is otherwise indistinguishable from a question nobody answered.
        /// </summary>
        public static bool IsSilent(string path)
        {
            try
            {
                using var reader = new AudioFileReader(path);

                var buffer = new float[reader.WaveFormat.SampleRate];
                int read;
                while ((read = reader.Read(buffer, 0, buffer.Length)) > 0)
                {
                    for (int i = 0; i < read; i++)
                        if (Math.Abs(buffer[i]) > 0.01f) return false;
                }

                return true;
            }
            catch
            {
                // Unreadable is not the same as silent, and refusing to send is the worse error.
                return false;
            }
        }
    }
}
