using System;
using System.IO;
using System.Text;

namespace Parrot.Windows
{
    internal sealed class WavAudio
    {
        internal float[] Samples;
        internal int SampleRate;

        internal double DurationSeconds
        {
            get { return Samples.Length / (double)SampleRate; }
        }
    }

    internal static class WavFile
    {
        internal static WavAudio Read(string path)
        {
            using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                return Read(stream);
            }
        }

        internal static WavAudio Read(Stream stream)
        {
            if (stream == null || !stream.CanRead || !stream.CanSeek)
            {
                throw new ArgumentException("WAV stream must be readable and seekable.", "stream");
            }

            BinaryReader reader = new BinaryReader(stream, Encoding.ASCII);
            {
                if (ReadFourCc(reader) != "RIFF")
                {
                    throw new InvalidDataException("WAV file is missing the RIFF header.");
                }
                reader.ReadUInt32();
                if (ReadFourCc(reader) != "WAVE")
                {
                    throw new InvalidDataException("Input is not a WAVE file.");
                }

                ushort formatTag = 0;
                ushort channels = 0;
                uint sampleRate = 0;
                ushort bitsPerSample = 0;
                byte[] pcm = null;

                while (stream.Position + 8 <= stream.Length)
                {
                    string chunkId = ReadFourCc(reader);
                    uint chunkLength = reader.ReadUInt32();
                    if (chunkLength > int.MaxValue || stream.Position + chunkLength > stream.Length)
                    {
                        throw new InvalidDataException("WAV chunk length is invalid.");
                    }

                    long chunkEnd = stream.Position + chunkLength;
                    if (chunkId == "fmt ")
                    {
                        if (chunkLength < 16)
                        {
                            throw new InvalidDataException("WAV format chunk is incomplete.");
                        }
                        formatTag = reader.ReadUInt16();
                        channels = reader.ReadUInt16();
                        sampleRate = reader.ReadUInt32();
                        reader.ReadUInt32();
                        reader.ReadUInt16();
                        bitsPerSample = reader.ReadUInt16();
                    }
                    else if (chunkId == "data")
                    {
                        int maximumBytes = WaveInCaptureSession.SampleRate
                            * WaveInCaptureSession.MaximumSeconds * 2;
                        if (chunkLength > maximumBytes)
                        {
                            throw new InvalidDataException("WAV exceeds Parrot's ten-minute limit.");
                        }
                        pcm = reader.ReadBytes((int)chunkLength);
                        if (pcm.Length != (int)chunkLength)
                        {
                            throw new EndOfStreamException("WAV audio data ended early.");
                        }
                    }

                    long nextChunk = chunkEnd + (chunkLength % 2);
                    if (nextChunk > stream.Length)
                    {
                        throw new InvalidDataException("WAV chunk padding is incomplete.");
                    }
                    stream.Position = nextChunk;
                }

                if (formatTag != 1
                    || channels != 1
                    || sampleRate != WaveInCaptureSession.SampleRate
                    || bitsPerSample != 16)
                {
                    throw new InvalidDataException("WAV must be 16 kHz, mono, 16-bit PCM.");
                }
                if (pcm == null || pcm.Length == 0 || pcm.Length % 2 != 0)
                {
                    throw new InvalidDataException("WAV contains no complete PCM samples.");
                }

                float[] samples = new float[pcm.Length / 2];
                for (int index = 0; index < samples.Length; index++)
                {
                    int byteIndex = index * 2;
                    short value = (short)(pcm[byteIndex] | (pcm[byteIndex + 1] << 8));
                    samples[index] = value / 32768F;
                }

                WavAudio audio = new WavAudio();
                audio.Samples = samples;
                audio.SampleRate = (int)sampleRate;
                return audio;
            }
        }

        private static string ReadFourCc(BinaryReader reader)
        {
            byte[] bytes = reader.ReadBytes(4);
            if (bytes.Length != 4)
            {
                throw new EndOfStreamException("WAV header ended early.");
            }
            return Encoding.ASCII.GetString(bytes);
        }
    }
}
