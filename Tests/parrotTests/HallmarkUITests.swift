import Foundation
import Testing
@testable import parrot

@Test func phraseSearchFindsSpokenAndWrittenTextWithoutChangingEntries() {
    let entry = PhraseReplacement(spoken: "orchard alias", replacement: "Café Orchard\nSecond line")
    #expect(SavedPhraseSearch.matches(entry, query: " CAFE "))
    #expect(SavedPhraseSearch.matches(entry, query: "orchard"))
    #expect(SavedPhraseSearch.matches(entry, query: "second line"))
    #expect(!SavedPhraseSearch.matches(entry, query: "missing"))
    #expect(entry.replacement == "Café Orchard\nSecond line")
}

@Test func blankPhraseSearchShowsAllEntries() {
    let entry = PhraseReplacement(spoken: "my sign off", replacement: "Thanks")
    #expect(SavedPhraseSearch.matches(entry, query: ""))
    #expect(SavedPhraseSearch.matches(entry, query: " \n\t"))
}

private enum HallmarkLoadFailure: Error { case unavailable }

@MainActor @Test func failedReloadKeepsDraftButMarksCachedEntriesUnavailable() {
    let model = StudioModel(modelID: "test")
    let saved = DictationPreferences(replacements: [.init(spoken: "company", replacement: "ACME")])
    var loadFails = false
    model.configurePreferences(load: {
        if loadFails { throw HallmarkLoadFailure.unavailable }
        return saved
    }, save: { _ in })
    model.startDraft(for: .vocabulary)
    model.updateSpoken("keep my draft", for: .vocabulary)
    loadFails = true
    model.reloadPreferences()
    #expect(!model.canEditPreferences)
    #expect(model.storeError != nil)
    #expect(model.preferences == saved)
    #expect(model.draft(for: .vocabulary)?.spoken == "keep my draft")
    #expect(!model.save(.empty))
}
