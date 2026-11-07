import Foundation

/// The file the app writes for the widgets, mirrored from `Diarion.Core/Models/Widgets/WidgetSnapshot.cs`
/// (spec 20, R1). Keys are camelCase and enums are strings because that is what the C# side emits;
/// a section is absent when the privacy level does not allow it. Everything here is already
/// localised by the app — the widget carries no dictionary of its own (D4).
public struct WidgetSnapshot: Codable, Equatable {
    public static let currentSchemaVersion = 1

    public let schemaVersion: Int
    /// yyyy-MM-dd of the day the counts are about.
    public let date: String
    /// Left as text: the widget only ever shows `labels.updated`, which the app has formatted.
    public let generatedAt: String
    public let locale: String
    /// "ButtonOnly", "Counts" or "WithText".
    public let privacyLevel: String
    public let labels: Labels
    public let streak: Streak?
    public let mood: Mood?
    public let tasks: Tasks?
    public let habits: Habits?

    public init(schemaVersion: Int = WidgetSnapshot.currentSchemaVersion, date: String, generatedAt: String,
                locale: String, privacyLevel: String, labels: Labels, streak: Streak? = nil, mood: Mood? = nil,
                tasks: Tasks? = nil, habits: Habits? = nil) {
        self.schemaVersion = schemaVersion
        self.date = date
        self.generatedAt = generatedAt
        self.locale = locale
        self.privacyLevel = privacyLevel
        self.labels = labels
        self.streak = streak
        self.mood = mood
        self.tasks = tasks
        self.habits = habits
    }

    public struct Labels: Codable, Equatable {
        public let appName: String
        public let open: String
        public let dateShort: String
        public let updated: String

        public init(appName: String, open: String, dateShort: String, updated: String) {
            self.appName = appName
            self.open = open
            self.dateShort = dateShort
            self.updated = updated
        }
    }

    public struct Streak: Codable, Equatable {
        public let days: Int
        public let heldByGrace: Bool
        public let label: String

        public init(days: Int, heldByGrace: Bool, label: String) {
            self.days = days
            self.heldByGrace = heldByGrace
            self.label = label
        }
    }

    public struct Mood: Codable, Equatable {
        /// An `EmotionQuadrant` name, or nil when nothing is recorded.
        public let quadrant: String?
        public let label: String

        public init(quadrant: String?, label: String) {
            self.quadrant = quadrant
            self.label = label
        }
    }

    public struct Tasks: Codable, Equatable {
        public let done: Int
        public let total: Int
        public let label: String
        public let items: [TaskItem]?

        public init(done: Int, total: Int, label: String, items: [TaskItem]? = nil) {
            self.done = done
            self.total = total
            self.label = label
            self.items = items
        }
    }

    public struct TaskItem: Codable, Equatable {
        public let title: String
        public let time: String?
        public let done: Bool
        public let priority: String

        public init(title: String, time: String?, done: Bool, priority: String) {
            self.title = title
            self.time = time
            self.done = done
            self.priority = priority
        }
    }

    public struct Habits: Codable, Equatable {
        public let done: Int
        public let total: Int
        public let label: String
        public let items: [HabitItem]?

        public init(done: Int, total: Int, label: String, items: [HabitItem]? = nil) {
            self.done = done
            self.total = total
            self.label = label
            self.items = items
        }
    }

    public struct HabitItem: Codable, Equatable {
        public let name: String
        public let done: Bool

        public init(name: String, done: Bool) {
            self.name = name
            self.done = done
        }
    }

    /// The button-only level: a logo and the open label, nothing else is drawn.
    public var isButtonOnly: Bool { privacyLevel == "ButtonOnly" }

    /// The with-text level: task and habit names may be shown (behind `privacySensitive`).
    public var showsText: Bool { privacyLevel == "WithText" }

    /// True when the snapshot describes `day` in the given calendar. A widget that wakes up on a later
    /// day must not present yesterday's counts as today's (spec 20, R4).
    public func isFor(day: Date, calendar: Calendar = .current) -> Bool {
        date == Self.dayKey(day, calendar: calendar)
    }

    /// yyyy-MM-dd for `day`, the same key the app writes.
    public static func dayKey(_ day: Date, calendar: Calendar = .current) -> String {
        let parts = calendar.dateComponents([.year, .month, .day], from: day)
        return String(format: "%04d-%02d-%02d", parts.year ?? 0, parts.month ?? 0, parts.day ?? 0)
    }
}

/// Decoding with the one rule that matters: an unknown schema is a placeholder, never a crash.
public enum SnapshotCodec {
    public static func decode(_ data: Data) -> WidgetSnapshot? {
        guard let snapshot = try? JSONDecoder().decode(WidgetSnapshot.self, from: data) else { return nil }
        guard snapshot.schemaVersion == WidgetSnapshot.currentSchemaVersion else { return nil }
        return snapshot
    }
}
