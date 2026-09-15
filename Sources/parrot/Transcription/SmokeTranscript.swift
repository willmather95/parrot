import Foundation

/// A nonempty transcript alone does not prove the model recognized the fixture.
/// Tolerate punctuation/case, but require all expected words in their order.
func smokeTranscriptMatches(_ text: String) -> Bool {
    let words = text.lowercased().split { !$0.isLetter }.map(String.init)
    return words == ["parrot", "local", "transcription", "test"]
}

enum SmokeVerificationError: Error, CustomStringConvertible {
    case incorrectTranscript

    var description: String {
        "local speech smoke failed: transcript did not match the generated fixture"
    }
}
