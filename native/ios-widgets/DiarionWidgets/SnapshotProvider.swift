import WidgetKit
import DiarionWidgetsShared

/// What one timeline entry carries: the snapshot as read, or nil when there is none.
struct SnapshotEntry: TimelineEntry {
    let date: Date
    let snapshot: WidgetSnapshot?
    /// Gallery and placeholder data, never redacted by the privacy rules of a real snapshot.
    let isSample: Bool

    /// The view's one question: are these numbers about the day this entry is shown on? A snapshot
    /// from yesterday is drawn as the button-only face — the app rewrites the file as soon as it runs.
    var current: WidgetSnapshot? {
        guard let snapshot, snapshot.isFor(day: date) else { return nil }
        return snapshot
    }
}

/// Reads the App Group file and hands it to WidgetKit (spec 20, R4). No planning here: one entry now,
/// another at midnight so the day rolls over even when the app is not opened, and the app asks for a
/// reload through WidgetCenter whenever it writes a new file.
struct SnapshotProvider: TimelineProvider {
    /// Without a file (fresh install, app never opened) the system is asked again in an hour.
    static let retryWithoutSnapshot: TimeInterval = 3600

    func placeholder(in context: Context) -> SnapshotEntry {
        SnapshotEntry(date: Date(), snapshot: PreviewData.sample(), isSample: true)
    }

    func getSnapshot(in context: Context, completion: @escaping (SnapshotEntry) -> Void) {
        let now = Date()
        if context.isPreview {
            completion(SnapshotEntry(date: now, snapshot: PreviewData.sample(), isSample: true))
            return
        }
        completion(SnapshotEntry(date: now, snapshot: SnapshotStore.appGroup()?.load(), isSample: false))
    }

    func getTimeline(in context: Context, completion: @escaping (Timeline<SnapshotEntry>) -> Void) {
        let now = Date()
        let snapshot = SnapshotStore.appGroup()?.load()
        let entry = SnapshotEntry(date: now, snapshot: snapshot, isSample: false)
        guard snapshot != nil else {
            completion(Timeline(entries: [entry], policy: .after(now.addingTimeInterval(Self.retryWithoutSnapshot))))
            return
        }
        // A second entry at midnight: the same file, but `current` becomes nil and the face changes.
        let midnight = Calendar.current.nextDate(
            after: now, matching: DateComponents(hour: 0, minute: 0, second: 5), matchingPolicy: .nextTime) ?? now.addingTimeInterval(86_400)
        let rollover = SnapshotEntry(date: midnight, snapshot: snapshot, isSample: false)
        completion(Timeline(entries: [entry, rollover], policy: .atEnd))
    }
}
