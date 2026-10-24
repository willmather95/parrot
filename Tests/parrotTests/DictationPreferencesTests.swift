import Foundation
import Testing
@testable import parrot

private func phrase(_ spoken: String, _ replacement: String) -> PhraseReplacement {
    PhraseReplacement(spoken: spoken, replacement: replacement)
}

@Test func emptyRulesPreserveEveryCharacter() throws {
    let original = "  Hello, world!\n$1 and \\path  "
    #expect(try DictationTextRules.apply(original, preferences: .empty) == original)
}

@Test func replacementsUseWholeUnicodeWordsAndLiteralOutput() throws {
    let settings = DictationPreferences(replacements: [phrase("acme", "$1\\company"), phrase("café", "Coffee")])
    #expect(try DictationTextRules.apply("ACME, acmes acme_1 xacme acmeé café!", preferences: settings)
            == "$1\\company, acmes acme_1 xacme acmeé Coffee!")
}

@Test func longestReplacementWinsWithoutCascading() throws {
    let settings = DictationPreferences(replacements: [phrase("new", "old"), phrase("new york", "NYC"), phrase("NYC", "wrong")])
    #expect(try DictationTextRules.apply("New York and new and NYC", preferences: settings) == "NYC and old and wrong")
}

@Test func regexSymbolsAreLiteralAndHorizontalSpacingWorks() throws {
    let settings = DictationPreferences(replacements: [phrase("c++", "C Plus Plus"), phrase("my company", "ACME")])
    #expect(try DictationTextRules.apply("c++ my\tcompany my\ncompany", preferences: settings)
            == "C Plus Plus ACME my\ncompany")
}

@Test func snippetsRequireEntireUtteranceAndNeverCascade() throws {
    let settings = DictationPreferences(replacements: [phrase("hello", "goodbye")], snippets: [phrase("my sign off", "hello\nBill")])
    #expect(try DictationTextRules.apply(" My SIGN off.!? \n", preferences: settings) == "hello\nBill")
    #expect(try DictationTextRules.apply("Please use my sign off.", preferences: settings) == "Please use my sign off.")
}

@Test func invalidAndDuplicateRulesAreRejected() {
    #expect(throws: (any Error).self) { try DictationPreferences(replacements: [phrase("!!!", "x")]).validated() }
    #expect(throws: (any Error).self) { try DictationPreferences(replacements: [phrase("Acme", "a")], snippets: [phrase(" acme ", "b")]).validated() }
    #expect(throws: (any Error).self) { try DictationPreferences(replacements: [phrase("a", "\u{0}")]).validated() }
    #expect(throws: (any Error).self) { try DictationPreferences(replacements: [phrase("a", String(repeating: "x", count: 201))]).validated() }
    #expect(throws: (any Error).self) { try DictationPreferences(snippets: [phrase("a", String(repeating: "x", count: 4001))]).validated() }
    let entry = phrase("one", "1")
    var duplicate = phrase("two", "2"); duplicate.id = entry.id
    #expect(throws: (any Error).self) { try DictationPreferences(replacements: [entry, duplicate]).validated() }
}

private func withSettings(_ body: (URL) throws -> Void) throws {
    let directory = FileManager.default.temporaryDirectory.appendingPathComponent("parrot-test-\(UUID())")
    try FileManager.default.createDirectory(at: directory, withIntermediateDirectories: true)
    defer { try? FileManager.default.removeItem(at: directory) }
    try body(directory.appendingPathComponent("settings.json"))
}

@Test func settingsPreserveModelAndRulesInBothDirections() throws {
    try withSettings { url in
        try Data("{\"selectedModelID\":\"original\"}".utf8).write(to: url)
        #expect(try ParrotSettings.dictationPreferences(at: url) == .empty)
        let settings = DictationPreferences(replacements: [phrase("acme", "ACME")])
        try ParrotSettings.saveDictationPreferences(settings, at: url)
        #expect(try ParrotSettings.decodeSelectedModelID(from: Data(contentsOf: url)) == "original")
        try ParrotSettings.select(modelID: "next", at: url)
        #expect(try ParrotSettings.dictationPreferences(at: url) == settings)
        #expect(try ParrotSettings.decodeSelectedModelID(from: Data(contentsOf: url)) == "next")
        let attrs = try FileManager.default.attributesOfItem(atPath: url.path)
        #expect((attrs[.posixPermissions] as? NSNumber)?.intValue == 0o600)
    }
}

@Test func corruptOrInvalidSettingsAreNotOverwritten() throws {
    try withSettings { url in
        let original = Data("{corrupt".utf8); try original.write(to: url)
        #expect(throws: (any Error).self) { try ParrotSettings.select(modelID: "next", at: url) }
        #expect(try Data(contentsOf: url) == original)
        #expect(throws: (any Error).self) { try ParrotSettings.saveDictationPreferences(.init(replacements: [phrase("", "x")]), at: url) }
        #expect(try Data(contentsOf: url) == original)
    }
}

@Test func settingsRejectSymlinksIncludingDanglingLinks() throws {
    try withSettings { url in
        let target = url.deletingLastPathComponent().appendingPathComponent("missing.json")
        try FileManager.default.createSymbolicLink(at: url, withDestinationURL: target)
        #expect(throws: (any Error).self) { try ParrotSettings.saveDictationPreferences(.empty, at: url) }
        #expect(try FileManager.default.destinationOfSymbolicLink(atPath: url.path) == target.path)
    }
}

@Test func settingsRejectOversizedFileWithoutChangingIt() throws {
    try withSettings { url in
        let data = Data(repeating: 32, count: 2_000_001); try data.write(to: url)
        #expect(throws: (any Error).self) { try ParrotSettings.select(modelID: "next", at: url) }
        #expect(try Data(contentsOf: url).count == data.count)
    }
}

@Test func settingsSupportConcurrentIndependentWriters() async throws {
    let directory = FileManager.default.temporaryDirectory.appendingPathComponent("parrot-test-\(UUID())")
    try FileManager.default.createDirectory(at: directory, withIntermediateDirectories: true)
    defer { try? FileManager.default.removeItem(at: directory) }
    let url = directory.appendingPathComponent("settings.json")
    let settings = DictationPreferences(replacements: [phrase("acme", "ACME")])
    try await withThrowingTaskGroup(of: Void.self) { group in
        for _ in 0..<12 {
            group.addTask { try ParrotSettings.select(modelID: "chosen", at: url) }
            group.addTask { try ParrotSettings.saveDictationPreferences(settings, at: url) }
        }
        try await group.waitForAll()
    }
    #expect(try ParrotSettings.dictationPreferences(at: url) == settings)
    #expect(try ParrotSettings.decodeSelectedModelID(from: Data(contentsOf: url)) == "chosen")
}

@Test func punctuationEndingSnippetTriggersAreNormalizedConsistently() throws {
    let settings = DictationPreferences(snippets: [phrase("my sign off.!?", "hello")])
    #expect(try settings.validated().snippets.first?.spoken == "my sign off")
    #expect(try DictationTextRules.apply("My sign off.", preferences: settings) == "hello")
    #expect(try DictationTextRules.apply("my sign off", preferences: settings) == "hello")
    #expect(throws: (any Error).self) {
        try DictationPreferences(snippets: [phrase("hello", "one"), phrase("hello.", "two")]).validated()
    }
}

@Test func stalePhraseWriterCannotEraseAnotherSessionsEntries() throws {
    try withSettings { url in
        let first = DictationPreferences(replacements: [phrase("first", "1")])
        let second = DictationPreferences(replacements: [phrase("second", "2")])
        try ParrotSettings.saveDictationPreferences(first, at: url, expecting: .empty)
        #expect(throws: (any Error).self) {
            try ParrotSettings.saveDictationPreferences(second, at: url, expecting: .empty)
        }
        #expect(try ParrotSettings.dictationPreferences(at: url) == first)
        try ParrotSettings.select(modelID: "model-selected-elsewhere", at: url)
        var merged = first; merged.replacements += second.replacements
        try ParrotSettings.saveDictationPreferences(merged, at: url, expecting: first)
        #expect(try ParrotSettings.dictationPreferences(at: url) == merged)
        #expect(try ParrotSettings.decodeSelectedModelID(from: Data(contentsOf: url)) == "model-selected-elsewhere")
    }
}

@Test func canonicallyEquivalentAccentsMatchWithoutChangingUnmatchedText() throws {
    let settings = DictationPreferences(replacements: [phrase("café", "Coffee")])
    #expect(try DictationTextRules.apply("cafe\u{301}!", preferences: settings) == "Coffee!")
    let unmatched = "re\u{301}sume\u{301}"
    #expect(try DictationTextRules.apply(unmatched, preferences: settings).utf8.elementsEqual(unmatched.utf8))
}

@Test func wholePhraseRulesDoNotCorruptContractionsOrHyphenatedWords() throws {
    let settings = DictationPreferences(replacements: [phrase("can", "may")])
    #expect(try DictationTextRules.apply("I can't and can’t, can-do and can.", preferences: settings)
            == "I can't and can’t, can-do and may.")
}
