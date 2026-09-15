using System;
using System.Collections.Generic;
using System.Media;
using System.Speech.Recognition;
using System.Threading;
using System.Windows.Forms;

namespace Parrot.Windows
{
    internal interface IParrotView
    {
        void SetState(CapturePhase phase, string detail, bool toggleEnabled, bool cancelEnabled);
    }

    internal sealed class ParrotController : IDisposable
    {
        private const int MaximumRecordingMilliseconds = 10 * 60 * 1000;
        private const int FinishDeadlineMilliseconds = 5000;
        private const int TeardownDeadlineMilliseconds = 4000;

        private readonly IParrotView _view;
        private readonly SynchronizationContext _uiContext;
        private readonly FocusSecurityGuard _focusGuard;
        private readonly ClipboardWriter _clipboardWriter;
        private readonly CaptureStateMachine _state = new CaptureStateMachine();
        private readonly System.Windows.Forms.Timer _recordingLimitTimer;
        private readonly System.Windows.Forms.Timer _finishDeadlineTimer;
        private readonly System.Windows.Forms.Timer _teardownDeadlineTimer;
        private readonly List<string> _segments = new List<string>();
        private SpeechCaptureSession _session;
        private Action _teardownContinuation;
        private int _teardownToken;
        private bool _teardownInProgress;
        private bool _teardownTimedOut;
        private bool _restartRequired;
        private bool _disposed;

        internal ParrotController(IParrotView view, SynchronizationContext uiContext)
        {
            _view = view;
            _uiContext = uiContext;
            _focusGuard = new FocusSecurityGuard();
            _clipboardWriter = new ClipboardWriter(_focusGuard);

            _recordingLimitTimer = new System.Windows.Forms.Timer();
            _recordingLimitTimer.Interval = MaximumRecordingMilliseconds;
            _recordingLimitTimer.Tick += HandleRecordingLimit;

            _finishDeadlineTimer = new System.Windows.Forms.Timer();
            _finishDeadlineTimer.Interval = FinishDeadlineMilliseconds;
            _finishDeadlineTimer.Tick += HandleFinishDeadline;

            _teardownDeadlineTimer = new System.Windows.Forms.Timer();
            _teardownDeadlineTimer.Interval = TeardownDeadlineMilliseconds;
            _teardownDeadlineTimer.Tick += HandleTeardownDeadline;

            ShowReady("Local Windows speech preview. Nothing is recording.");
        }

        internal CapturePhase Phase
        {
            get { return _state.Phase; }
        }

        internal void Toggle()
        {
            if (_disposed || _teardownInProgress || _restartRequired)
            {
                return;
            }

            if (_state.Phase == CapturePhase.Listening)
            {
                BeginFinishing(false);
                return;
            }

            if (_state.Phase == CapturePhase.Finishing)
            {
                return;
            }

            StartCapture();
        }

        internal void Cancel()
        {
            if (_session == null)
            {
                return;
            }

            long generation = _session.Generation;
            if (!_state.CancelOrTimeout(generation))
            {
                return;
            }

            StopTimers();
            _segments.Clear();
            _view.SetState(
                CapturePhase.Ready,
                "Capture cancelled. Releasing the microphone...",
                false,
                false);
            BeginSessionTeardown(
                true,
                delegate { ShowReady("Capture cancelled. No transcript was copied."); });
        }

        private void StartCapture()
        {
            FocusCheckResult focus = _focusGuard.Check();
            if (!focus.Allowed)
            {
                _state.BlockStart();
                ShowBlocked(focus.Message);
                return;
            }

            RecognizerInfo recognizer;
            try
            {
                recognizer = SpeechEngineFactory.FindEnglishRecognizer();
            }
            catch (Exception exception)
            {
                _state.BlockStart();
                ShowBlocked("Windows speech engine unavailable: " + SafeMessage(exception));
                return;
            }

            if (recognizer == null)
            {
                _state.BlockStart();
                ShowBlocked("No installed English Windows speech recognizer was found.");
                return;
            }

            long generation;
            if (!_state.TryStart(out generation))
            {
                return;
            }

            _segments.Clear();
            try
            {
                _session = new SpeechCaptureSession(
                    generation,
                    recognizer,
                    _uiContext,
                    HandleRecognized,
                    HandleCompleted);
                _session.Start();
                _recordingLimitTimer.Start();
                _view.SetState(
                    CapturePhase.Listening,
                    "Listening. Press Ctrl+Alt+Space or Stop when finished.",
                    true,
                    true);
            }
            catch (Exception exception)
            {
                _state.CancelOrTimeout(generation);
                _segments.Clear();
                string detail = "Microphone or local speech engine unavailable: "
                    + SafeMessage(exception);
                if (_session == null)
                {
                    ShowBlocked(detail);
                }
                else
                {
                    _view.SetState(
                        CapturePhase.Blocked,
                        detail + " Releasing the microphone...",
                        false,
                        false);
                    BeginSessionTeardown(true, delegate { ShowBlocked(detail); });
                }
            }
        }

        private void BeginFinishing(bool reachedLimit)
        {
            if (_session == null || !_state.TryBeginFinishing(_session.Generation))
            {
                return;
            }

            _recordingLimitTimer.Stop();
            _finishDeadlineTimer.Start();
            _view.SetState(
                CapturePhase.Finishing,
                reachedLimit
                    ? "Ten-minute limit reached. Finishing locally..."
                    : "Finishing locally...",
                false,
                true);

            try
            {
                _session.RequestStop();
            }
            catch (Exception exception)
            {
                long generation = _session.Generation;
                _state.CancelOrTimeout(generation);
                StopTimers();
                _segments.Clear();
                string detail = "Speech recognition could not stop cleanly: "
                    + SafeMessage(exception);
                _view.SetState(
                    CapturePhase.Blocked,
                    detail + " Releasing the microphone...",
                    false,
                    false);
                BeginSessionTeardown(true, delegate { ShowBlocked(detail); });
            }
        }

        private void HandleRecognized(long generation, string text)
        {
            if (_session == null
                || generation != _session.Generation
                || generation != _state.ActiveGeneration
                || (_state.Phase != CapturePhase.Listening
                    && _state.Phase != CapturePhase.Finishing))
            {
                return;
            }

            if (!String.IsNullOrWhiteSpace(text))
            {
                _segments.Add(text.Trim());
            }
        }

        private void HandleCompleted(long generation, Exception error, bool cancelled)
        {
            if (_session == null || generation != _session.Generation)
            {
                return;
            }

            CompletionDisposition disposition = _state.Complete(
                generation,
                error == null && !cancelled);
            if (disposition == CompletionDisposition.Ignored)
            {
                return;
            }

            StopTimers();
            string transcript = String.Join(" ", _segments.ToArray()).Trim();
            _segments.Clear();
            _view.SetState(
                CapturePhase.Finishing,
                "Releasing the microphone...",
                false,
                false);
            BeginSessionTeardown(
                false,
                delegate { FinishCompletedCapture(disposition, transcript); });
        }

        private void FinishCompletedCapture(
            CompletionDisposition disposition,
            string transcript)
        {
            if (disposition == CompletionDisposition.Discard)
            {
                ShowBlocked("Recognition did not complete cleanly. Transcript discarded.");
                return;
            }

            if (String.IsNullOrWhiteSpace(transcript))
            {
                ShowReady("No speech was recognized.");
                return;
            }

            string detail;
            ClipboardWriteResult result = _clipboardWriter.TryWrite(transcript, out detail);
            if (result == ClipboardWriteResult.Copied)
            {
                ShowReady(detail);
                return;
            }

            ShowBlocked(detail);
        }

        private void HandleRecordingLimit(object sender, EventArgs eventArgs)
        {
            _recordingLimitTimer.Stop();
            BeginFinishing(true);
        }

        private void HandleFinishDeadline(object sender, EventArgs eventArgs)
        {
            _finishDeadlineTimer.Stop();
            if (_session == null)
            {
                return;
            }

            long generation = _session.Generation;
            if (!_state.CancelOrTimeout(generation))
            {
                return;
            }

            _segments.Clear();
            _view.SetState(
                CapturePhase.Blocked,
                "Speech recognition timed out. Transcript discarded. Releasing the microphone...",
                false,
                false);
            BeginSessionTeardown(
                true,
                delegate
                {
                    ShowBlocked("Speech recognition timed out. Transcript discarded.");
                });
        }

        private void BeginSessionTeardown(bool cancel, Action continuation)
        {
            SpeechCaptureSession session = _session;
            _session = null;
            if (session == null)
            {
                continuation();
                return;
            }

            _teardownInProgress = true;
            _teardownTimedOut = false;
            _teardownContinuation = continuation;
            _teardownToken++;
            int token = _teardownToken;
            _teardownDeadlineTimer.Start();

            ThreadPool.QueueUserWorkItem(delegate
            {
                Exception error = null;
                try
                {
                    if (cancel)
                    {
                        session.RequestCancel();
                    }

                    session.Dispose();
                }
                catch (Exception exception)
                {
                    error = exception;
                }

                PostToUi(delegate { CompleteSessionTeardown(token, error); });
            });
        }

        private void HandleTeardownDeadline(object sender, EventArgs eventArgs)
        {
            _teardownDeadlineTimer.Stop();
            if (!_teardownInProgress)
            {
                return;
            }

            _teardownTimedOut = true;
            _teardownContinuation = null;
            _view.SetState(
                CapturePhase.Blocked,
                "Microphone cleanup timed out. Capture discarded. Wait for recovery or restart Parrot.",
                false,
                false);
        }

        private void CompleteSessionTeardown(int token, Exception error)
        {
            if (_disposed || token != _teardownToken || !_teardownInProgress)
            {
                return;
            }

            _teardownDeadlineTimer.Stop();
            _teardownInProgress = false;
            Action continuation = _teardownContinuation;
            _teardownContinuation = null;

            if (error != null)
            {
                _restartRequired = true;
                _view.SetState(
                    CapturePhase.Blocked,
                    "Microphone cleanup failed. Restart Parrot before recording again.",
                    false,
                    false);
                return;
            }

            if (_teardownTimedOut)
            {
                _teardownTimedOut = false;
                ShowBlocked("Microphone cleanup recovered. The capture was discarded; you can try again.");
                return;
            }

            if (continuation != null)
            {
                continuation();
            }
        }

        private void PostToUi(Action action)
        {
            try
            {
                _uiContext.Post(
                    delegate(object ignored)
                    {
                        if (!_disposed)
                        {
                            action();
                        }
                    },
                    null);
            }
            catch (InvalidOperationException)
            {
                // The application is already closing.
            }
        }

        private void StopTimers()
        {
            _recordingLimitTimer.Stop();
            _finishDeadlineTimer.Stop();
        }

        private void ShowReady(string detail)
        {
            _view.SetState(CapturePhase.Ready, detail, true, false);
        }

        private void ShowBlocked(string detail)
        {
            SystemSounds.Exclamation.Play();
            _view.SetState(CapturePhase.Blocked, detail, true, false);
        }

        private static string SafeMessage(Exception exception)
        {
            return String.IsNullOrWhiteSpace(exception.Message)
                ? exception.GetType().Name
                : exception.Message;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            StopTimers();
            _teardownDeadlineTimer.Stop();
            _teardownContinuation = null;
            SpeechCaptureSession session = _session;
            _session = null;
            if (session != null)
            {
                _state.CancelOrTimeout(session.Generation);
                ThreadPool.QueueUserWorkItem(delegate
                {
                    try
                    {
                        session.RequestCancel();
                        session.Dispose();
                    }
                    catch (Exception)
                    {
                    }
                });
            }

            _segments.Clear();
            _recordingLimitTimer.Dispose();
            _finishDeadlineTimer.Dispose();
            _teardownDeadlineTimer.Dispose();
        }
    }
}
