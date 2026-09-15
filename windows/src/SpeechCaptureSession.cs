using System;
using System.Collections.Generic;
using System.Speech.Recognition;
using System.Threading;

namespace Parrot.Windows
{
    internal static class SpeechEngineFactory
    {
        internal static RecognizerInfo FindEnglishRecognizer()
        {
            IList<RecognizerInfo> recognizers = SpeechRecognitionEngine.InstalledRecognizers();
            RecognizerInfo firstEnglish = null;

            foreach (RecognizerInfo recognizer in recognizers)
            {
                if (!String.Equals(
                    recognizer.Culture.TwoLetterISOLanguageName,
                    "en",
                    StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (String.Equals(
                    recognizer.Culture.Name,
                    "en-US",
                    StringComparison.OrdinalIgnoreCase))
                {
                    return recognizer;
                }

                if (firstEnglish == null)
                {
                    firstEnglish = recognizer;
                }
            }

            return firstEnglish;
        }
    }

    internal sealed class SpeechCaptureSession : IDisposable
    {
        private readonly SpeechRecognitionEngine _engine;
        private readonly SynchronizationContext _uiContext;
        private readonly Action<long, string> _recognized;
        private readonly Action<long, Exception, bool> _completed;
        private bool _disposed;

        internal SpeechCaptureSession(
            long generation,
            RecognizerInfo recognizer,
            SynchronizationContext uiContext,
            Action<long, string> recognized,
            Action<long, Exception, bool> completed)
        {
            if (recognizer == null)
            {
                throw new InvalidOperationException("No installed English speech recognizer was found.");
            }

            Generation = generation;
            _uiContext = uiContext;
            _recognized = recognized;
            _completed = completed;
            SpeechRecognitionEngine engine = new SpeechRecognitionEngine(recognizer);
            try
            {
                engine.LoadGrammar(new DictationGrammar());
                engine.SetInputToDefaultAudioDevice();
            }
            catch
            {
                engine.Dispose();
                throw;
            }

            _engine = engine;
            _engine.SpeechRecognized += HandleSpeechRecognized;
            _engine.RecognizeCompleted += HandleRecognizeCompleted;
        }

        internal long Generation { get; private set; }

        internal void Start()
        {
            ThrowIfDisposed();
            _engine.RecognizeAsync(RecognizeMode.Multiple);
        }

        internal void RequestStop()
        {
            ThrowIfDisposed();
            _engine.RecognizeAsyncStop();
        }

        internal void RequestCancel()
        {
            if (_disposed)
            {
                return;
            }

            try
            {
                _engine.RecognizeAsyncCancel();
            }
            catch (InvalidOperationException)
            {
                // Recognition may already have completed between state change
                // and cancellation. The generation check still rejects it.
            }
        }

        private void HandleSpeechRecognized(object sender, SpeechRecognizedEventArgs eventArgs)
        {
            string text = eventArgs.Result == null ? null : eventArgs.Result.Text;
            if (String.IsNullOrWhiteSpace(text))
            {
                return;
            }

            Post(delegate
            {
                _recognized(Generation, text.Trim());
            });
        }

        private void HandleRecognizeCompleted(object sender, RecognizeCompletedEventArgs eventArgs)
        {
            Exception error = eventArgs.Error;
            bool cancelled = eventArgs.Cancelled;
            Post(delegate
            {
                _completed(Generation, error, cancelled);
            });
        }

        private void Post(Action action)
        {
            _uiContext.Post(delegate(object ignored) { action(); }, null);
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException("SpeechCaptureSession");
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _engine.SpeechRecognized -= HandleSpeechRecognized;
            _engine.RecognizeCompleted -= HandleRecognizeCompleted;
            _engine.Dispose();
        }
    }
}
