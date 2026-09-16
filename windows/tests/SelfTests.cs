using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Parrot.Windows
{
    internal static class SelfTests
    {
        private static readonly List<string> Failures = new List<string>();

        internal static int Run()
        {
            RunTest("normal capture delivers only after finishing", NormalCaptureDeliversOnlyAfterFinishing);
            RunTest("cancel invalidates late completion", CancelInvalidatesLateCompletion);
            RunTest("completion error discards capture", CompletionErrorDiscardsCapture);
            RunTest("unexpected completion while listening discards", UnexpectedCompletionWhileListeningDiscards);
            RunTest("timeout invalidates late completion and permits retry", TimeoutInvalidatesLateCompletionAndPermitsRetry);
            RunTest("blocked start can be retried", BlockedStartCanBeRetried);
            RunTest("overlapping start is rejected", OverlappingStartIsRejected);
            RunTest("foreground identity changes fail closed", ForegroundIdentityChangesFailClosed);
            RunTest("focused control identity changes fail closed", FocusedControlIdentityChangesFailClosed);
            RunTest("chunking covers audio without overlap", ChunkingCoversAudioWithoutOverlap);
            RunTest("chunking uses a natural quiet boundary", ChunkingUsesNaturalQuietBoundary);
            RunTest("empty audio produces no chunks", EmptyAudioProducesNoChunks);
            RunTest("cancel lease remains cancelled", CancelLeaseRemainsCancelled);
            RunTest("wave parser accepts required fixture format", WaveParserAcceptsRequiredFixtureFormat);
            RunTest("benchmark JSON escapes transcript text", BenchmarkJsonEscapesTranscriptText);
            RunTest("benchmark CLI requires both distinct paths", BenchmarkCliRequiresBothDistinctPaths);
            RunTest("capture buffers preserve ring chronology", CaptureBuffersPreserveRingChronology);

            if (Failures.Count == 0)
            {
                Console.WriteLine("PASS: 17 deterministic core tests");
                return 0;
            }

            foreach (string failure in Failures)
            {
                Console.WriteLine("FAIL: " + failure);
            }

            return 1;
        }

        private static void NormalCaptureDeliversOnlyAfterFinishing()
        {
            CaptureStateMachine state = new CaptureStateMachine();
            long generation;
            Assert(state.TryStart(out generation), "start should succeed");
            Assert(state.Phase == CapturePhase.Listening, "state should be Listening");
            Assert(state.TryBeginFinishing(generation), "stop should enter Finishing");
            Assert(
                state.Complete(generation, true) == CompletionDisposition.Deliver,
                "clean completion should deliver");
            Assert(state.Phase == CapturePhase.Ready, "delivery should return to Ready");
        }

        private static void CancelInvalidatesLateCompletion()
        {
            CaptureStateMachine state = new CaptureStateMachine();
            long generation;
            Assert(state.TryStart(out generation), "start should succeed");
            Assert(state.CancelOrTimeout(generation), "cancel should succeed");
            Assert(
                state.Complete(generation, true) == CompletionDisposition.Ignored,
                "late completion after cancel must be ignored");
            Assert(state.Phase == CapturePhase.Ready, "cancel should return to Ready");
        }

        private static void CompletionErrorDiscardsCapture()
        {
            CaptureStateMachine state = new CaptureStateMachine();
            long generation;
            Assert(state.TryStart(out generation), "start should succeed");
            Assert(state.TryBeginFinishing(generation), "stop should enter Finishing");
            Assert(
                state.Complete(generation, false) == CompletionDisposition.Discard,
                "unclean completion must discard");
            Assert(state.Phase == CapturePhase.Ready, "discard should return to Ready");
        }

        private static void UnexpectedCompletionWhileListeningDiscards()
        {
            CaptureStateMachine state = new CaptureStateMachine();
            long generation;
            Assert(state.TryStart(out generation), "start should succeed");
            Assert(
                state.Complete(generation, true) == CompletionDisposition.Discard,
                "completion before a requested stop must discard");
            Assert(state.Phase == CapturePhase.Ready, "discard should return to Ready");
        }

        private static void TimeoutInvalidatesLateCompletionAndPermitsRetry()
        {
            CaptureStateMachine state = new CaptureStateMachine();
            long first;
            Assert(state.TryStart(out first), "first start should succeed");
            Assert(state.TryBeginFinishing(first), "first stop should succeed");
            Assert(state.CancelOrTimeout(first), "timeout should invalidate first capture");

            long second;
            Assert(state.TryStart(out second), "second start should succeed");
            Assert(second != first, "new capture needs a new generation");
            Assert(
                state.Complete(first, true) == CompletionDisposition.Ignored,
                "old completion must not affect a new capture");
            Assert(state.Phase == CapturePhase.Listening, "new capture must remain Listening");
        }

        private static void BlockedStartCanBeRetried()
        {
            CaptureStateMachine state = new CaptureStateMachine();
            state.BlockStart();
            Assert(state.Phase == CapturePhase.Blocked, "state should be Blocked");
            long generation;
            Assert(state.TryStart(out generation), "user retry should start a new capture");
            Assert(state.Phase == CapturePhase.Listening, "retry should be Listening");
        }

        private static void OverlappingStartIsRejected()
        {
            CaptureStateMachine state = new CaptureStateMachine();
            long first;
            long second;
            Assert(state.TryStart(out first), "first start should succeed");
            Assert(!state.TryStart(out second), "second start must be rejected");
            Assert(state.ActiveGeneration == first, "active generation must not change");
        }

        private static void ForegroundIdentityChangesFailClosed()
        {
            IntPtr firstWindow = new IntPtr(100);
            Assert(
                FocusSecurityGuard.SameForegroundIdentity(
                    firstWindow,
                    10,
                    firstWindow,
                    10),
                "unchanged foreground identity should match");
            Assert(
                !FocusSecurityGuard.SameForegroundIdentity(
                    firstWindow,
                    10,
                    new IntPtr(101),
                    10),
                "changed foreground window must fail closed");
            Assert(
                !FocusSecurityGuard.SameForegroundIdentity(
                    firstWindow,
                    10,
                    firstWindow,
                    11),
                "changed foreground process must fail closed");
        }

        private static void FocusedControlIdentityChangesFailClosed()
        {
            Assert(
                FocusSecurityGuard.SameRuntimeId(
                    new int[] { 1, 7, 42 },
                    new int[] { 1, 7, 42 }),
                "unchanged runtime IDs should match");
            Assert(
                !FocusSecurityGuard.SameRuntimeId(
                    new int[] { 1, 7, 42 },
                    new int[] { 1, 7, 43 }),
                "a changed control runtime ID must fail closed");
            Assert(
                !FocusSecurityGuard.SameRuntimeId(null, new int[] { 1 }),
                "unknown control identity must fail closed");
        }

        private static void ChunkingCoversAudioWithoutOverlap()
        {
            int rate = 10;
            float[] samples = new float[65 * rate];
            for (int index = 0; index < samples.Length; index++) { samples[index] = 0.2F; }
            List<AudioChunk> chunks = AudioChunker.Split(samples, rate);
            int expectedOffset = 0;
            foreach (AudioChunk chunk in chunks)
            {
                Assert(chunk.Offset == expectedOffset, "chunks must be contiguous");
                Assert(chunk.Length > 0, "chunks must not be empty");
                Assert(
                    chunk.Length <= AudioChunker.MaximumChunkSeconds * rate,
                    "chunk exceeded the hard duration bound");
                expectedOffset += chunk.Length;
            }
            Assert(expectedOffset == samples.Length, "chunks must cover every sample once");
        }

        private static void ChunkingUsesNaturalQuietBoundary()
        {
            int rate = 1000;
            float[] samples = new float[53 * rate];
            for (int index = 0; index < samples.Length; index++) { samples[index] = 0.1F; }
            for (int index = 16800; index < 17400; index++) { samples[index] = 0F; }
            List<AudioChunk> chunks = AudioChunker.Split(samples, rate);
            Assert(chunks.Count >= 2, "long audio should be split");
            int firstEnd = chunks[0].Offset + chunks[0].Length;
            Assert(
                firstEnd >= 17000 && firstEnd <= 17600,
                "first split should use the quiet boundary in the search window");
        }

        private static void EmptyAudioProducesNoChunks()
        {
            Assert(
                AudioChunker.Split(new float[0], WaveInCaptureSession.SampleRate).Count == 0,
                "empty audio should not create a decoder stream");
        }

        private static void CancelLeaseRemainsCancelled()
        {
            OperationLease lease = new OperationLease(42);
            Assert(!lease.IsCancelled, "new operation should be live");
            lease.Cancel();
            Assert(lease.IsCancelled, "cancel must be visible to the decode worker");
            lease.Cancel();
            Assert(lease.IsCancelled, "cancel must be idempotent");
        }

        private static void WaveParserAcceptsRequiredFixtureFormat()
        {
            byte[] pcm = new byte[] { 0, 0, 0xFF, 0x7F, 0, 0x80 };
            using (MemoryStream stream = CreateWaveFixture(pcm))
            {
                WavAudio audio = WavFile.Read(stream);
                Assert(audio.SampleRate == 16000, "sample rate should be parsed");
                Assert(audio.Samples.Length == 3, "all PCM samples should be parsed");
                Assert(audio.Samples[1] > 0.99F, "positive PCM amplitude should be normalized");
                Assert(audio.Samples[2] == -1F, "negative PCM amplitude should be normalized");
            }
        }

        private static MemoryStream CreateWaveFixture(byte[] pcm)
        {
            MemoryStream stream = new MemoryStream();
            BinaryWriter writer = new BinaryWriter(stream, Encoding.ASCII);
            writer.Write(Encoding.ASCII.GetBytes("RIFF"));
            writer.Write((uint)(36 + pcm.Length));
            writer.Write(Encoding.ASCII.GetBytes("WAVEfmt "));
            writer.Write((uint)16);
            writer.Write((ushort)1);
            writer.Write((ushort)1);
            writer.Write((uint)16000);
            writer.Write((uint)32000);
            writer.Write((ushort)2);
            writer.Write((ushort)16);
            writer.Write(Encoding.ASCII.GetBytes("data"));
            writer.Write((uint)pcm.Length);
            writer.Write(pcm);
            writer.Flush();
            stream.Position = 0;
            return stream;
        }

        private static void BenchmarkJsonEscapesTranscriptText()
        {
            string json = BenchmarkJson.Create("say \"hi\"\\next\nline", 1, 2, 3, 1, 4);
            Assert(
                json.IndexOf("say \\\"hi\\\"\\\\next\\nline", StringComparison.Ordinal) >= 0,
                "transcript must be JSON escaped");
            Assert(
                json.IndexOf("\"peak_working_set_bytes\":4", StringComparison.Ordinal) >= 0,
                "memory metric must be emitted as a number");
        }

        private static void BenchmarkCliRequiresBothDistinctPaths()
        {
            TranscribeFileOptions options;
            Assert(
                TranscribeFileOptions.TryParse(
                    new string[] { "--transcribe-file", "input.wav", "--output-json", "result.json" },
                    out options),
                "valid benchmark arguments should parse");
            Assert(
                !TranscribeFileOptions.TryParse(
                    new string[] { "--transcribe-file", "same.wav", "--output-json", "same.wav" },
                    out options),
                "benchmark output must not overwrite its input");
        }

        private static void CaptureBuffersPreserveRingChronology()
        {
            CaptureBufferOrder order = new CaptureBufferOrder(8, 4);
            bool[] completed = new bool[] { true, true, false, false, true, true, false, false };
            List<int> observed = new List<int>();
            int index;
            while (order.TryTake(completed, out index)) { observed.Add(index); }
            completed[6] = true;
            completed[7] = true;
            while (order.TryTake(completed, out index)) { observed.Add(index); }
            Assert(
                String.Join(",", observed.ConvertAll<string>(delegate(int value) { return value.ToString(); }).ToArray())
                    == "4,5,6,7,0,1",
                "coalesced callbacks across ring wrap must remain chronological");
        }

        private static void RunTest(string name, Action test)
        {
            try
            {
                test();
            }
            catch (Exception exception)
            {
                Failures.Add(name + ": " + exception.Message);
            }
        }

        private static void Assert(bool condition, string message)
        {
            if (!condition)
            {
                throw new InvalidOperationException(message);
            }
        }
    }
}
