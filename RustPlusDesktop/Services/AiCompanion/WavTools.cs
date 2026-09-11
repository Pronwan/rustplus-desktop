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
