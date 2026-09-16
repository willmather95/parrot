using System;
using System.Media;
using System.Threading;

namespace Parrot.Windows
{
    internal interface IParrotView
    {
        void SetState(CapturePhase phase, string detail, bool toggleEnabled, bool cancelEnabled);
    }

    internal sealed class OperationLease
    {
        private volatile bool _cancelled;

        internal OperationLease(long generation)
        {
            Generation = generation;
        }

        internal long Generation { get; private set; }
        internal bool IsCancelled { get { return _cancelled; } }
        internal void Cancel() { _cancelled = true; }
    }

    internal sealed class ParrotController : IDisposable
    {
        private const int MaximumRecordingMilliseconds = 10 * 60 * 1000;
        private const int FinishDeadlineMilliseconds = 5 * 60 * 1000;

        private readonly IParrotView _view;
        private readonly SynchronizationContext _uiContext;
        private readonly FocusSecurityGuard _focusGuard;
        private readonly ClipboardWriter _clipboardWriter;
        private readonly CaptureStateMachine _state = new CaptureStateMachine();
        private readonly System.Windows.Forms.Timer _recordingLimitTimer;
        private readonly System.Windows.Forms.Timer _finishDeadlineTimer;
        private readonly ParakeetRecognizer _recognizer = new ParakeetRecognizer();
        private WaveInCaptureSession _capture;
        private OperationLease _operation;
        private bool _modelReady;
        private bool _modelLoading;
        private bool _operationTimedOut;
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
            BeginModelLoad();
        }

        internal CapturePhase Phase { get { return _state.Phase; } }

        internal void Toggle()
        {
            if (_disposed || _restartRequired || _operation != null)
            {
                return;
            }
            if (_state.Phase == CapturePhase.Listening)
            {
                BeginFinishing(false);
                return;
            }
            if (_state.Phase == CapturePhase.Finishing || _modelLoading)
            {
                return;
            }
            if (!_modelReady)
            {
                BeginModelLoad();
                return;
            }
            StartCapture();
        }

        internal void Cancel()
        {
            WaveInCaptureSession capture = _capture;
            OperationLease operation = _operation;
            long generation;
            if (capture != null)
            {
                generation = capture.Generation;
            }
            else if (operation != null)
            {
                generation = operation.Generation;
            }
            else
            {
                return;
            }

            if (!_state.CancelOrTimeout(generation))
            {
                return;
            }
            StopTimers();
            if (operation != null)
            {
                operation.Cancel();
                _view.SetState(
                    CapturePhase.Finishing,
                    "Cancelling local transcription. No transcript will be copied...",
                    false,
                    false);
                return;
            }

            _capture = null;
            OperationLease cleanup = new OperationLease(generation);
            cleanup.Cancel();
            _operation = cleanup;
            _view.SetState(
                CapturePhase.Finishing,
                "Capture cancelled. Releasing the microphone...",
                false,
                false);
            QueueCaptureWork(capture, cleanup, false);
        }

        private void BeginModelLoad()
        {
            if (_disposed || _modelLoading || _modelReady || _operation != null)
            {
                return;
            }
            _modelLoading = true;
            _view.SetState(
                CapturePhase.Ready,
                "Loading the local Parakeet model. Nothing is recording...",
                false,
                false);
            ThreadPool.QueueUserWorkItem(delegate
            {
                Exception error = null;
                try { _recognizer.Initialize(); }
                catch (Exception exception) { error = exception; }
                PostToUi(delegate { CompleteModelLoad(error); });
            });
        }

        private void CompleteModelLoad(Exception error)
        {
            _modelLoading = false;
            if (error != null)
            {
                _modelReady = false;
                _state.MarkBlocked();
                ShowBlocked("Local Parakeet model unavailable: " + SafeMessage(error), true);
                return;
            }
            _modelReady = true;
            ShowReady("Local Parakeet model ready. Nothing is recording.");
        }

        private void StartCapture()
        {
            FocusCheckResult focus = _focusGuard.Check();
            if (!focus.Allowed)
            {
                _state.BlockStart();
                ShowBlocked(focus.Message, true);
                return;
            }

            long generation;
            if (!_state.TryStart(out generation))
            {
                return;
            }
            WaveInCaptureSession capture = null;
            try
            {
                capture = new WaveInCaptureSession(
                    generation,
                    _uiContext,
                    HandleNativeRecordingLimit,
                    HandleCaptureFailure);
                capture.Start();
                _capture = capture;
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
                if (capture != null) { capture.Dispose(); }
                ShowBlocked("Default microphone unavailable: " + SafeMessage(exception), true);
            }
        }

        private void BeginFinishing(bool reachedLimit)
        {
            WaveInCaptureSession capture = _capture;
            if (capture == null || !_state.TryBeginFinishing(capture.Generation))
            {
                return;
            }
            _capture = null;
            _recordingLimitTimer.Stop();
            _finishDeadlineTimer.Start();
            OperationLease operation = new OperationLease(capture.Generation);
            _operation = operation;
            _view.SetState(
                CapturePhase.Finishing,
                reachedLimit
                    ? "Ten-minute limit reached. Transcribing locally..."
                    : "Transcribing locally...",
                false,
                true);
            QueueCaptureWork(capture, operation, true);
        }

        private void QueueCaptureWork(
            WaveInCaptureSession capture,
            OperationLease operation,
            bool transcribe)
        {
            ThreadPool.QueueUserWorkItem(delegate
            {
                Exception error = null;
                ParakeetTranscriptionResult result = null;
                try
                {
                    float[] samples = capture.StopAndGetSamples();
                    capture.Dispose();
                    if (transcribe && !operation.IsCancelled)
                    {
                        result = _recognizer.Transcribe(
                            samples,
                            WaveInCaptureSession.SampleRate,
                            delegate { return operation.IsCancelled; });
                    }
                }
                catch (OperationCanceledException)
                {
                    operation.Cancel();
                }
                catch (Exception exception)
                {
                    error = exception;
                    try { capture.Dispose(); }
                    catch (Exception) { }
                }
                PostToUi(delegate { CompleteCaptureWork(operation, result, error); });
            });
        }

        private void CompleteCaptureWork(
            OperationLease operation,
            ParakeetTranscriptionResult result,
            Exception error)
        {
            if (_operation != operation)
            {
                return;
            }
            _finishDeadlineTimer.Stop();
            _operation = null;

            if (_operationTimedOut)
            {
                _operationTimedOut = false;
                ShowBlocked(
                    "Local transcription recovered after timing out. The capture was discarded; you can try again.",
                    true);
                return;
            }
            if (operation.IsCancelled)
            {
                if (error == null)
                {
                    ShowReady("Capture cancelled. No transcript was copied.");
                }
                else
                {
                    _restartRequired = true;
                    ShowBlocked(
                        "Capture cancelled, but microphone cleanup failed: " + SafeMessage(error),
                        false);
                }
                return;
            }

            CompletionDisposition disposition = _state.Complete(
                operation.Generation,
                error == null && result != null);
            if (disposition == CompletionDisposition.Ignored)
            {
                return;
            }
            if (disposition == CompletionDisposition.Discard || error != null)
            {
                _restartRequired = true;
                ShowBlocked(
                    "Local transcription failed. Transcript discarded: "
                        + (error == null ? "unknown local inference error" : SafeMessage(error)),
                    false);
                return;
            }
            FinishCompletedCapture(result.Text);
        }

        private void FinishCompletedCapture(string transcript)
        {
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
            ShowBlocked(detail, true);
        }

        private void HandleRecordingLimit(object sender, EventArgs eventArgs)
        {
            _recordingLimitTimer.Stop();
            BeginFinishing(true);
        }

        private void HandleNativeRecordingLimit(long generation)
        {
            if (_capture != null && _capture.Generation == generation)
            {
                BeginFinishing(true);
            }
        }

        private void HandleCaptureFailure(long generation, Exception exception)
        {
            WaveInCaptureSession capture = _capture;
            if (capture == null
                || capture.Generation != generation
                || !_state.CancelOrTimeout(generation))
            {
                return;
            }
            StopTimers();
            _capture = null;
            OperationLease cleanup = new OperationLease(generation);
            cleanup.Cancel();
            _operation = cleanup;
            _view.SetState(
                CapturePhase.Blocked,
                "Microphone capture failed. Discarding the capture...",
                false,
                false);
            QueueCaptureWork(capture, cleanup, false);
        }

        private void HandleFinishDeadline(object sender, EventArgs eventArgs)
        {
            _finishDeadlineTimer.Stop();
            OperationLease operation = _operation;
            if (operation == null)
            {
                return;
            }
            operation.Cancel();
            _state.CancelOrTimeout(operation.Generation);
            _operationTimedOut = true;
            _view.SetState(
                CapturePhase.Blocked,
                "Local transcription timed out. Transcript discarded. Wait for recovery or restart Parrot.",
                false,
                false);
        }

        private void PostToUi(Action action)
        {
            try
            {
                _uiContext.Post(
                    delegate(object ignored)
                    {
                        if (!_disposed) { action(); }
                    },
                    null);
            }
            catch (InvalidOperationException) { }
        }

        private void StopTimers()
        {
            _recordingLimitTimer.Stop();
            _finishDeadlineTimer.Stop();
        }

        private void ShowReady(string detail)
        {
            _view.SetState(CapturePhase.Ready, detail, _modelReady, false);
        }

        private void ShowBlocked(string detail, bool allowRetry)
        {
            SystemSounds.Exclamation.Play();
            _view.SetState(
                CapturePhase.Blocked,
                detail,
                allowRetry && !_restartRequired && _operation == null,
                false);
        }

        private static string SafeMessage(Exception exception)
        {
            return String.IsNullOrWhiteSpace(exception.Message)
                ? exception.GetType().Name
                : exception.Message;
        }

        public void Dispose()
        {
            if (_disposed) { return; }
            _disposed = true;
            StopTimers();
            OperationLease operation = _operation;
            if (operation != null) { operation.Cancel(); }
            WaveInCaptureSession capture = _capture;
            _capture = null;
            if (capture != null)
            {
                _state.CancelOrTimeout(capture.Generation);
                ThreadPool.QueueUserWorkItem(delegate
                {
                    try { capture.Dispose(); }
                    catch (Exception) { }
                });
            }
            ThreadPool.QueueUserWorkItem(delegate
            {
                try { _recognizer.Dispose(); }
                catch (Exception) { }
            });
            _recordingLimitTimer.Dispose();
            _finishDeadlineTimer.Dispose();
        }
    }
}
