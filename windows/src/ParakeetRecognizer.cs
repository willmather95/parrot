using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using SherpaOnnx;

namespace Parrot.Windows
{
    internal sealed class ParakeetTranscriptionResult
    {
        internal string Text;
        internal double DecodeSeconds;
        internal int ChunkCount;
    }

    internal sealed class ParakeetRecognizer : IDisposable
    {
        internal const string RuntimeVersion = "1.13.8";
        internal const string ModelId = "parakeet-tdt-0.6b-v2-int8";
        internal const string ModelRevision = "1ab9323565ddb038682214b292f588070a538ce2";

        private readonly object _lock = new object();
        private OfflineRecognizer _recognizer;
        private bool _disposed;

        internal bool IsReady
        {
            get
            {
                lock (_lock)
                {
                    return _recognizer != null && !_disposed;
                }
            }
        }

        internal double Initialize()
        {
            lock (_lock)
            {
                ThrowIfDisposed();
                if (_recognizer != null)
                {
                    return 0;
                }

                ValidatePackageFiles();
                string nativeVersion = VersionInfo.Version;
                if (!String.Equals(nativeVersion, RuntimeVersion, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        "Expected sherpa-onnx " + RuntimeVersion + " but loaded "
                        + (String.IsNullOrWhiteSpace(nativeVersion) ? "an unknown version" : nativeVersion)
                        + ".");
                }

                Stopwatch stopwatch = Stopwatch.StartNew();
                OfflineRecognizerConfig config = CreateConfig();
                try
                {
                    _recognizer = new OfflineRecognizer(config);

                    // Exercise the same native inference path with private
                    // generated audio before advertising readiness.
                    float[] warmup = new float[WaveInCaptureSession.SampleRate];
                    for (int index = 0; index < warmup.Length; index++)
                    {
                        warmup[index] = (float)(
                            Math.Sin(2 * Math.PI * 220 * index / WaveInCaptureSession.SampleRate)
                            * 0.002);
                    }
                    DecodeChunk(warmup, WaveInCaptureSession.SampleRate);
                }
                catch
                {
                    if (_recognizer != null)
                    {
                        _recognizer.Dispose();
                        _recognizer = null;
                    }
                    throw;
                }
                stopwatch.Stop();
                return stopwatch.Elapsed.TotalSeconds;
            }
        }

        internal ParakeetTranscriptionResult Transcribe(
            float[] samples,
            int sampleRate,
            Func<bool> isCancelled)
        {
            if (samples == null)
            {
                throw new ArgumentNullException("samples");
            }
            if (sampleRate != WaveInCaptureSession.SampleRate)
            {
                throw new InvalidOperationException("Parrot requires 16 kHz mono audio.");
            }

            lock (_lock)
            {
                ThrowIfDisposed();
                if (_recognizer == null)
                {
                    throw new InvalidOperationException("The Parakeet model is not initialized.");
                }

                List<AudioChunk> chunks = AudioChunker.Split(samples, sampleRate);
                List<string> transcripts = new List<string>();
                Stopwatch stopwatch = Stopwatch.StartNew();
                foreach (AudioChunk chunk in chunks)
                {
                    ThrowIfCancelled(isCancelled);
                    float[] audio = new float[chunk.Length];
                    Array.Copy(samples, chunk.Offset, audio, 0, chunk.Length);
                    string text = DecodeChunk(audio, sampleRate).Trim();
                    if (!String.IsNullOrWhiteSpace(text))
                    {
                        transcripts.Add(text);
                    }
                }
                ThrowIfCancelled(isCancelled);
                stopwatch.Stop();

                ParakeetTranscriptionResult result = new ParakeetTranscriptionResult();
                result.Text = String.Join(" ", transcripts.ToArray()).Trim();
                result.DecodeSeconds = stopwatch.Elapsed.TotalSeconds;
                result.ChunkCount = chunks.Count;
                return result;
            }
        }

        internal static string ModelDirectory
        {
            get
            {
                return Path.Combine(
                    AppDomain.CurrentDomain.BaseDirectory,
                    "models",
                    ModelId);
            }
        }

        internal static void ValidatePackageFiles()
        {
            string root = AppDomain.CurrentDomain.BaseDirectory;
            RequireFile(Path.Combine(root, "sherpa-onnx.dll"));
            RequireFile(Path.Combine(root, "sherpa-onnx-c-api.dll"));
            RequireFile(Path.Combine(root, "onnxruntime.dll"));
            RequireFile(Path.Combine(ModelDirectory, "encoder.int8.onnx"));
            RequireFile(Path.Combine(ModelDirectory, "decoder.int8.onnx"));
            RequireFile(Path.Combine(ModelDirectory, "joiner.int8.onnx"));
            RequireFile(Path.Combine(ModelDirectory, "tokens.txt"));
        }

        private static OfflineRecognizerConfig CreateConfig()
        {
            OfflineRecognizerConfig config = new OfflineRecognizerConfig();
            config.FeatConfig.SampleRate = WaveInCaptureSession.SampleRate;
            config.FeatConfig.FeatureDim = 80;
            config.ModelConfig.Transducer.Encoder = Path.Combine(ModelDirectory, "encoder.int8.onnx");
            config.ModelConfig.Transducer.Decoder = Path.Combine(ModelDirectory, "decoder.int8.onnx");
            config.ModelConfig.Transducer.Joiner = Path.Combine(ModelDirectory, "joiner.int8.onnx");
            config.ModelConfig.Tokens = Path.Combine(ModelDirectory, "tokens.txt");
            config.ModelConfig.NumThreads = 2;
            config.ModelConfig.Debug = 0;
            config.ModelConfig.Provider = "cpu";
            config.ModelConfig.ModelType = "nemo_transducer";
            config.ModelConfig.ModelingUnit = "cjkchar";
            config.ModelConfig.BpeVocab = String.Empty;
            config.ModelConfig.TeleSpeechCtc = String.Empty;
            config.DecodingMethod = "greedy_search";
            config.MaxActivePaths = 4;
            config.HotwordsFile = String.Empty;
            config.HotwordsScore = 1.5F;
            config.RuleFsts = String.Empty;
            config.RuleFars = String.Empty;
            config.BlankPenalty = 0;
            return config;
        }

        private string DecodeChunk(float[] samples, int sampleRate)
        {
            using (OfflineStream stream = _recognizer.CreateStream())
            {
                stream.AcceptWaveform(sampleRate, samples);
                _recognizer.Decode(stream);
                OfflineRecognizerResult result = stream.Result;
                return result == null || result.Text == null ? String.Empty : result.Text;
            }
        }

        private static void ThrowIfCancelled(Func<bool> isCancelled)
        {
            if (isCancelled != null && isCancelled())
            {
                throw new OperationCanceledException("Transcription was cancelled.");
            }
        }

        private static void RequireFile(string path)
        {
            if (!File.Exists(path))
            {
                throw new FileNotFoundException(
                    "Required local inference file is missing: " + Path.GetFileName(path),
                    path);
            }
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException("ParakeetRecognizer");
            }
        }

        public void Dispose()
        {
            lock (_lock)
            {
                if (_disposed)
                {
                    return;
                }

                _disposed = true;
                if (_recognizer != null)
                {
                    _recognizer.Dispose();
                    _recognizer = null;
                }
            }
        }
    }
}
