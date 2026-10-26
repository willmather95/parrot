import Foundation
import Testing
@testable import parrot

@Test func pauseStopsOnceAndBlocksShortcutUntilResume() {
    let state = DictationToggleState()
    let now = Date()
    #expect(state.toggle(observedAt: now) == .start)
    #expect(state.setEnabled(false, observedAt: now.addingTimeInterval(1)))
    #expect(!state.setEnabled(false, observedAt: now.addingTimeInterval(2)))
    #expect(state.toggle(observedAt: now.addingTimeInterval(3)) == .ignoredWhilePaused)
    #expect(!state.setEnabled(true, observedAt: now.addingTimeInterval(4)))
    #expect(state.toggle(observedAt: now.addingTimeInterval(3)) == .ignoredWhilePaused)
    #expect(state.toggle(observedAt: now.addingTimeInterval(5)) == .start)
    #expect(state.toggle(observedAt: now.addingTimeInterval(6)) == .stop)
}

@Test func pauseWhileIdleDoesNotInventARecording() {
    let state = DictationToggleState()
    #expect(!state.setEnabled(false))
    #expect(!state.stopIfRecording())
    #expect(state.toggle() == .ignoredWhilePaused)
    #expect(!state.setEnabled(true))
    #expect(state.toggle() == .start)
}
