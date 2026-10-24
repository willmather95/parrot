import AppKit
import SwiftUI

/// Status bar item in the top-right of the menu bar. Shows recording state at
/// a glance and provides the only persistent control surface for the daemon
/// (since we run as `.accessory` - no dock icon, no main window).
@MainActor
final class MenuBarController {
    private let statusItem: NSStatusItem
    private let modelLabel: NSMenuItem
    private let stateLabel: NSMenuItem
    private let modelID: String
    private let onQuit: @MainActor () -> Void
    private let studioModel: StudioModel
    private var studioWindow: StudioWindow?
    private var previouslyActiveApplication: NSRunningApplication?

    init(
        modelID: String,
        onQuit: @escaping @MainActor () -> Void = { NSApp.terminate(nil) }
    ) {
        self.modelID = modelID
        self.onQuit = onQuit
        self.studioModel = StudioModel(modelID: modelID)
        self.statusItem = NSStatusBar.system.statusItem(withLength: NSStatusItem.variableLength)

        let menu = NSMenu()
        menu.autoenablesItems = false

        stateLabel = NSMenuItem(
            title: "idle · press control + fn/globe to record",
            action: nil,
            keyEquivalent: ""
        )
        stateLabel.isEnabled = false
        menu.addItem(stateLabel)

        modelLabel = NSMenuItem(title: "model: \(modelID)", action: nil, keyEquivalent: "")
        modelLabel.isEnabled = false
        menu.addItem(modelLabel)

        menu.addItem(.separator())

        let studio = NSMenuItem(
            title: "Open Parrot Studio…",
            action: #selector(openStudioClicked),
            keyEquivalent: ","
        )
        studio.keyEquivalentModifierMask = .command
        studio.target = self
        menu.addItem(studio)

        menu.addItem(.separator())

        let quit = NSMenuItem(
            title: "Quit parrot",
            action: #selector(quitClicked),
            keyEquivalent: "q"
        )
        quit.target = self
        menu.addItem(quit)

        statusItem.menu = menu
        configureButton(recording: false)
    }

    var isReady: Bool {
        guard let window = statusItem.button?.window else { return false }
        return window.windowNumber > 0 && window.screen != nil
    }

    func setRecording(_ recording: Bool) {
        studioModel.setState(recording ? .recording : .ready)
        stateLabel.title = recording
            ? "● recording · press control + fn/globe to stop"
            : "idle · press control + fn/globe to record"
        configureButton(recording: recording)
    }

    func setTranscribing(_ message: String = "transcribing…") {
        studioModel.setState(.transcribing)
        stateLabel.title = message
        guard let button = statusItem.button else { return }
        let image = NSImage(
            systemSymbolName: "waveform.path",
            accessibilityDescription: "Parrot transcribing"
        )
        image?.isTemplate = true
        button.image = image
        button.toolTip = "Parrot is transcribing on this Mac"
    }

    func setCaptureError(_ message: String) {
        studioModel.setState(.error(message))
        stateLabel.title = message
        guard let button = statusItem.button else { return }
        let image = NSImage(
            systemSymbolName: "mic.slash",
            accessibilityDescription: "Parrot microphone unavailable"
        )
        image?.isTemplate = true
        button.image = image
        button.toolTip = message
    }

    /// Install the app-owned local preference store. The UI never reads settings directly.
    func configurePreferences(
        load: @escaping () throws -> DictationPreferences,
        save: @escaping (DictationPreferences) throws -> Void
    ) {
        studioModel.configurePreferences(load: load, save: save)
    }

    /// Optional elapsed time supplied by the capture lifecycle, never inferred by the UI.
    func setElapsedSeconds(_ seconds: TimeInterval?) {
        studioModel.elapsedSeconds = seconds
    }

    /// Real audio level from the capture pipeline. Safe to call from any thread.
    nonisolated func pushLevel(_ level: Float) {
        Task { @MainActor in
            self.studioModel.pushLevel(level)
        }
    }

    private func configureButton(recording: Bool) {
        guard let button = statusItem.button else { return }
        let image = recording
            ? NSImage(systemSymbolName: "mic.fill", accessibilityDescription: "Parrot recording")
            : ParrotBrand.mark
        image?.isTemplate = true
        button.image = image
        button.toolTip = recording
            ? "Parrot is recording · press Control + Fn/Globe to stop"
            : "Parrot dictation · press Control + Fn/Globe to record"
    }

    @objc private func quitClicked() {
        onQuit()
    }

    @objc private func openStudioClicked() {
        if studioWindow?.isVisible != true {
            previouslyActiveApplication = NSWorkspace.shared.frontmostApplication
        }
        studioModel.reloadPreferences()
        if studioWindow == nil {
            let window = StudioWindow(
                contentRect: NSRect(x: 0, y: 0, width: 790, height: 580),
                styleMask: [.titled, .closable, .miniaturizable, .resizable],
                backing: .buffered,
                defer: false
            )
            window.title = "Parrot Studio"
            window.minSize = NSSize(width: 540, height: 430)
            window.center()
            window.isReleasedWhenClosed = false
            window.onClose = { [weak self] in
                guard let self,
                      let previous = self.previouslyActiveApplication,
                      previous.processIdentifier != NSRunningApplication.current.processIdentifier
                else { return }
                previous.activate(options: [])
            }
            window.contentView = NSHostingView(rootView: StudioView(model: studioModel))
            studioWindow = window
        }
        studioWindow?.makeKeyAndOrderFront(nil)
        NSApp.activate(ignoringOtherApps: true)
    }
}

/// Escape closes the accessory app's Studio without terminating dictation.
final class StudioWindow: NSWindow {
    var onClose: (() -> Void)?

    override func cancelOperation(_ sender: Any?) {
        performClose(sender)
    }

    override func close() {
        super.close()
        onClose?()
    }
}
