import Foundation

/// Converts noisy modifier-flag updates into one toggle request per complete
/// Control + Fn/Globe press/release cycle.
struct ToggleChordState {
    private(set) var isChordDown = false
    private var suppressUntilRelease = false

    mutating func update(isDown: Bool) -> Bool {
        if suppressUntilRelease {
            isChordDown = isDown
            if !isDown { suppressUntilRelease = false }
            return false
        }

        let isRisingEdge = isDown && !isChordDown
        isChordDown = isDown
        return isRisingEdge
    }

    mutating func recoveredAfterTapDisable(isChordCurrentlyDown: Bool) {
        // Querying the real session flags closes both recovery races: keep
        // suppressing a chord that is still physically held, but do not wait
        // forever for a release that already happened while the tap was down.
        suppressUntilRelease = isChordCurrentlyDown
        isChordDown = isChordCurrentlyDown
    }
}

/// Thread-safe recording truth shared by the hotkey callback and the safety
/// timer. State changes happen before their side effects so duplicate events
/// cannot start or stop the same session twice.
final class DictationToggleState: @unchecked Sendable {
    enum Action: Equatable {
        case start
        case stop
        case ignoredDuringSafetyRearm
        case ignoredWhilePaused
    }

    private let lock = NSLock()
    private var recording = false
    private var enabled = true
    private var enabledSince = Date.distantPast
    private var restartBlockedUntilObservation = Date.distantPast

    func toggle(observedAt: Date = Date()) -> Action {
        lock.lock()
        defer { lock.unlock() }
        guard enabled, observedAt >= enabledSince else { return .ignoredWhilePaused }
        if !recording, observedAt < restartBlockedUntilObservation {
            return .ignoredDuringSafetyRearm
        }
        recording.toggle()
        return recording ? .start : .stop
    }

    /// Returns whether the caller must finish an active clip. Old queued
    /// shortcut observations cannot reopen the microphone after resume.
    @discardableResult
    func setEnabled(_ value: Bool, observedAt: Date = Date()) -> Bool {
        lock.lock()
        defer { lock.unlock() }
        guard enabled != value else { return false }
        enabled = value
        enabledSince = observedAt
        guard !value, recording else { return false }
        recording = false
        return true
    }

    func startFailed() {
        lock.lock()
        recording = false
        lock.unlock()
    }

    /// Used by the safety limit and shutdown paths. Returns true exactly once
    /// for an active recording.
    func stopIfRecording(
        blockingRestartFor duration: TimeInterval = 0,
        observedAt: Date = Date()
    ) -> Bool {
        lock.lock()
        defer { lock.unlock() }
        guard recording else { return false }
        recording = false
        if duration > 0 {
            restartBlockedUntilObservation = observedAt.addingTimeInterval(duration)
        }
        return true
    }
}

enum RecordingStopReason: Equatable {
    case userToggle
    case safetyLimit
}

/// Owns the single delayed safety action so every normal stop can cancel it.
final class RecordingLimitScheduler: @unchecked Sendable {
    private let lock = NSLock()
    private var workItem: DispatchWorkItem?

    func schedule(after duration: TimeInterval, action: @escaping @Sendable () -> Void) {
        cancel()
        let item = DispatchWorkItem(block: action)
        lock.lock()
        workItem = item
        lock.unlock()
        DispatchQueue.main.asyncAfter(deadline: .now() + duration, execute: item)
    }

    func cancel() {
        lock.lock()
        let item = workItem
        workItem = nil
        lock.unlock()
        item?.cancel()
    }
}
