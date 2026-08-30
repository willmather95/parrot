import FluidAudio
import Darwin
import Foundation

/// NVIDIA Parakeet TDT 0.6B v2 through FluidAudio's Core ML runtime.
/// The model downloads once, then runs entirely on-device on Apple Silicon.
actor ParakeetTranscriber: Transcriber {
    let modelID: String
    private var manager: AsrManager?
    private var isInferenceWarm = false

    init(model: TranscriptionModel) {
        self.modelID = model.id
    }

    func warmUp() async throws {
        guard !isInferenceWarm else { return }

        if manager == nil {
            Self.cleanupStaleTemporaryAudio()
            FileHandle.standardError.write(Data("loading \(modelID)...\n".utf8))
            let models = try await AsrModels.downloadAndLoad(version: .v2)
            FileHandle.standardError.write(Data("loading Parakeet Core ML models...\n".utf8))
            let manager = AsrManager()
            try await manager.loadModels(models)
            self.manager = manager
        }

        guard let manager else { throw ParakeetTranscriberError.notLoaded }
        let started = ProcessInfo.processInfo.systemUptime
        FileHandle.standardError.write(Data("warming \(modelID) inference...\n".utf8))
        let url = FileManager.default.temporaryDirectory
            .appendingPathComponent(
                "parrot-warmup-\(getpid())-\(UUID().uuidString).wav"
            )
        defer { try? FileManager.default.removeItem(at: url) }
        try WAVWriter.write(
            samples: Self.inferenceWarmupSamples(),
            sampleRate: Int(AudioCapture.targetSampleRate),
            to: url.path
        )
        _ = try await transcribeFile(url, using: manager)
        isInferenceWarm = true
        let elapsed = ProcessInfo.processInfo.systemUptime - started
        FileHandle.standardError.write(Data(
            String(format: "✓ \(modelID) ready · inference warmup %.2fs\n", elapsed).utf8
        ))
    }

    func transcribe(_ audio: [Float]) async throws -> String {
        if !isInferenceWarm { try await warmUp() }

        let url = FileManager.default.temporaryDirectory
            .appendingPathComponent(
                "parrot-audio-\(getpid())-\(UUID().uuidString).wav"
            )
        defer { try? FileManager.default.removeItem(at: url) }
        try WAVWriter.write(samples: audio, sampleRate: Int(AudioCapture.targetSampleRate), to: url.path)

        return try await transcribeFile(url)
    }

    func transcribeFile(_ url: URL) async throws -> String {
        if !isInferenceWarm { try await warmUp() }
        guard let manager else { throw ParakeetTranscriberError.notLoaded }

        return try await transcribeFile(url, using: manager)
    }

    private func transcribeFile(_ url: URL, using manager: AsrManager) async throws -> String {
        var decoderState = try TdtDecoderState()
        let result = try await manager.transcribe(url, decoderState: &decoderState)
        return result.text.trimmingCharacters(in: .whitespacesAndNewlines)
    }

    /// One second of deterministic, low-amplitude synthetic audio forces the
    /// same Core ML prediction path as dictation without recording, playing,
    /// retaining, or learning from user audio. Its transcript is discarded.
    static func inferenceWarmupSamples(sampleRate: Int = Int(AudioCapture.targetSampleRate)) -> [Float] {
        let safeSampleRate = max(1, sampleRate)
        let frequency = 220.0
        let amplitude = 0.002
        return (0..<safeSampleRate).map { index in
            Float(sin(2 * Double.pi * frequency * Double(index) / Double(safeSampleRate)) * amplitude)
        }
    }

    private static func cleanupStaleTemporaryAudio() {
        let directory = FileManager.default.temporaryDirectory
        guard let urls = try? FileManager.default.contentsOfDirectory(
            at: directory,
            includingPropertiesForKeys: [.contentModificationDateKey],
            options: [.skipsHiddenFiles]
        ) else { return }

        for url in urls where url.lastPathComponent.hasPrefix("parrot-") && url.pathExtension == "wav" {
            let name = url.deletingPathExtension().lastPathComponent

            // Current files include their creator PID. warmUp runs before this
            // actor creates audio, so same-PID files are leftovers from a rare
            // PID reuse and are safe to remove. Legacy UUID-only names also
            // belong to an earlier Parrot run.
            guard let ownerPID = temporaryInferenceOwnerPID(from: name) else {
                try? FileManager.default.removeItem(at: url)
                continue
            }
            if ownerPID == getpid() {
                try? FileManager.default.removeItem(at: url)
                continue
            }

            errno = 0
            if Darwin.kill(ownerPID, 0) == -1, errno == ESRCH {
                try? FileManager.default.removeItem(at: url)
            }
        }
    }

    /// Audio and warmup inference files share the same PID-aware lifetime.
    /// Preserving files owned by another live process prevents concurrent
    /// setup, doctor, smoke, and daemon runs from interrupting one another.
    static func temporaryInferenceOwnerPID(from name: String) -> Int32? {
        let components = name.split(separator: "-")
        guard components.count >= 4,
              components[0] == "parrot",
              components[1] == "audio" || components[1] == "warmup"
        else { return nil }
        return Int32(components[2])
    }
}

enum ParakeetTranscriberError: Error {
    case notLoaded
    case emptyResult
}
