import Foundation

/// Where the snapshot lives on iOS: the App Group container shared with the app, which writes
/// `widget-snapshot.json` there (`IosWidgetSnapshotPublisher`). Paths mirror the C# side.
public enum AppGroupPaths {
    public static let groupId = "group.org.diarion.app"
    public static let snapshotFile = "widget-snapshot.json"

    public static func containerURL(fileManager: FileManager = .default) -> URL? {
        fileManager.containerURL(forSecurityApplicationGroupIdentifier: groupId)
    }
}

/// Reads the snapshot; a missing or unreadable file is nil, never a crash. Plain `Data(contentsOf:)`,
/// no UserDefaults and no file timestamps, so the privacy manifest declares no required-reason API.
public struct SnapshotStore {
    private let root: URL

    public init(root: URL) {
        self.root = root
    }

    /// The store of the App Group container, or nil when the extension has no container (entitlement missing).
    public static func appGroup(fileManager: FileManager = .default) -> SnapshotStore? {
        guard let url = AppGroupPaths.containerURL(fileManager: fileManager) else { return nil }
        return SnapshotStore(root: url)
    }

    public func load() -> WidgetSnapshot? {
        let url = root.appendingPathComponent(AppGroupPaths.snapshotFile)
        guard let data = try? Data(contentsOf: url) else { return nil }
        return SnapshotCodec.decode(data)
    }
}
