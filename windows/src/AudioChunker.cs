using System;
using System.Collections.Generic;

namespace Parrot.Windows
{
    internal struct AudioChunk
    {
        internal AudioChunk(int offset, int length)
        {
            Offset = offset;
            Length = length;
        }

        internal int Offset;
        internal int Length;
    }

    internal static class AudioChunker
    {
        internal const int MaximumChunkSeconds = 30;
        private const int QuietSearchSeconds = 15;
        private const int QuietWindowMilliseconds = 200;
        private const double QuietRmsThreshold = 0.012;

        internal static List<AudioChunk> Split(float[] samples, int sampleRate)
        {
            if (samples == null)
            {
                throw new ArgumentNullException("samples");
            }
            if (sampleRate <= 0)
            {
                throw new ArgumentOutOfRangeException("sampleRate");
            }

            List<AudioChunk> chunks = new List<AudioChunk>();
            int maximum = checked(sampleRate * MaximumChunkSeconds);
            int search = checked(sampleRate * QuietSearchSeconds);
            int window = Math.Max(1, sampleRate * QuietWindowMilliseconds / 1000);
            int offset = 0;

            while (offset < samples.Length)
            {
                int remaining = samples.Length - offset;
                if (remaining <= maximum)
                {
                    chunks.Add(new AudioChunk(offset, remaining));
                    break;
                }

                int hardEnd = offset + maximum;
                int searchStart = Math.Max(offset + window, hardEnd - search);
                int lastWindowStart = hardEnd - window;
                double bestRms;
                int bestWindowStart = FindQuietestWindow(
                    samples,
                    searchStart,
                    lastWindowStart,
                    window,
                    out bestRms);
                int split = bestRms <= QuietRmsThreshold
                    ? bestWindowStart + window
                    : hardEnd;
                if (split <= offset || split > hardEnd)
                {
                    split = hardEnd;
                }

                chunks.Add(new AudioChunk(offset, split - offset));
                offset = split;
            }

            return chunks;
        }

        private static int FindQuietestWindow(
            float[] samples,
            int firstStart,
            int lastStart,
            int window,
            out double bestRms)
        {
            double sumSquares = 0;
            for (int index = firstStart; index < firstStart + window; index++)
            {
                double value = samples[index];
                sumSquares += value * value;
            }

            double bestSum = sumSquares;
            int bestStart = firstStart;
            for (int start = firstStart + 1; start <= lastStart; start++)
            {
                double removed = samples[start - 1];
                double added = samples[start + window - 1];
                sumSquares -= removed * removed;
                sumSquares += added * added;

                // Prefer the later boundary when equally quiet so silence does
                // not create needlessly small chunks.
                if (sumSquares <= bestSum)
                {
                    bestSum = sumSquares;
                    bestStart = start;
                }
            }

            bestRms = Math.Sqrt(Math.Max(0, bestSum) / window);
            return bestStart;
        }
    }
}
