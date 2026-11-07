import SwiftUI
import WidgetKit
import DiarionWidgetsShared

/// W2 "Tasks of the day": the count and a progress bar at every level; at the with-text level also
/// the rows — open ones first, a priority dot in front, the time at the end, done ones greyed. Rows
/// are `privacySensitive`, so the lock screen and a locked StandBy redact them.
struct TasksWidget: Widget {
    static let kind = "org.diarion.app.widgets.tasks"

    var body: some WidgetConfiguration {
        StaticConfiguration(kind: Self.kind, provider: SnapshotProvider()) { entry in
            TasksEntryView(entry: entry)
        }
        .configurationDisplayName("tasks.name")
        .description("tasks.description")
        .supportedFamilies([.systemMedium, .systemLarge])
    }
}

struct TasksEntryView: View {
    @Environment(\.widgetFamily) private var family
    let entry: SnapshotEntry

    var body: some View {
        content
            .diarionBackground(entry.palette, family: family)
            .widgetURL(URL(string: "diarion://tasks"))
    }

    @ViewBuilder
    private var content: some View {
        if let snapshot = entry.current, !snapshot.isButtonOnly, let tasks = snapshot.tasks {
            TasksView(snapshot: snapshot, tasks: tasks, palette: entry.palette, rows: family == .systemLarge ? 8 : 3)
        } else {
            ButtonOnlyView(label: entry.openLabel, palette: entry.palette)
        }
    }
}

struct TasksView: View {
    let snapshot: WidgetSnapshot
    let tasks: WidgetSnapshot.Tasks
    let palette: WidgetPalette
    let rows: Int

    private var visible: [WidgetSnapshot.TaskItem] { Array((tasks.items ?? []).prefix(rows)) }
    private var hidden: Int { tasks.items == nil ? 0 : max(0, tasks.total - visible.count) }

    var body: some View {
        VStack(alignment: .leading, spacing: 6) {
            HStack {
                Text(tasks.label)
                    .font(.subheadline.weight(.semibold))
                    .foregroundStyle(palette.text)
                    .lineLimit(1)
                Spacer(minLength: 4)
                DateLabel(text: snapshot.labels.dateShort, palette: palette)
            }
            ProgressBar(fraction: tasks.total > 0 ? Double(tasks.done) / Double(tasks.total) : 0, palette: palette)
            if !visible.isEmpty {
                VStack(alignment: .leading, spacing: WidgetMetrics.rowSpacing) {
                    ForEach(Array(visible.enumerated()), id: \.offset) { _, item in
                        TaskRow(item: item, palette: palette)
                    }
                }
                .privacySensitive()
            }
            Spacer(minLength: 0)
            HStack {
                UpdatedLabel(text: snapshot.labels.updated, palette: palette)
                Spacer(minLength: 4)
                if hidden > 0 {
                    Text(verbatim: "+\(hidden)")
                        .font(.caption2)
                        .foregroundStyle(palette.secondary)
                }
            }
        }
        .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .topLeading)
    }
}

/// Track in the theme's divider colour, fill in its Sage: progress is success, not an alarm.
struct ProgressBar: View {
    let fraction: Double
    let palette: WidgetPalette

    var body: some View {
        GeometryReader { geometry in
            ZStack(alignment: .leading) {
                Capsule().fill(palette.track)
                Capsule().fill(palette.sage).frame(width: geometry.size.width * min(max(fraction, 0), 1))
            }
        }
        .frame(height: 6)
    }
}

struct TaskRow: View {
    let item: WidgetSnapshot.TaskItem
    let palette: WidgetPalette

    var body: some View {
        HStack(spacing: 8) {
            Dot(fill: item.done ? palette.sage : palette.priority(item.priority), palette: palette, size: WidgetMetrics.smallDot)
            Text(item.title)
                .font(.footnote)
                .foregroundStyle(item.done ? palette.secondary : palette.text)
                .strikethrough(item.done, color: palette.secondary)
                .lineLimit(1)
            Spacer(minLength: 4)
            if let time = item.time {
                Text(time)
                    .font(.caption)
                    .foregroundStyle(palette.secondary)
                    .lineLimit(1)
            }
        }
    }
}
