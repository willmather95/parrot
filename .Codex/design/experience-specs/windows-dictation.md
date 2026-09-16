# Experience Specification: Windows Dictation

**Status:** draft
**Owner:** Parrot
**Surface:** Windows desktop utility and notification-area icon
**Updated:** 2026-09-16
**Source context:** Mac toggle-dictation specification, Windows foundation prototype, user request for a work-laptop version without administrator access.

## 1. Product Moment

Will wants to dictate into work applications on Windows. The primary action is one shortcut to start and the same shortcut to stop. The costliest mistakes are unnoticed recording and delivery into a password field. Recording status and the stop instruction must be understandable immediately.

## 2. Experience Thesis

A small native utility makes recording explicit and leaves delivery in the user's control.

The signature moment is finishing a thought and seeing “Copied. Press Ctrl+V.” Shortcut handling, status, cancellation, and errors stay predictable. No decorative effects are necessary.

## 3. Information Hierarchy

1. Current state: Ready, Listening, Finishing, Copied, or Blocked.
2. Start/Stop and Cancel controls, plus Ctrl+Alt+Space instruction.
3. Local Parakeet speech backend and its availability.

No transcript history, account, or upsell competes with dictation.

## 4. Composition and Regions

Use native Windows Forms controls in a compact modeless main window, with a notification-area icon and menu. Keep a visible status surface while recording. Do not steal focus when a global shortcut starts recording. Do not create a custom modal or animated overlay. Native layout must grow for DPI scaling and text.

## 5. State and Transition Model

| State | Entry | Feedback | Exit |
| --- | --- | --- | --- |
| Ready | Initialization succeeds | Start instruction | Explicit start |
| Listening | Safe focus and successful microphone start | Listening and Stop | Stop, Cancel, error, ten-minute cap |
| Finishing | Stop | Finishing; no new start | Completion or bounded timeout |
| Copied | Nonempty result and safe clipboard delivery | Press Ctrl+V | Next start |
| Blocked | Missing bundled model/runtime, permission, unknown/secure focus, timeout, or copy failure | Specific recovery instruction without transcript content | Retry or restart |

Cancel, completion errors, and timeouts discard the capture. Late callbacks cannot publish or enter another capture. A capture never starts automatically on launch or after failure.

## 6. Interaction Contract

- Ctrl+Alt+Space toggles capture once per press; held keys cannot repeat it.
- Start/Stop is available by keyboard and pointer through native controls.
- Cancel discards audio/text. Escape in the application cancels active work.
- Quit releases recording resources and closes the application.
- Clipboard is the only delivery mode. No synthetic paste or text injection.
- Check protected/unknown focus before recording and immediately before copying. Discard if safety cannot be established.

## 7. Motion Choreography

N/A: static native state changes only. Reduced-motion settings require no alternate animation path.

## 8. Responsive Transformations

Desktop only. Test 100%, 150%, and 200% Windows display scaling, laptop plus external monitor, and keyboard navigation. Native autosizing must retain labels and controls. Mobile/tablet browser layouts do not apply; touch input on Windows must work with normal buttons.

## 9. Accessibility and User Control

Use native labeled controls with visible focus, normal Tab order, system colors, and text status in addition to icons. Critical failures remain in the main window, not only temporary notifications. Escape cancels, Quit always remains reachable, and no focus trap is introduced. Verify status accessibility with Narrator on Windows.

## 10. Performance and Media Contract

No network call, media download, or model loading blocks the initial window. Speech initialization must report failure visibly. Bound recording at ten minutes and finishing with a deadline. No raw audio or transcripts are written to disk by this host. Clipboard retention is controlled separately by Windows settings.

## 11. Content Truth and Microcopy

Headline: “Parrot for Windows”. Primary action: “Start recording”. Label the local Parakeet backend and preview status. Do not imply measured work-laptop accuracy, managed-device approval, or verified microphone capture from initialization alone. Errors explain the smallest recovery action.

## 12. Implementation Boundaries

Use in-box .NET Framework, Windows Forms, UI Automation, and pinned app-local sherpa-onnx with Parakeet TDT 0.6B v2 int8. Capture PCM audio in memory; load the model and decode off the UI thread. Source lives in windows/. Keep macOS app/runtime behavior unchanged. No service, driver, administrator manifest, execution-policy bypass, cloud transcription, transcript logging, or automatic startup registration. The user requested the local-model quality upgrade on September 16. Signing still requires separate credentials and authority.

## 13. Assumptions and Open Decisions

Windows 11 x64 is the first supported target. Target hardware is a Lenovo ThinkPad of unknown CPU/RAM. Use CPU inference without a GPU requirement. Real microphone accuracy and latency remain unknown until checked on that laptop. Employer application-control policy may reject unsigned executables even without elevation. The larger portable package carries its model and runtime so first use needs no download or language pack.

## 14. Acceptance Criteria

1. PASS if launching from a user-writable folder does not request elevation.
2. PASS if one shortcut starts and the second stops, with visible state throughout.
3. PASS if an ordinary editor receives copied text after manual Ctrl+V.
4. PASS if protected/unknown focus blocks capture or delivery without changing clipboard.
5. PASS if Cancel, timeout, error, and stale callbacks cannot publish text.
6. PASS if repeated recordings release resources and remain independent.
7. PASS if keyboard, Narrator, touch, and 100/150/200% scaling preserve usable controls.
8. PASS if a missing model/runtime or microphone permission produces an actionable error without downloading anything.
9. PASS if the running macOS installation remains unchanged.
10. PASS if pinned public speech fixtures meet the declared word-error thresholds and silence does not produce text. Report timings without treating CI hardware as the ThinkPad.
11. PASS if long input is bounded and segmented, and cancellation prevents stale inference from copying text.

## 15. Verification Plan

Run deterministic Windows state-machine tests, compilation, manifest inspection, package checksum validation, per-user install checks, and actual neural inference with public speech and non-speech fixtures in Windows CI. Real Windows verification covers microphone, editor paste, password/unknown focus, hotkey conflict, cancel, sleep/resume, unplugged microphone, DPI, Narrator, and quit. These remain unverified on a Mac. Boris and independently orchestrated Fresh Eyes review cover the final candidate. No web Lighthouse or mobile browser checks apply to a native desktop utility.
