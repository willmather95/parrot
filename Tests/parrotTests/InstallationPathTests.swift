import Foundation
import Testing
@testable import parrot

private let systemApp = "/Applications/Parrot.app"
private let testHome = URL(fileURLWithPath: "/Users/test", isDirectory: true)
private let userApp = "/Users/test/Applications/Parrot.app"

private func resolveApplicationPath(
    bundlePath: String,
    executable: Bool = true,
    installed: [String]? = nil,
    launchAgentProgram: String? = nil
) throws -> String {
    try ParrotLoginService.resolveApplicationPath(
        bundleURL: URL(fileURLWithPath: bundlePath, isDirectory: true),
        homeDirectory: testHome,
        isExecutableFile: { _ in executable },
        installedApplicationPaths: installed ?? [bundlePath],
        launchAgentProgram: launchAgentProgram
    )
}

@Test func systemApplicationPathIsSupported() throws {
    #expect(try resolveApplicationPath(bundlePath: systemApp) == systemApp)
}

@Test func perUserApplicationPathIsSupported() throws {
    #expect(try resolveApplicationPath(bundlePath: userApp) == userApp)
}

@Test func resolverRejectsApplicationsOutsideSupportedLocations() {
    #expect(throws: ParrotLoginService.ApplicationPathError.unsupportedBundle(
        "/tmp/Parrot.app",
        supported: [systemApp, userApp]
    )) {
        try resolveApplicationPath(bundlePath: "/tmp/Parrot.app")
    }
}

@Test func resolverRequiresBundledExecutable() {
    #expect(throws: ParrotLoginService.ApplicationPathError.missingExecutable(
        "\(userApp)/Contents/MacOS/parrot"
    )) {
        try resolveApplicationPath(bundlePath: userApp, executable: false)
    }
}

@Test func resolverRejectsBothInstallLocations() {
    #expect(throws: ParrotLoginService.ApplicationPathError.conflictingInstallation(
        systemApp,
        selected: userApp
    )) {
        try resolveApplicationPath(
            bundlePath: userApp,
            installed: [userApp, systemApp]
        )
    }
}

@Test func resolverRejectsLoginServiceForOtherInstallLocation() {
    let systemBinary = "\(systemApp)/Contents/MacOS/parrot"
    #expect(throws: ParrotLoginService.ApplicationPathError.conflictingLoginService(
        systemBinary,
        selected: userApp
    )) {
        try resolveApplicationPath(
            bundlePath: userApp,
            launchAgentProgram: systemBinary
        )
    }
}

@Test func resolverAcceptsLoginServiceForSelectedInstallLocation() throws {
    let userBinary = "\(userApp)/Contents/MacOS/parrot"
    #expect(try resolveApplicationPath(
        bundlePath: userApp,
        launchAgentProgram: userBinary
    ) == userApp)
}
