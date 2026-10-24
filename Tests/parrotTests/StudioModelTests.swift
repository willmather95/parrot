import Foundation
import Testing
@testable import parrot

private enum StudioTestFailure: Error { case unavailable }

@MainActor @Test func studioDoesNotWriteAfterFailedLoad() {
    let model = StudioModel(modelID: "test")
    var writes = 0
    model.configurePreferences(load: { throw StudioTestFailure.unavailable }, save: { _ in writes += 1 })
    #expect(!model.canEditPreferences)
    #expect(model.storeError != nil)
    #expect(!model.save(.empty))
    #expect(writes == 0)
}

@MainActor @Test func studioPreservesVisibleRulesOnSaveFailure() {
    let original = DictationPreferences(replacements: [.init(spoken: "acme", replacement: "ACME")])
    let model = StudioModel(modelID: "test")
    model.configurePreferences(load: { original }, save: { _ in throw StudioTestFailure.unavailable })
    #expect(model.canEditPreferences)
    #expect(!model.save(.empty))
    #expect(model.preferences == original)
    #expect(model.storeError != nil)
}

@MainActor @Test func studioSavesAndReflectsOnlyValidatedPreferences() {
    var stored = DictationPreferences.empty
    let model = StudioModel(modelID: "test")
    model.configurePreferences(load: { stored }, save: { stored = $0 })
    let next = DictationPreferences(replacements: [.init(spoken: " my   company ", replacement: " ACME ")])
    #expect(model.save(next))
    #expect(stored == model.preferences)
    #expect(stored.replacements.first?.spoken == "my company")
    #expect(stored.replacements.first?.replacement == "ACME")
    #expect(!model.save(.init(snippets: [.init(spoken: "", replacement: "bad")])) )
    #expect(stored.replacements.count == 1)
}

@MainActor @Test func studioMeterStopsOutsideRecording() {
    let model = StudioModel(modelID: "test")
    model.pushLevel(1)
    #expect(model.levels.allSatisfy { $0 == 0 })
    model.setState(.recording); model.pushLevel(0.1); model.elapsedSeconds = 12
    #expect(model.levels.contains { $0 > 0 })
    model.setState(.transcribing)
    #expect(model.levels.allSatisfy { $0 == 0 })
    #expect(model.elapsedSeconds == nil)
    model.pushLevel(1)
    #expect(model.levels.allSatisfy { $0 == 0 })
}

@MainActor @Test func studioKeepsIndependentDraftsAcrossSectionsAndStoreFailures() {
    let model = StudioModel(modelID: "test")
    model.configurePreferences(load: { .empty }, save: { _ in throw StudioTestFailure.unavailable })
    model.startDraft(for: .vocabulary)
    model.updateSpoken("my company", for: .vocabulary)
    model.updateReplacement("ACME", for: .vocabulary)
    let before = model.draft(for: .vocabulary)
    model.startDraft(for: .snippets)
    model.updateSpoken("my sign off", for: .snippets)
    #expect(model.draft(for: .vocabulary) == before)
    #expect(!model.save(.empty))
    #expect(!model.canEditPreferences)
    #expect(model.draft(for: .vocabulary) == before)
    #expect(model.draft(for: .snippets)?.spoken == "my sign off")
    model.reloadPreferences()
    #expect(model.canEditPreferences)
    #expect(model.draft(for: .vocabulary) == before)
    model.clearDraft(for: .snippets)
    #expect(model.draft(for: .vocabulary) == before)
}

@MainActor @Test func startingAnotherEditCannotEraseAnUnsavedDraft() {
    let model = StudioModel(modelID: "test")
    model.startDraft(for: .vocabulary)
    model.updateSpoken("unsaved", for: .vocabulary)
    model.updateReplacement("Keep this", for: .vocabulary)
    let before = model.draft(for: .vocabulary)
    model.startDraft(for: .vocabulary, editing: .init(spoken: "another", replacement: "Other"))
    #expect(model.draft(for: .vocabulary) == before)
    model.clearDraft(for: .vocabulary)
    model.startDraft(for: .vocabulary, editing: .init(spoken: "another", replacement: "Other"))
    #expect(model.draft(for: .vocabulary)?.spoken == "another")
}
