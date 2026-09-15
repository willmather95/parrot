using System;

namespace Parrot.Windows
{
    internal enum CapturePhase
    {
        Ready,
        Listening,
        Finishing,
        Blocked
    }

    internal enum CompletionDisposition
    {
        Ignored,
        Deliver,
        Discard
    }

    // This class contains no speech or UI code so the safety-critical session
    // transitions can be tested deterministically on every platform.
    internal sealed class CaptureStateMachine
    {
        private CapturePhase _phase = CapturePhase.Ready;
        private long _generation;

        internal CapturePhase Phase
        {
            get { return _phase; }
        }

        internal long ActiveGeneration
        {
            get { return _generation; }
        }

        internal bool TryStart(out long generation)
        {
            generation = 0;
            if (_phase != CapturePhase.Ready && _phase != CapturePhase.Blocked)
            {
                return false;
            }

            _generation++;
            generation = _generation;
            _phase = CapturePhase.Listening;
            return true;
        }

        internal void BlockStart()
        {
            _generation++;
            _phase = CapturePhase.Blocked;
        }

        internal bool TryBeginFinishing(long generation)
        {
            if (generation != _generation || _phase != CapturePhase.Listening)
            {
                return false;
            }

            _phase = CapturePhase.Finishing;
            return true;
        }

        internal CompletionDisposition Complete(long generation, bool cleanCompletion)
        {
            if (generation != _generation)
            {
                return CompletionDisposition.Ignored;
            }

            // System.Speech can end on a device or engine error before Parrot
            // asks it to stop. A current completion while Listening is real,
            // but it is never publishable because the normal stop boundary was
            // not reached.
            if (_phase == CapturePhase.Listening)
            {
                _generation++;
                _phase = CapturePhase.Ready;
                return CompletionDisposition.Discard;
            }

            if (_phase != CapturePhase.Finishing)
            {
                return CompletionDisposition.Ignored;
            }

            _generation++;
            _phase = CapturePhase.Ready;
            return cleanCompletion
                ? CompletionDisposition.Deliver
                : CompletionDisposition.Discard;
        }

        internal bool CancelOrTimeout(long generation)
        {
            if (generation != _generation
                || (_phase != CapturePhase.Listening && _phase != CapturePhase.Finishing))
            {
                return false;
            }

            _generation++;
            _phase = CapturePhase.Ready;
            return true;
        }

        internal void MarkBlocked()
        {
            _generation++;
            _phase = CapturePhase.Blocked;
        }
    }
}
