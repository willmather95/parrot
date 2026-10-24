import Foundation

struct PhraseReplacement: Codable, Identifiable, Equatable, Sendable {
    var id: UUID
    var spoken: String
    var replacement: String

    init(id: UUID = UUID(), spoken: String, replacement: String) {
        self.id = id; self.spoken = spoken; self.replacement = replacement
    }
}

struct DictationPreferences: Codable, Equatable, Sendable {
    var replacements: [PhraseReplacement]
    var snippets: [PhraseReplacement]
    static let empty = Self()

    init(replacements: [PhraseReplacement] = [], snippets: [PhraseReplacement] = []) {
        self.replacements = replacements; self.snippets = snippets
    }

    func validated() throws -> Self {
        guard replacements.count <= 100, snippets.count <= 100 else {
            throw DictationPreferencesError.invalid("Keep each list to 100 entries or fewer.")
        }
        var seen = Set<String>()
        var identifiers = Set<UUID>()
        func check(_ entry: PhraseReplacement, limit: Int, snippet: Bool = false) throws -> PhraseReplacement {
            let spoken = snippet ? Self.snippetKey(entry.spoken) : Self.cleanTrigger(entry.spoken)
            let replacement = entry.replacement.trimmingCharacters(in: .whitespacesAndNewlines)
            guard !spoken.isEmpty, spoken.count <= 80,
                  spoken.unicodeScalars.contains(where: { CharacterSet.alphanumerics.contains($0) }) else {
                throw DictationPreferencesError.invalid("Use a spoken phrase of 1 to 80 characters containing a word or number.")
            }
            guard !replacement.isEmpty, replacement.count <= limit else {
                throw DictationPreferencesError.invalid("Enter replacement text of 1 to \(limit) characters.")
            }
            guard !spoken.unicodeScalars.contains(where: { CharacterSet.controlCharacters.contains($0) }),
                  !replacement.unicodeScalars.contains(where: {
                      CharacterSet.controlCharacters.contains($0) && $0 != "\n" && $0 != "\t" && $0 != "\r"
                  }) else {
                throw DictationPreferencesError.invalid("Remove unsupported control characters from this entry.")
            }
            guard seen.insert(Self.triggerKey(spoken)).inserted else {
                throw DictationPreferencesError.invalid("Each spoken phrase must be unique across Vocabulary and Snippets.")
            }
            guard identifiers.insert(entry.id).inserted else {
                throw DictationPreferencesError.invalid("Each entry needs a unique identifier.")
            }
            return PhraseReplacement(id: entry.id, spoken: spoken, replacement: replacement)
        }
        return try Self(replacements: replacements.map { try check($0, limit: 200) },
                        snippets: snippets.map { try check($0, limit: 4000, snippet: true) })
    }

    static func cleanTrigger(_ value: String) -> String {
        value.components(separatedBy: .whitespacesAndNewlines).filter { !$0.isEmpty }.joined(separator: " ")
            .precomposedStringWithCanonicalMapping
    }
    static func snippetKey(_ value: String) -> String {
        var result = cleanTrigger(value)
        while let last = result.last, ".!?".contains(last) { result.removeLast() }
        return cleanTrigger(result)
    }
    static func triggerKey(_ value: String) -> String {
        cleanTrigger(value).folding(options: [.caseInsensitive], locale: Locale(identifier: "en_US_POSIX"))
    }
}

enum DictationPreferencesError: LocalizedError {
    case invalid(String)
    var errorDescription: String? { if case let .invalid(message) = self { return message }; return nil }
}

enum DictationTextRules {
    static func apply(_ text: String, preferences raw: DictationPreferences) throws -> String {
        let preferences = try raw.validated()
        // Snippets require the entire utterance. They never fire inside a sentence,
        // execute commands, or reprocess their expansion through other rules.
        let key = DictationPreferences.triggerKey(DictationPreferences.snippetKey(text))
        if let snippet = preferences.snippets.first(where: { DictationPreferences.triggerKey($0.spoken) == key }) {
            return snippet.replacement
        }
        guard !preferences.replacements.isEmpty else { return text }
        // Every match refers to the original transcript. Replacement output cannot cascade.
        struct Match { let range: NSRange; let replacement: String; let priority: Int }
        var matches: [Match] = []
        // Normalize only the matching copy so canonically equivalent accents match.
        // With no matches, preserve the original transcript byte-for-byte.
        let matchingText = text.precomposedStringWithCanonicalMapping
        let original = matchingText as NSString
        for (index, entry) in preferences.replacements.enumerated() {
            let phrase = entry.spoken.components(separatedBy: " ").map(NSRegularExpression.escapedPattern(for:))
                .joined(separator: "[\\p{Zs}\\t]+")
            // Apostrophes and hyphens are word-internal: "can" must not rewrite "can't".
            let word = "[\\p{L}\\p{M}\\p{N}_'’\\-‐‑]"
            let pattern = "(?<!" + word + ")" + phrase + "(?!" + word + ")"
            let regex = try NSRegularExpression(pattern: pattern, options: [.caseInsensitive])
            matches += regex.matches(in: matchingText, range: NSRange(location: 0, length: original.length)).map {
                Match(range: $0.range, replacement: entry.replacement, priority: index)
            }
        }
        matches.sort {
            if $0.range.location != $1.range.location { return $0.range.location < $1.range.location }
            if $0.range.length != $1.range.length { return $0.range.length > $1.range.length }
            return $0.priority < $1.priority
        }
        var chosen: [Match] = [], end = 0
        for match in matches where match.range.location >= end {
            chosen.append(match); end = NSMaxRange(match.range)
        }
        guard !chosen.isEmpty else { return text }
        let output = NSMutableString(string: matchingText)
        for match in chosen.reversed() { output.replaceCharacters(in: match.range, with: match.replacement) }
        return output as String
    }
}
