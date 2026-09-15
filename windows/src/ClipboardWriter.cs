using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace Parrot.Windows
{
    internal enum ClipboardWriteResult
    {
        Copied,
        Blocked,
        Failed
    }

    internal sealed class ClipboardWriter
    {
        private readonly FocusSecurityGuard _focusGuard;

        internal ClipboardWriter(FocusSecurityGuard focusGuard)
        {
            _focusGuard = focusGuard;
        }

        internal ClipboardWriteResult TryWrite(string text, out string detail)
        {
            detail = String.Empty;
            if (String.IsNullOrWhiteSpace(text))
            {
                detail = "No speech was recognized.";
                return ClipboardWriteResult.Failed;
            }

            const int attempts = 5;
            for (int attempt = 0; attempt < attempts; attempt++)
            {
                if (attempt > 0)
                {
                    Thread.Sleep(60);
                }

                // This is deliberately adjacent to SetText. Focus can change
                // while recognition finishes, so start-time approval is never
                // reused for delivery.
                FocusCheckResult focus = _focusGuard.Check();
                if (!focus.Allowed)
                {
                    detail = focus.Message + " Transcript discarded.";
                    return ClipboardWriteResult.Blocked;
                }

                try
                {
                    Clipboard.SetText(text, TextDataFormat.UnicodeText);
                    detail = "Copied to clipboard. Press Ctrl+V to paste.";
                    return ClipboardWriteResult.Copied;
                }
                catch (ExternalException)
                {
                    // Another process can briefly own the clipboard. Retry for
                    // a bounded interval without clearing existing contents.
                }
            }

            detail = "Clipboard was busy. Transcript discarded.";
            return ClipboardWriteResult.Failed;
        }
    }
}
