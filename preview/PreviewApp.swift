import AppKit
import SwiftUI

/// A separate in-memory renderer. It intentionally does not construct Parrot's
/// menu controller, audio capture, hotkey monitor, model, or preference store.
@main
@MainActor
struct ParrotRetroPreview {
    static func main() {
        let arguments = CommandLine.arguments
        let stateName = argument("--state", in: arguments) ?? "recording"
        let sectionName = argument("--section", in: arguments) ?? "overview"
        let screenshotPath = argument("--screenshot", in: arguments)
        let simulateSaveFailure = arguments.contains("--save-failure")
        let simulateLoadFailure = arguments.contains("--load-failure")

        let app = NSApplication.shared
        app.setActivationPolicy(.regular)
        if let iconURL = Bundle.main.url(forResource: "Parrot", withExtension: "icns"),
           let icon = NSImage(contentsOf: iconURL) {
            app.applicationIconImage = icon
        }

        if let markPath = argument("--brand-check", in: arguments) {
            let bird = ParrotBrand.recordingMark
            guard !bird.isTemplate, ParrotBrand.mark.isTemplate,
                  let tiff = bird.tiffRepresentation,
                  let bitmap = NSBitmapImageRep(data: tiff),
                  let png = bitmap.representation(using: .png, properties: [:]) else { exit(1) }
            do { try png.write(to: URL(fileURLWithPath: markPath)) }
            catch { fputs("Cannot render recording bird.\n", stderr); exit(1) }
            print("PASS: ocean bird renders; idle bird remains template; no microphone capture")
            exit(0)
        }

        if arguments.contains("--menu-check") || arguments.contains("--menu-preview") {
            app.setActivationPolicy(.accessory)
            app.finishLaunching()
            let menu = MenuBarController(modelID: "Preview model", onQuit: { app.terminate(nil) })
            var changes: [Bool] = []
            menu.configureDictationToggle { changes.append($0) }
            if arguments.contains("--menu-check") {
                DispatchQueue.main.async {
                    var checks: [String: Bool] = [:]
                    checks["accessory"] = app.activationPolicy() == .accessory
                    checks["statusItemAttached"] = menu.isReady
                    checks["startsEnabled"] = menu.dictationEnabled
                    menu.toggleDictationClicked()
                    checks["pauseUpdatesState"] = !menu.dictationEnabled && changes == [false]
                    menu.setTranscribing()
                    menu.setRecording(false)
                    menu.toggleDictationClicked()
                    checks["resumeOnlySignalsEnable"] = menu.dictationEnabled && changes == [false, true]
                    let data = try! JSONSerialization.data(withJSONObject: checks, options: [.prettyPrinted, .sortedKeys])
                    print(String(decoding: data, as: UTF8.self))
                    exit(checks.values.allSatisfy { $0 } ? 0 : 1)
                }
            }
            withExtendedLifetime(menu) { app.run() }
            return
        }

        let studio = StudioModel(modelID: "Preview model", isPreview: true)
        var stored = DictationPreferences(
            replacements: [
                PhraseReplacement(spoken: "Parrot Studio", replacement: "Parrot Studio"),
                PhraseReplacement(spoken: "Acme", replacement: "ACME")
            ],
            snippets: [
                PhraseReplacement(spoken: "my sign off", replacement: "Thanks,\nParrot user")
            ]
        )
        if arguments.contains("--empty") { stored = .empty }
        studio.configurePreferences(load: {
            if simulateLoadFailure { throw PreviewStoreError.simulated }
            return stored
        }, save: {
            if simulateSaveFailure { throw PreviewStoreError.simulated }
            stored = $0
        })

        let overlay = OverlayModel()
        switch stateName {
        case "ready":
            studio.setState(.ready)
            overlay.state = .hidden
        case "transcribing":
            studio.setState(.transcribing)
            overlay.state = .transcribing
        case "error":
            studio.setState(.error("Microphone unavailable. Check access and try again."))
            overlay.state = .hidden
        default:
            studio.setState(.recording)
            studio.pushLevel(0.12)
            studio.elapsedSeconds = 24
            overlay.state = .recording
            overlay.pushLevel(0.12)
            overlay.elapsedSeconds = 24
        }

        let section = StudioSection(rawValue: sectionName) ?? .overview
        if arguments.contains("--draft") || simulateSaveFailure {
            let kind: PhraseKind = section == .snippets ? .snippets : .vocabulary
            studio.startDraft(for: kind)
            studio.updateSpoken("example phrase", for: kind)
            studio.updateReplacement("Example phrase", for: kind)
        }
        let minimum = arguments.contains("--minimum")
        let canvas = PreviewCanvas(studio: studio, overlay: overlay, section: section,
                                   minimum: minimum)
            .transaction { transaction in
                if arguments.contains("--reduced-motion") {
                    transaction.animation = nil
                    transaction.disablesAnimations = true
                }
            }
        let content = NSHostingView(rootView: canvas)
        let window = StudioWindow(
            contentRect: NSRect(x: 0, y: 0, width: minimum ? 580 : 860, height: minimum ? 580 : 730),
            styleMask: [.titled, .closable, .miniaturizable],
            backing: .buffered,
            defer: false
        )
        window.title = "Parrot Retro Preview - no live audio"
        window.center()
        window.contentView = content
        window.makeKeyAndOrderFront(nil)
        app.activate(ignoringOtherApps: true)

        if simulateSaveFailure {
            DispatchQueue.main.asyncAfter(deadline: .now() + 0.2) {
                _ = studio.save(studio.preferences)
            }
        }
        if let screenshotPath {
            DispatchQueue.main.asyncAfter(deadline: .now() + 0.7) {
                do {
                    try renderPNG(from: content, to: screenshotPath)
                    print("Preview screenshot: \(screenshotPath)")
                    app.terminate(nil)
                } catch {
                    fputs("Preview screenshot failed: \(error)\n", stderr)
                    exit(1)
                }
            }
        }
        app.run()
    }

    private static func argument(_ name: String, in args: [String]) -> String? {
        guard let index = args.firstIndex(of: name), args.indices.contains(index + 1) else { return nil }
        return args[index + 1]
    }

    private static func renderPNG(from view: NSView, to path: String) throws {
        view.layoutSubtreeIfNeeded()
        view.displayIfNeeded()
        let bounds = view.bounds
        guard let bitmap = view.bitmapImageRepForCachingDisplay(in: bounds) else {
            throw PreviewError.bitmapUnavailable
        }
        view.cacheDisplay(in: bounds, to: bitmap)
        guard let data = bitmap.representation(using: .png, properties: [:]) else {
            throw PreviewError.pngUnavailable
        }
        try data.write(to: URL(fileURLWithPath: path), options: .atomic)
    }

    private enum PreviewError: Error {
        case bitmapUnavailable, pngUnavailable
    }

    private enum PreviewStoreError: Error {
        case simulated
    }
}

private struct PreviewCanvas: View {
    @ObservedObject var studio: StudioModel
    @ObservedObject var overlay: OverlayModel
    let section: StudioSection
    let minimum: Bool

    var body: some View {
        VStack(spacing: 16) {
            StudioView(model: studio, initialSection: section)
                .frame(width: minimum ? 540 : 790, height: minimum ? 430 : 580)
                .clipShape(RoundedRectangle(cornerRadius: 12))
                .overlay(RoundedRectangle(cornerRadius: 12).stroke(ParrotPalette.line))
            if overlay.state != .hidden {
                OverlayPill(model: overlay)
                    .frame(width: 276, height: 56)
            } else {
                Text("Overlay hidden in this state")
                    .font(.system(size: 11, design: .monospaced))
                    .foregroundStyle(ParrotPalette.muted)
                    .frame(height: 56)
            }
            Text("PREVIEW ONLY · SIMULATED DATA · NO MICROPHONE")
                .font(.system(size: 10, weight: .semibold, design: .monospaced))
                .tracking(0.8)
                .foregroundStyle(ParrotPalette.muted)
        }
        .frame(width: minimum ? 580 : 860, height: minimum ? 580 : 730)
        .background(ParrotPalette.background)
    }
}
