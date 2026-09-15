using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Automation;

namespace Parrot.Windows
{
    internal enum FocusBlockReason
    {
        None,
        Password,
        Elevated,
        Unavailable
    }

    internal sealed class FocusCheckResult
    {
        private FocusCheckResult(bool allowed, FocusBlockReason reason, string message)
        {
            Allowed = allowed;
            Reason = reason;
            Message = message;
        }

        internal bool Allowed { get; private set; }
        internal FocusBlockReason Reason { get; private set; }
        internal string Message { get; private set; }

        internal static FocusCheckResult Allow()
        {
            return new FocusCheckResult(true, FocusBlockReason.None, String.Empty);
        }

        internal static FocusCheckResult Block(FocusBlockReason reason, string message)
        {
            return new FocusCheckResult(false, reason, message);
        }
    }

    internal sealed class FocusSecurityGuard
    {
        private readonly object _checkLock = new object();
        private Task<FocusCheckResult> _activeCheck;

        internal FocusCheckResult Check()
        {
            // Cross-process UI Automation providers are outside Parrot's
            // control. Run the inspection away from the message loop and fail
            // closed on a bounded wait so a hung provider cannot freeze the UI
            // indefinitely. A timed-out worker has no delivery side effects.
            Task<FocusCheckResult> check;
            lock (_checkLock)
            {
                if (_activeCheck != null)
                {
                    if (!_activeCheck.IsCompleted)
                    {
                        return FocusCheckResult.Block(
                            FocusBlockReason.Unavailable,
                            "A previous Windows focus inspection is still unavailable.");
                    }

                    // The prior request already timed out. Discard its late
                    // result because focus approval is never reusable.
                    _activeCheck = null;
                }

                check = Task.Factory.StartNew(
                    CheckCore,
                    CancellationToken.None,
                    TaskCreationOptions.DenyChildAttach,
                    TaskScheduler.Default);
                _activeCheck = check;
            }

            try
            {
                if (!check.Wait(1200))
                {
                    return FocusCheckResult.Block(
                        FocusBlockReason.Unavailable,
                        "Windows focus inspection timed out.");
                }
            }
            catch (AggregateException)
            {
                lock (_checkLock)
                {
                    if (Object.ReferenceEquals(_activeCheck, check))
                    {
                        _activeCheck = null;
                    }
                }

                return FocusCheckResult.Block(
                    FocusBlockReason.Unavailable,
                    "Windows could not verify focus security.");
            }

            FocusCheckResult result;
            try
            {
                result = check.Result;
            }
            catch (AggregateException)
            {
                result = FocusCheckResult.Block(
                    FocusBlockReason.Unavailable,
                    "Windows could not verify focus security.");
            }
            lock (_checkLock)
            {
                if (Object.ReferenceEquals(_activeCheck, check))
                {
                    _activeCheck = null;
                }
            }

            return result;
        }

        private static FocusCheckResult CheckCore()
        {
            try
            {
                IntPtr foreground = NativeMethods.GetForegroundWindow();
                if (foreground == IntPtr.Zero)
                {
                    return FocusCheckResult.Block(
                        FocusBlockReason.Unavailable,
                        "Parrot could not verify the focused window.");
                }

                uint foregroundProcessId;
                if (NativeMethods.GetWindowThreadProcessId(foreground, out foregroundProcessId) == 0
                    || foregroundProcessId == 0)
                {
                    return FocusCheckResult.Block(
                        FocusBlockReason.Unavailable,
                        "Parrot could not identify the focused application.");
                }

                // Parrot contains no secret-entry controls. Allowing its own
                // accessible window makes the visible Toggle button usable.
                if (foregroundProcessId == (uint)Process.GetCurrentProcess().Id)
                {
                    return ForegroundStillMatches(foreground, foregroundProcessId)
                        ? FocusCheckResult.Allow()
                        : FocusCheckResult.Block(
                            FocusBlockReason.Unavailable,
                            "The focused application changed during the security check.");
                }

                FocusCheckResult processResult = CheckForegroundProcess(foregroundProcessId);
                if (!processResult.Allowed)
                {
                    return processResult;
                }

                AutomationElement focused = AutomationElement.FocusedElement;
                if (focused == null)
                {
                    return FocusCheckResult.Block(
                        FocusBlockReason.Unavailable,
                        "Parrot could not inspect the focused control.");
                }

                object processValue = focused.GetCurrentPropertyValue(
                    AutomationElement.ProcessIdProperty,
                    true);
                if (Object.ReferenceEquals(processValue, AutomationElement.NotSupported)
                    || !(processValue is int)
                    || (uint)(int)processValue != foregroundProcessId)
                {
                    return FocusCheckResult.Block(
                        FocusBlockReason.Unavailable,
                        "The focused control could not be matched to the focused application.");
                }

                object passwordValue = focused.GetCurrentPropertyValue(
                    AutomationElement.IsPasswordProperty,
                    true);
                if (Object.ReferenceEquals(passwordValue, AutomationElement.NotSupported)
                    || !(passwordValue is bool))
                {
                    return FocusCheckResult.Block(
                        FocusBlockReason.Unavailable,
                        "The focused control did not expose its password status.");
                }

                if ((bool)passwordValue)
                {
                    return FocusCheckResult.Block(
                        FocusBlockReason.Password,
                        "Password field focused. Dictation was blocked.");
                }

                int[] originalRuntimeId = focused.GetRuntimeId();
                AutomationElement refreshedFocus = AutomationElement.FocusedElement;
                if (refreshedFocus == null)
                {
                    return FocusCheckResult.Block(
                        FocusBlockReason.Unavailable,
                        "The focused control changed during the security check.");
                }

                object refreshedProcessValue = refreshedFocus.GetCurrentPropertyValue(
                    AutomationElement.ProcessIdProperty,
                    true);
                object refreshedPasswordValue = refreshedFocus.GetCurrentPropertyValue(
                    AutomationElement.IsPasswordProperty,
                    true);
                object hasKeyboardFocusValue = refreshedFocus.GetCurrentPropertyValue(
                    AutomationElement.HasKeyboardFocusProperty,
                    true);
                if (Object.ReferenceEquals(refreshedProcessValue, AutomationElement.NotSupported)
                    || !(refreshedProcessValue is int)
                    || (uint)(int)refreshedProcessValue != foregroundProcessId
                    || Object.ReferenceEquals(refreshedPasswordValue, AutomationElement.NotSupported)
                    || !(refreshedPasswordValue is bool)
                    || (bool)refreshedPasswordValue
                    || Object.ReferenceEquals(hasKeyboardFocusValue, AutomationElement.NotSupported)
                    || !(hasKeyboardFocusValue is bool)
                    || !(bool)hasKeyboardFocusValue
                    || !SameRuntimeId(originalRuntimeId, refreshedFocus.GetRuntimeId()))
                {
                    return FocusCheckResult.Block(
                        FocusBlockReason.Unavailable,
                        "The focused control changed or became protected during the security check.");
                }

                if (!ForegroundStillMatches(foreground, foregroundProcessId))
                {
                    return FocusCheckResult.Block(
                        FocusBlockReason.Unavailable,
                        "The focused application changed during the security check.");
                }

                return FocusCheckResult.Allow();
            }
            catch (ElementNotAvailableException)
            {
                return FocusCheckResult.Block(
                    FocusBlockReason.Unavailable,
                    "The focused control disappeared before Parrot could verify it.");
            }
            catch (UnauthorizedAccessException)
            {
                return FocusCheckResult.Block(
                    FocusBlockReason.Unavailable,
                    "The focused application is inaccessible to Parrot.");
            }
            catch (COMException)
            {
                return FocusCheckResult.Block(
                    FocusBlockReason.Unavailable,
                    "Windows UI Automation could not verify the focused control.");
            }
            catch (InvalidOperationException)
            {
                return FocusCheckResult.Block(
                    FocusBlockReason.Unavailable,
                    "Windows could not verify focus security.");
            }
            catch (Exception)
            {
                return FocusCheckResult.Block(
                    FocusBlockReason.Unavailable,
                    "Windows could not verify focus security.");
            }
        }

        internal static bool SameForegroundIdentity(
            IntPtr expectedWindow,
            uint expectedProcessId,
            IntPtr currentWindow,
            uint currentProcessId)
        {
            return expectedWindow != IntPtr.Zero
                && expectedWindow == currentWindow
                && expectedProcessId != 0
                && expectedProcessId == currentProcessId;
        }

        internal static bool SameRuntimeId(int[] first, int[] second)
        {
            if (first == null || second == null || first.Length == 0 || first.Length != second.Length)
            {
                return false;
            }

            for (int index = 0; index < first.Length; index++)
            {
                if (first[index] != second[index])
                {
                    return false;
                }
            }

            return true;
        }

        private static bool ForegroundStillMatches(IntPtr expectedWindow, uint expectedProcessId)
        {
            IntPtr currentWindow = NativeMethods.GetForegroundWindow();
            uint currentProcessId;
            if (currentWindow == IntPtr.Zero
                || NativeMethods.GetWindowThreadProcessId(currentWindow, out currentProcessId) == 0)
            {
                return false;
            }

            return SameForegroundIdentity(
                expectedWindow,
                expectedProcessId,
                currentWindow,
                currentProcessId);
        }

        private static FocusCheckResult CheckForegroundProcess(uint processId)
        {
            IntPtr process = NativeMethods.OpenProcess(
                NativeMethods.ProcessQueryLimitedInformation,
                false,
                processId);
            if (process == IntPtr.Zero)
            {
                return FocusCheckResult.Block(
                    FocusBlockReason.Unavailable,
                    "The focused application is inaccessible to Parrot.");
            }

            IntPtr token = IntPtr.Zero;
            try
            {
                if (!NativeMethods.OpenProcessToken(process, NativeMethods.TokenQuery, out token)
                    || token == IntPtr.Zero)
                {
                    return FocusCheckResult.Block(
                        FocusBlockReason.Unavailable,
                        "Parrot could not verify the focused application's security level.");
                }

                NativeMethods.TokenElevationInfo elevation = new NativeMethods.TokenElevationInfo();
                elevation.TokenIsElevated = 0;
                int returnedLength;
                if (!NativeMethods.GetTokenInformation(
                    token,
                    NativeMethods.TokenElevation,
                    ref elevation,
                    Marshal.SizeOf(typeof(NativeMethods.TokenElevationInfo)),
                    out returnedLength))
                {
                    return FocusCheckResult.Block(
                        FocusBlockReason.Unavailable,
                        "Parrot could not verify the focused application's security level.");
                }

                if (returnedLength < Marshal.SizeOf(typeof(NativeMethods.TokenElevationInfo)))
                {
                    return FocusCheckResult.Block(
                        FocusBlockReason.Unavailable,
                        "Parrot received an incomplete security status from the focused application.");
                }

                if (elevation.TokenIsElevated != 0)
                {
                    return FocusCheckResult.Block(
                        FocusBlockReason.Elevated,
                        "An elevated application is focused. Dictation was blocked.");
                }

                return FocusCheckResult.Allow();
            }
            finally
            {
                if (token != IntPtr.Zero)
                {
                    NativeMethods.CloseHandle(token);
                }

                NativeMethods.CloseHandle(process);
            }
        }
    }
}
