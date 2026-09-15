import Testing
@testable import parrot

@Test func smokeAcceptsPunctuationAndCapitalization() {
    #expect(smokeTranscriptMatches("Parrot local transcription test."))
    #expect(smokeTranscriptMatches("PARROT, local transcription test!"))
}

@Test func smokeRejectsIncorrectNonemptyTranscripts() {
    #expect(!smokeTranscriptMatches("Hello world"))
    #expect(!smokeTranscriptMatches("Parrot local transcription"))
    #expect(!smokeTranscriptMatches("Parrot local translation test"))
    #expect(!smokeTranscriptMatches("Parrot local transcription test test"))
    #expect(!smokeTranscriptMatches(""))
}
