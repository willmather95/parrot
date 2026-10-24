import Foundation
import Darwin

/// Local-only settings shared by the CLI and Studio. No transcript or audio storage.
enum ParrotSettings {
    private struct Values: Codable {
        var selectedModelID: String?
        var dictationPreferences: DictationPreferences?
    }

    private static var url: URL {
        FileManager.default.homeDirectoryForCurrentUser
            .appendingPathComponent("Library/Application Support/parrot", isDirectory: true)
            .appendingPathComponent("settings.json")
    }

    static func selectedModelID() throws -> String? {
        try readValues(at: url).selectedModelID
    }

    static func decodeSelectedModelID(from data: Data) throws -> String? {
        try JSONDecoder().decode(Values.self, from: data).selectedModelID
    }

    static func select(modelID: String) throws { try select(modelID: modelID, at: url) }
    static func select(modelID: String, at destination: URL) throws {
        try update(at: destination) { $0.selectedModelID = modelID }
    }

    static func dictationPreferences() throws -> DictationPreferences {
        try dictationPreferences(at: url)
    }
    static func dictationPreferences(at source: URL) throws -> DictationPreferences {
        try (readValues(at: source).dictationPreferences ?? .empty).validated()
    }
    static func saveDictationPreferences(_ preferences: DictationPreferences) throws {
        try saveDictationPreferences(preferences, at: url)
    }
    static func saveDictationPreferences(_ preferences: DictationPreferences,
                                         expecting previous: DictationPreferences) throws {
        try saveDictationPreferences(preferences, at: url, expecting: previous)
    }
    static func saveDictationPreferences(_ preferences: DictationPreferences, at destination: URL,
                                         expecting previous: DictationPreferences? = nil) throws {
        let validated = try preferences.validated()
        try update(at: destination) { values in
            if let previous, try (values.dictationPreferences ?? .empty).validated() != previous.validated() {
                throw ParrotSettingsError.conflict
            }
            values.dictationPreferences = validated
        }
    }

    private static func readValues(at source: URL) throws -> Values {
        do {
            let fd = Darwin.open(source.path, O_RDONLY | O_CLOEXEC | O_NOFOLLOW | O_NONBLOCK)
            if fd < 0, errno == ENOENT {
                var linkInfo = stat()
                // A dangling symlink is still an existing user file, not empty settings.
                guard lstat(source.path, &linkInfo) != 0, errno == ENOENT else {
                    throw POSIXError(.ELOOP)
                }
                return Values(selectedModelID: nil, dictationPreferences: nil)
            }
            guard fd >= 0 else { throw POSIXError(POSIXErrorCode(rawValue: errno) ?? .EIO) }
            let handle = FileHandle(fileDescriptor: fd, closeOnDealloc: true)
            var info = stat()
            guard fstat(fd, &info) == 0, (info.st_mode & S_IFMT) == S_IFREG,
                  info.st_size <= 2_000_000 else { throw POSIXError(.EFBIG) }
            let data = try handle.read(upToCount: 2_000_001) ?? Data()
            guard data.count <= 2_000_000 else { throw POSIXError(.EFBIG) }
            return try JSONDecoder().decode(Values.self, from: data)
        } catch { throw ParrotSettingsError.unreadable(url: source, underlying: error) }
    }

    // All writers use a sibling lock and read-modify-write so changing a model
    // cannot erase Vocabulary/Snippets and saving rules cannot erase model choice.
    private static func update(at destination: URL, change: (inout Values) throws -> Void) throws {
        let directory = destination.deletingLastPathComponent()
        try FileManager.default.createDirectory(at: directory, withIntermediateDirectories: true,
                                               attributes: [.posixPermissions: 0o700])
        let lockPath = destination.appendingPathExtension("lock").path
        let fd = Darwin.open(lockPath, O_CREAT | O_RDWR | O_CLOEXEC | O_NOFOLLOW, mode_t(0o600))
        guard fd >= 0 else { throw POSIXError(POSIXErrorCode(rawValue: errno) ?? .EIO) }
        defer { Darwin.close(fd) }
        guard flock(fd, LOCK_EX) == 0 else { throw POSIXError(POSIXErrorCode(rawValue: errno) ?? .EIO) }
        defer { flock(fd, LOCK_UN) }
        var values = try readValues(at: destination)
        try change(&values)
        let encoder = JSONEncoder(); encoder.outputFormatting = [.prettyPrinted, .sortedKeys]
        let data = try encoder.encode(values)
        let temporary = directory.appendingPathComponent(".settings-\(UUID().uuidString).tmp")
        let tempFD = Darwin.open(temporary.path, O_CREAT | O_EXCL | O_WRONLY | O_CLOEXEC | O_NOFOLLOW, mode_t(0o600))
        guard tempFD >= 0 else { throw POSIXError(POSIXErrorCode(rawValue: errno) ?? .EIO) }
        let handle = FileHandle(fileDescriptor: tempFD, closeOnDealloc: false)
        defer { Darwin.close(tempFD); Darwin.unlink(temporary.path) }
        try handle.write(contentsOf: data)
        guard fsync(tempFD) == 0, Darwin.rename(temporary.path, destination.path) == 0 else {
            throw POSIXError(POSIXErrorCode(rawValue: errno) ?? .EIO)
        }
    }
}

enum ParrotSettingsError: LocalizedError {
    case unreadable(url: URL, underlying: Error)
    case conflict
    var errorDescription: String? {
        switch self {
        case .conflict:
            return "Phrase settings changed in another session. Reopen Studio before saving again."
        case .unreadable:
            return "Parrot settings could not be read. The existing file was left unchanged."
        }
    }
}
