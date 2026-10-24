// Hallmark: native instrument, locked charcoal/amber palette.
// Pre-emit critique: philosophy 4, hierarchy 4, execution 4, specificity 5, restraint 4, variety 4.
import AppKit
import SwiftUI

enum ParrotPalette {
    static let background = Color(red: 21/255, green: 25/255, blue: 24/255)
    static let panel = Color(red: 31/255, green: 36/255, blue: 34/255)
    static let raised = Color(red: 39/255, green: 45/255, blue: 42/255)
    static let line = Color(red: 77/255, green: 83/255, blue: 75/255)
    static let cream = Color(red: 238/255, green: 232/255, blue: 213/255)
    static let muted = Color(red: 177/255, green: 181/255, blue: 168/255)
    static let amber = Color(red: 240/255, green: 174/255, blue: 82/255)
    static let red = Color(red: 226/255, green: 117/255, blue: 100/255)
}

enum StudioRunState: Equatable {
    case ready
    case recording
    case transcribing
    case error(String)

    var title: String {
        switch self {
        case .ready: "Ready to dictate"
        case .recording: "Recording"
        case .transcribing: "Transcribing"
        case .error: "Needs attention"
        }
    }

    var detail: String {
        switch self {
        case .ready: "Press Control + Fn/Globe to start."
        case .recording: "Press Control + Fn/Globe to stop."
        case .transcribing: "Processing your speech on this Mac."
        case .error(let message): message
        }
    }
}

struct PhraseDraft: Equatable {
    var id: UUID
    var isNew: Bool
    var spoken: String
    var replacement: String

    init(entry: PhraseReplacement? = nil) {
        id = entry?.id ?? UUID()
        isNew = entry == nil
        spoken = entry?.spoken ?? ""
        replacement = entry?.replacement ?? ""
    }
}

@MainActor
final class StudioModel: ObservableObject {
    @Published var runState: StudioRunState = .ready
    @Published var preferences: DictationPreferences = .empty
    @Published var storeError: String?
    @Published var canEditPreferences = false
    @Published var levels: [Float] = Array(repeating: 0, count: 18)
    @Published var elapsedSeconds: TimeInterval?
    @Published private(set) var drafts: [PhraseKind: PhraseDraft] = [:]

    let modelID: String
    let isPreview: Bool
    private var loadPreferences: (() throws -> DictationPreferences)?
    private var savePreferences: ((DictationPreferences) throws -> Void)?

    init(modelID: String, isPreview: Bool = false) {
        self.modelID = modelID
        self.isPreview = isPreview
    }

    func configurePreferences(
        load: @escaping () throws -> DictationPreferences,
        save: @escaping (DictationPreferences) throws -> Void
    ) {
        loadPreferences = load
        savePreferences = save
        reloadPreferences()
    }

    func reloadPreferences() {
        guard let loadPreferences else {
            canEditPreferences = false
            storeError = "Local phrase settings are unavailable."
            return
        }
        do {
            preferences = try loadPreferences()
            canEditPreferences = true
            storeError = nil
        } catch {
            canEditPreferences = false
            storeError = "Could not load local phrase settings. Check the local settings file and reopen Studio."
        }
    }

    @discardableResult
    func save(_ next: DictationPreferences) -> Bool {
        guard canEditPreferences, let savePreferences else {
            storeError = "Local phrase settings are unavailable."
            return false
        }
        do {
            let valid = try next.validated()
            try savePreferences(valid)
            preferences = valid
            storeError = nil
            return true
        } catch let error as DictationPreferencesError {
            storeError = error.localizedDescription
            return false
        } catch {
            canEditPreferences = false
            storeError = "Could not save local phrase settings. Reopen Studio to reload the latest settings, then try again."
            return false
        }
    }

    func draft(for kind: PhraseKind) -> PhraseDraft? { drafts[kind] }

    func startDraft(for kind: PhraseKind, editing entry: PhraseReplacement? = nil) {
        guard drafts[kind] == nil else { return }
        drafts[kind] = PhraseDraft(entry: entry)
    }

    func updateSpoken(_ text: String, for kind: PhraseKind) {
        drafts[kind]?.spoken = text
    }

    func updateReplacement(_ text: String, for kind: PhraseKind) {
        drafts[kind]?.replacement = text
    }

    func clearDraft(for kind: PhraseKind) {
        drafts.removeValue(forKey: kind)
    }

    func pushLevel(_ level: Float) {
        guard runState == .recording else { return }
        let shaped = min(1, sqrt(max(0, level)) * 2.8)
        let envelope: [Float] = [0.24, 0.36, 0.52, 0.68, 0.83, 0.96, 0.78, 0.61, 0.85, 1,
                                 0.72, 0.9, 0.66, 0.8, 0.57, 0.43, 0.33, 0.23]
        levels = envelope.map { shaped * $0 }
    }

    func setState(_ state: StudioRunState) {
        runState = state
        if state != .recording {
            levels = Array(repeating: 0, count: levels.count)
            elapsedSeconds = nil
        }
    }
}

struct StudioView: View {
    @ObservedObject var model: StudioModel
    @State private var selection: StudioSection
    @FocusState private var focusedSection: StudioSection?
    @Environment(\.accessibilityReduceMotion) private var reduceMotion

    init(model: StudioModel, initialSection: StudioSection = .overview) {
        self.model = model
        _selection = State(initialValue: initialSection)
    }

    var body: some View {
        GeometryReader { geometry in
            Group {
                if geometry.size.width < 670 {
                    VStack(spacing: 0) {
                        navigation(horizontal: true)
                        Divider().overlay(ParrotPalette.line)
                        content
                    }
                } else {
                    HStack(spacing: 0) {
                        navigation(horizontal: false)
                            .frame(width: 186)
                        Divider().overlay(ParrotPalette.line)
                        content
                    }
                }
            }
            .background(ParrotPalette.background)
            .foregroundStyle(ParrotPalette.cream)
        }
        .frame(minWidth: 540, minHeight: 430)
        .preferredColorScheme(.dark)
        .tint(ParrotPalette.amber)
        .onAppear {
            model.reloadPreferences()
            focusedSection = selection
        }
        .accessibilityIdentifier("parrotStudio")
    }

    @ViewBuilder
    private func navigation(horizontal: Bool) -> some View {
        VStack(alignment: .leading, spacing: 0) {
            if !horizontal {
            HStack(alignment: .center, spacing: 10) {
                Image(systemName: "bird.fill")
                    .font(.system(size: 24, weight: .medium))
                    .foregroundStyle(ParrotPalette.amber)
                    .accessibilityHidden(true)
                VStack(alignment: .leading, spacing: 1) {
                    Text("PARROT")
                        .font(.system(size: 14, weight: .bold, design: .rounded))
                        .tracking(2)
                    Text("STUDIO / LOCAL")
                        .font(.system(size: 9, weight: .medium, design: .monospaced))
                        .tracking(1.1)
                        .foregroundStyle(ParrotPalette.muted)
                }
            }
            .padding(.bottom, 36)
            }

            if horizontal {
                HStack(spacing: 6) {
                    ForEach(StudioSection.allCases) { section in
                        navigationButton(section)
                    }
                }
            } else {
                VStack(spacing: 5) {
                    ForEach(StudioSection.allCases) { section in
                        navigationButton(section)
                    }
                }
            }

            if !horizontal {
                Spacer(minLength: 16)
                Rectangle().fill(ParrotPalette.line).frame(height: 1)
                Text(model.isPreview ? "PREVIEW · SIMULATED" : "ON-DEVICE DICTATION")
                    .font(.system(size: 9, weight: .medium, design: .monospaced))
                    .tracking(0.7)
                    .foregroundStyle(ParrotPalette.muted)
                    .padding(.top, 15)
            }
        }
        .padding(horizontal ? 18 : 22)
        .padding(.top, horizontal ? 0 : 30)
        .padding(.bottom, horizontal ? 0 : 26)
        .frame(maxWidth: .infinity, alignment: .leading)
        .background(ParrotPalette.panel)
    }

    private func navigationButton(_ section: StudioSection) -> some View {
        Button {
            selection = section
        } label: {
            HStack(spacing: 10) {
                Image(systemName: section.icon)
                    .frame(width: 17)
                    .accessibilityHidden(true)
                Text(section.title)
                Spacer(minLength: 0)
            }
            .font(.system(size: 12, weight: selection == section ? .semibold : .medium))
            .foregroundStyle(selection == section ? ParrotPalette.cream : ParrotPalette.muted)
            .padding(.horizontal, 11)
            .frame(height: 38)
            .background(selection == section ? ParrotPalette.raised : .clear, in: RoundedRectangle(cornerRadius: 8))
            .overlay(alignment: .leading) {
                if selection == section {
                    RoundedRectangle(cornerRadius: 2)
                        .fill(ParrotPalette.amber)
                        .frame(width: 2, height: 18)
                }
            }
        }
        .buttonStyle(.plain)
        .keyboardShortcut(section.shortcut, modifiers: .command)
        .help("\(section.title) (Command-\(section.shortcutLabel))")
        .overlay(RoundedRectangle(cornerRadius: 8).stroke(
            focusedSection == section ? ParrotPalette.amber : .clear, lineWidth: 2))
        .focused($focusedSection, equals: section)
        .accessibilityLabel(section.title)
        .accessibilityAddTraits(selection == section ? .isSelected : [])
        .accessibilityIdentifier("nav\(section.title)")
    }

    @ViewBuilder
    private var content: some View {
        ScrollView {
            VStack(alignment: .leading, spacing: 24) {
                HStack(alignment: .firstTextBaseline) {
                    VStack(alignment: .leading, spacing: 5) {
                        Text(selection.eyebrow)
                            .font(.system(size: 10, weight: .semibold, design: .monospaced))
                            .tracking(1.6)
                            .foregroundStyle(ParrotPalette.amber)
                        Text(selection.title)
                            .font(.system(size: 31, weight: .semibold, design: .rounded))
                            .tracking(-0.7)
                    }
                    Spacer()
                    if model.isPreview {
                        Text("PREVIEW")
                            .font(.system(size: 9, weight: .bold, design: .monospaced))
                            .tracking(0.8)
                            .padding(.horizontal, 9)
                            .padding(.vertical, 6)
                            .background(ParrotPalette.amber.opacity(0.12), in: Capsule())
                            .foregroundStyle(ParrotPalette.amber)
                    }
                }

                switch selection {
                case .overview:
                    overview
                case .vocabulary:
                    PhraseEditor(model: model, kind: .vocabulary).id(PhraseKind.vocabulary)
                case .snippets:
                    PhraseEditor(model: model, kind: .snippets).id(PhraseKind.snippets)
                }
            }
            .padding(.horizontal, 32)
            .padding(.vertical, 30)
            .frame(maxWidth: .infinity, alignment: .leading)
        }
        .scrollIndicators(.automatic)
        .animation(reduceMotion ? nil : .easeOut(duration: 0.16), value: selection)
    }

    private var overview: some View {
        VStack(alignment: .leading, spacing: 20) {
            VStack(alignment: .leading, spacing: 20) {
                HStack(alignment: .top, spacing: 16) {
                    Circle()
                        .fill(model.runState == .recording ? ParrotPalette.red : ParrotPalette.amber)
                        .frame(width: 9, height: 9)
                        .padding(.top, 9)
                        .accessibilityHidden(true)
                    VStack(alignment: .leading, spacing: 6) {
                        Text(model.runState.title)
                            .font(.system(size: 23, weight: .medium, design: .rounded))
                        Text(model.runState.detail)
                            .font(.system(size: 12))
                            .foregroundStyle(ParrotPalette.muted)
                            .fixedSize(horizontal: false, vertical: true)
                    }
                    Spacer()
                    if let seconds = model.elapsedSeconds, model.runState == .recording {
                        Text(Self.duration(seconds))
                            .font(.system(size: 17, weight: .medium, design: .monospaced))
                            .monospacedDigit()
                            .foregroundStyle(ParrotPalette.amber)
                            .accessibilityLabel("Recording duration \(Int(seconds)) seconds")
                    }
                }
                LevelMeter(levels: model.levels, active: model.runState == .recording)
                    .frame(height: 60)
            }
            .padding(22)
            .frame(maxWidth: .infinity, alignment: .leading)
            .background(ParrotPalette.panel, in: RoundedRectangle(cornerRadius: 13))
            .overlay(RoundedRectangle(cornerRadius: 13).stroke(ParrotPalette.line.opacity(0.7)))

            VStack(alignment: .leading, spacing: 12) {
                Text("Make it your wording")
                    .font(.system(size: 17, weight: .semibold, design: .rounded))
                if let error = model.storeError {
                    Label(error, systemImage: "exclamationmark.circle")
                        .font(.system(size: 11))
                        .foregroundStyle(ParrotPalette.red)
                        .fixedSize(horizontal: false, vertical: true)
                }
                VStack(spacing: 0) {
                    toolRow(.vocabulary, detail: "Correct names and recurring phrases.",
                            count: model.canEditPreferences ? model.preferences.replacements.count : nil)
                    Rectangle().fill(ParrotPalette.line).frame(height: 1)
                    toolRow(.snippets, detail: "Say a short trigger. Insert saved text.",
                            count: model.canEditPreferences ? model.preferences.snippets.count : nil)
                }
                .background(ParrotPalette.panel, in: RoundedRectangle(cornerRadius: 10))
            }

            HStack(spacing: 6) {
                Image(systemName: "desktopcomputer").accessibilityHidden(true)
                Text(model.isPreview ? "Simulated, no audio" : "Processed on this Mac")
                Text("·")
                Text(model.modelID).lineLimit(1).truncationMode(.middle)
            }
            .font(.system(size: 11))
            .foregroundStyle(ParrotPalette.muted)

            Text(model.isPreview
                 ? "Preview only. No audio is captured or transcribed. The phrase examples are synthetic."
                 : "Parrot transcribes locally. Vocabulary and Snippets change the text after transcription.")
                .font(.system(size: 11))
                .foregroundStyle(ParrotPalette.muted)
                .fixedSize(horizontal: false, vertical: true)
        }
    }

    private func toolRow(_ section: StudioSection, detail: String, count: Int?) -> some View {
        Button { selection = section } label: {
            HStack(spacing: 13) {
                Image(systemName: section.icon)
                    .font(.system(size: 17)).foregroundStyle(ParrotPalette.amber)
                    .frame(width: 23).accessibilityHidden(true)
                VStack(alignment: .leading, spacing: 4) {
                    Text(section.title).font(.system(size: 13, weight: .semibold))
                    Text(detail).font(.system(size: 11)).foregroundStyle(ParrotPalette.muted)
                }
                Spacer(minLength: 6)
                Text(count.map { "\($0) saved" } ?? "Unavailable").font(.system(size: 10, design: .monospaced))
                    .foregroundStyle(ParrotPalette.muted)
                Image(systemName: "chevron.right").font(.system(size: 10, weight: .semibold))
                    .foregroundStyle(ParrotPalette.amber).accessibilityHidden(true)
            }
            .padding(16).contentShape(Rectangle())
        }
        .buttonStyle(.plain)
        .accessibilityLabel("Open \(section.title). \(detail) \(count.map { "\($0) saved." } ?? "Saved entries unavailable.")")
    }

    private static func duration(_ seconds: TimeInterval) -> String {
        let value = max(0, Int(seconds))
        return String(format: "%02d:%02d", value / 60, value % 60)
    }
}

enum StudioSection: String, CaseIterable, Identifiable {
    case overview, vocabulary, snippets
    var id: String { rawValue }
    var title: String { rawValue.capitalized }
    var shortcut: KeyEquivalent {
        switch self { case .overview: "1"; case .vocabulary: "2"; case .snippets: "3" }
    }
    var shortcutLabel: String {
        switch self { case .overview: "1"; case .vocabulary: "2"; case .snippets: "3" }
    }
    var eyebrow: String {
        switch self {
        case .overview: "ON THIS MAC"
        case .vocabulary: "LOCAL TEXT RULES"
        case .snippets: "QUICK EXPANSIONS"
        }
    }
    var icon: String {
        switch self {
        case .overview: "waveform"
        case .vocabulary: "text.book.closed"
        case .snippets: "text.quote"
        }
    }
}

struct LevelMeter: View {
    let levels: [Float]
    let active: Bool
    @Environment(\.accessibilityReduceMotion) private var reduceMotion

    var body: some View {
        HStack(alignment: .bottom, spacing: 4) {
            ForEach(Array(levels.enumerated()), id: \.offset) { index, value in
                RoundedRectangle(cornerRadius: 2)
                    .fill(index > 14 && value > 0.75 ? ParrotPalette.red : ParrotPalette.amber)
                    .opacity(active ? 0.5 + Double(value) * 0.5 : 0.2)
                    .frame(maxWidth: .infinity)
                    .frame(height: 56)
                    .scaleEffect(y: active ? max(0.09, CGFloat(value)) : 0.09, anchor: .bottom)
                    .animation(reduceMotion ? nil : .easeOut(duration: 0.09), value: value)
            }
        }
        .accessibilityElement(children: .ignore)
        .accessibilityLabel(active ? "Recording level indicator" : "Recording level inactive")
    }
}

enum PhraseKind: Hashable {
    case vocabulary, snippets
    var title: String { self == .vocabulary ? "Vocabulary" : "Snippets" }
    var helper: String {
        self == .vocabulary
            ? "Replace whole spoken phrases in the final text. This does not retrain the speech model."
            : "Expand an exact full utterance into saved text after transcription."
    }
    var empty: String {
        self == .vocabulary ? "No vocabulary entries yet." : "No snippets yet."
    }
    var targetLabel: String { self == .vocabulary ? "Write as" : "Expand to" }
}

enum SavedPhraseSearch {
    static func matches(_ entry: PhraseReplacement, query: String) -> Bool {
        let term = query.trimmingCharacters(in: .whitespacesAndNewlines)
        return term.isEmpty || entry.spoken.localizedStandardContains(term)
            || entry.replacement.localizedStandardContains(term)
    }

}

private struct PhraseEditor: View {
    @ObservedObject var model: StudioModel
    let kind: PhraseKind
    @FocusState private var spokenFocused: Bool
    @State private var query = ""
    @State private var feedback: String?
    @State private var showMatchingHelp = false

    private var visibleEntries: [PhraseReplacement] {
        entries.filter { SavedPhraseSearch.matches($0, query: query) }
    }


    private var draft: PhraseDraft? { model.draft(for: kind) }

    private var entries: [PhraseReplacement] {
        kind == .vocabulary ? model.preferences.replacements : model.preferences.snippets
    }

    var body: some View {
        VStack(alignment: .leading, spacing: 18) {
            Text(kind.helper)
                .font(.system(size: 12))
                .foregroundStyle(ParrotPalette.muted)
                .fixedSize(horizontal: false, vertical: true)

            DisclosureGroup("How matching works", isExpanded: $showMatchingHelp) {
                VStack(alignment: .leading, spacing: 7) {
                    Text(kind == .vocabulary
                         ? "Example: voice ink → VoiceInk. A whole phrase can match inside a longer sentence."
                         : "Example: my sign off → Thanks, Parrot user. Say the trigger on its own, not inside a longer sentence.")
                    Text("Examples only. Nothing is added until you save an entry.")
                        .foregroundStyle(ParrotPalette.muted)
                }
                .font(.system(size: 11)).padding(.top, 8)
                .fixedSize(horizontal: false, vertical: true)
            }
            .font(.system(size: 11, weight: .medium))

            if let error = model.storeError {
                HStack(alignment: .top, spacing: 8) {
                    Image(systemName: "exclamationmark.circle")
                    Text(error)
                }
                .font(.system(size: 11))
                .foregroundStyle(ParrotPalette.red)
                .padding(12)
                .frame(maxWidth: .infinity, alignment: .leading)
                .background(ParrotPalette.red.opacity(0.09), in: RoundedRectangle(cornerRadius: 8))
                .accessibilityAddTraits(.updatesFrequently)
            }

            if let feedback, model.storeError == nil {
                Label(feedback, systemImage: "checkmark.circle")
                    .font(.system(size: 12)).foregroundStyle(ParrotPalette.amber)
                    .accessibilityAddTraits(.updatesFrequently)
            }

            if !model.canEditPreferences && draft == nil {
                Text("Saved entries unavailable. Reopen Studio to reload your local settings.")
                    .font(.system(size: 12))
                    .foregroundStyle(ParrotPalette.muted)
                    .fixedSize(horizontal: false, vertical: true)
            }
            if model.canEditPreferences && entries.isEmpty && draft == nil {
                VStack(spacing: 10) {
                    Image(systemName: kind == .vocabulary ? "text.book.closed" : "text.quote")
                        .font(.system(size: 23))
                        .foregroundStyle(ParrotPalette.amber)
                    Text(kind.empty)
                        .font(.system(size: 14, weight: .medium))
                    Text(kind == .vocabulary
                         ? "Save a name or phrase exactly as you want it written."
                         : "Save text you often repeat, then give it a distinct trigger.")
                        .font(.system(size: 11))
                        .foregroundStyle(ParrotPalette.muted)
                }
                .frame(maxWidth: .infinity)
                .padding(.vertical, 28)
                .background(ParrotPalette.panel, in: RoundedRectangle(cornerRadius: 12))
            }

            if model.canEditPreferences && !entries.isEmpty && draft == nil {
                HStack {
                    Text("\(entries.count) saved")
                        .font(.system(size: 11, weight: .medium, design: .monospaced))
                        .foregroundStyle(ParrotPalette.muted)
                    Spacer()
                    TextField("Find wording", text: $query)
                        .textFieldStyle(.roundedBorder)
                        .frame(maxWidth: 210)
                        .accessibilityLabel("Find saved \(kind.title)")
                }
                if visibleEntries.isEmpty {
                    VStack(alignment: .leading, spacing: 8) {
                        Text("No saved entries match your search.")
                        Button("Clear search") { query = "" }
                    }
                    .font(.system(size: 12)).padding(16)
                }
                VStack(spacing: 0) {
                    ForEach(visibleEntries) { entry in
                        HStack(alignment: .center, spacing: 14) {
                            VStack(alignment: .leading, spacing: 4) {
                                Text(entry.spoken)
                                    .font(.system(size: 13, weight: .semibold))
                                Text(entry.replacement)
                                    .font(.system(size: 12))
                                    .foregroundStyle(ParrotPalette.muted)
                                    .lineLimit(2)
                            }
                            Spacer(minLength: 10)
                            Button("Edit") { beginEditing(entry) }
                                .accessibilityLabel("Edit \(entry.spoken)")
                                .disabled(!model.canEditPreferences || draft != nil)
                            Button {
                                remove(entry)
                            } label: {
                                Image(systemName: "trash")
                            }
                            .accessibilityLabel("Remove \(entry.spoken)")
                            .help("Remove \(entry.spoken)")
                            .disabled(!model.canEditPreferences || draft != nil)
                        }
                        .buttonStyle(.borderless)
                        .padding(.horizontal, 16)
                        .padding(.vertical, 13)
                        if entry.id != visibleEntries.last?.id {
                            Rectangle().fill(ParrotPalette.line).frame(height: 1)
                        }
                    }
                }
                .background(ParrotPalette.panel, in: RoundedRectangle(cornerRadius: 12))
            }

            if let draft {
                VStack(alignment: .leading, spacing: 14) {
                    Text(draft.isNew ? "NEW ENTRY" : "EDIT ENTRY")
                        .font(.system(size: 10, weight: .semibold, design: .monospaced))
                        .tracking(1.2)
                        .foregroundStyle(ParrotPalette.amber)
                    VStack(alignment: .leading, spacing: 5) {
                        Text("When I say")
                            .font(.system(size: 11, weight: .medium))
                        TextField("Spoken phrase", text: Binding(
                            get: { model.draft(for: kind)?.spoken ?? "" },
                            set: { model.updateSpoken($0, for: kind) }
                        ))
                            .textFieldStyle(.roundedBorder)
                            .focused($spokenFocused)
                            .accessibilityLabel("When I say")
                            .accessibilityIdentifier("spokenPhrase")
                    }
                    VStack(alignment: .leading, spacing: 5) {
                        Text(kind.targetLabel)
                            .font(.system(size: 11, weight: .medium))
                        TextField("Replacement text", text: Binding(
                            get: { model.draft(for: kind)?.replacement ?? "" },
                            set: { model.updateReplacement($0, for: kind) }
                        ), axis: .vertical)
                            .textFieldStyle(.roundedBorder)
                            .lineLimit(2...5)
                            .accessibilityLabel(kind.targetLabel)
                            .accessibilityIdentifier("replacementText")
                    }
                    Text("Unsaved draft. Switching sections keeps your edits.")
                        .font(.system(size: 11)).foregroundStyle(ParrotPalette.muted)
                    if draft.spoken.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty ||
                        draft.replacement.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty {
                        Text("Fill in both fields to save.")
                            .font(.system(size: 11)).foregroundStyle(ParrotPalette.muted)
                    }
                    HStack {
                        Button("Discard draft") { cancelEditing() }
                        Button("Save \(kind == .vocabulary ? "wording" : "snippet")") { saveEditing() }
                            .keyboardShortcut(.return, modifiers: .command)
                            .disabled(!model.canEditPreferences ||
                                      draft.spoken.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty ||
                                      draft.replacement.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty)
                    }
                    .buttonStyle(.bordered)
                }
                .padding(18)
                .frame(maxWidth: .infinity, alignment: .leading)
                .background(ParrotPalette.panel, in: RoundedRectangle(cornerRadius: 12))
            } else {
                Button {
                    feedback = nil
                    query = ""
                    model.startDraft(for: kind)
                    spokenFocused = true
                } label: {
                    Label("Add \(kind == .vocabulary ? "wording" : "snippet")", systemImage: "plus")
                }
                .buttonStyle(.borderedProminent)
                .keyboardShortcut("n", modifiers: .command)
                .help("Add an entry (Command-N)")
                .accessibilityIdentifier("addPhrase")
                .disabled(!model.canEditPreferences)
            }
        }
        .onAppear { if draft != nil { spokenFocused = true } }
    }

    private func beginEditing(_ entry: PhraseReplacement) {
        feedback = nil
        model.startDraft(for: kind, editing: entry)
        spokenFocused = true
    }

    private func cancelEditing() {
        model.clearDraft(for: kind)
    }

    private func saveEditing() {
        guard let draft else { return }
        let nextEntry = PhraseReplacement(
            id: draft.id,
            spoken: draft.spoken.trimmingCharacters(in: .whitespacesAndNewlines),
            replacement: draft.replacement.trimmingCharacters(in: .whitespacesAndNewlines)
        )
        var next = model.preferences
        if kind == .vocabulary {
            next.replacements.removeAll { $0.id == nextEntry.id }
            next.replacements.append(nextEntry)
        } else {
            next.snippets.removeAll { $0.id == nextEntry.id }
            next.snippets.append(nextEntry)
        }
        if model.save(next) {
            feedback = "\(kind == .vocabulary ? "Wording" : "Snippet") saved."
            query = ""
            cancelEditing()
        }
    }

    private func remove(_ entry: PhraseReplacement) {
        var next = model.preferences
        if kind == .vocabulary {
            next.replacements.removeAll { $0.id == entry.id }
        } else {
            next.snippets.removeAll { $0.id == entry.id }
        }
        if model.save(next) {
            feedback = "Entry removed."
            if draft?.id == entry.id { cancelEditing() }
        }
    }
}
