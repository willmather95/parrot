using System;
using System.Collections.Generic;

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

            if (Failures.Count == 0)
            {
                Console.WriteLine("PASS: 9 deterministic core tests");
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
