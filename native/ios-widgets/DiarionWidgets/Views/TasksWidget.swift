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
            .diarionBackground(family: family)
            .widgetURL(URL(string: "diarion://tasks"))
    }

    @ViewBuilder
    private var content: some View {
        if let snapshot = entry.current, !snapshot.isButtonOnly, let tasks = snapshot.tasks {
            TasksView(snapshot: snapshot, tasks: tasks, rows: family == .systemLarge ? 8 : 3)
        } else {
            ButtonOnlyView(label: entry.openLabel)
        }
    }
}

struct TasksView: View {
    let snapshot: WidgetSnapshot
    let tasks: WidgetSnapshot.Tasks
    let rows: Int

    private var visible: [WidgetSnapshot.TaskItem] { Array((tasks.items ?? []).prefix(rows)) }
    private var hidden: Int { tasks.items == nil ? 0 : max(0, tasks.total - visible.count) }

    var body: some View {
        VStack(alignment: .leading, spacing: 6) {
            HStack {
                Text(tasks.label)
                    .font(.subheadline.weight(.semibold))
                    .foregroundStyle(DiarionPalette.text)
                    .lineLimit(1)
                Spacer(minLength: 4)
                DateLabel(text: snapshot.labels.dateShort)
            }
            ProgressView(value: Double(tasks.done), total: Double(max(tasks.total, 1)))
                .tint(DiarionPalette.sage)
                .background(DiarionPalette.track)
            if !visible.isEmpty {
                VStack(alignment: .leading, spacing: WidgetMetrics.rowSpacing) {
                    ForEach(Array(visible.enumerated()), id: \.offset) { _, item in
                        TaskRow(item: item)
                    }
                }
                .privacySensitive()
            }
            Spacer(minLength: 0)
            HStack {
                UpdatedLabel(text: snapshot.labels.updated)
                Spacer(minLength: 4)
                if hidden > 0 {
                    Text(verbatim: "+\(hidden)")
                        .font(.caption2)
                        .foregroundStyle(DiarionPalette.secondary)
                }
            }
        }
        .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .topLeading)
    }
}

struct TaskRow: View {
    let item: WidgetSnapshot.TaskItem

    var body: some View {
        HStack(spacing: 8) {
            Dot(fill: item.done ? DiarionPalette.sage : DiarionPalette.priority(item.priority), size: WidgetMetrics.smallDot)
            Text(item.title)
                .font(.footnote)
                .foregroundStyle(item.done ? DiarionPalette.secondary : DiarionPalette.text)
                .strikethrough(item.done, color: DiarionPalette.secondary)
                .lineLimit(1)
            Spacer(minLength: 4)
            if let time = item.time {
                Text(time)
                    .font(.caption)
                    .foregroundStyle(DiarionPalette.secondary)
                    .lineLimit(1)
            }
        }
    }
}
