import SwiftUI
import WidgetKit
import DiarionWidgetsShared

/// W3 "Habits": the count, then either a row of dots — one per habit scheduled today, filled when
/// done — or, at the with-text level, the rows with a mark and the habit's name.
struct HabitsWidget: Widget {
    static let kind = "org.diarion.app.widgets.habits"

    var body: some WidgetConfiguration {
        StaticConfiguration(kind: Self.kind, provider: SnapshotProvider()) { entry in
            HabitsEntryView(entry: entry)
        }
        .configurationDisplayName("habits.name")
        .description("habits.description")
        .supportedFamilies([.systemMedium, .systemLarge])
    }
}

struct HabitsEntryView: View {
    @Environment(\.widgetFamily) private var family
    let entry: SnapshotEntry

    var body: some View {
        content
            .diarionBackground(entry.palette, family: family)
            .widgetURL(URL(string: "diarion://habits"))
    }

    @ViewBuilder
    private var content: some View {
        if let snapshot = entry.current, !snapshot.isButtonOnly, let habits = snapshot.habits {
            HabitsView(snapshot: snapshot, habits: habits, palette: entry.palette, rows: family == .systemLarge ? 10 : 4)
        } else {
            ButtonOnlyView(label: entry.openLabel, palette: entry.palette)
        }
    }
}

struct HabitsView: View {
    let snapshot: WidgetSnapshot
    let habits: WidgetSnapshot.Habits
    let palette: WidgetPalette
    let rows: Int

    private static let maxDots = 10

    private var visible: [WidgetSnapshot.HabitItem] { Array((habits.items ?? []).prefix(rows)) }
    private var hidden: Int { habits.items == nil ? 0 : max(0, habits.total - visible.count) }

    var body: some View {
        VStack(alignment: .leading, spacing: 6) {
            HStack {
                Text(habits.label)
                    .font(.subheadline.weight(.semibold))
                    .foregroundStyle(palette.text)
                    .lineLimit(1)
                Spacer(minLength: 4)
                DateLabel(text: snapshot.labels.dateShort, palette: palette)
            }
            if habits.items == nil {
                // Dots in the habits' own order are not known at this level, only how many are done:
                // the done ones come first, which reads as a bar filling from the left.
                HStack(spacing: 6) {
                    ForEach(0..<min(habits.total, Self.maxDots), id: \.self) { index in
                        Dot(fill: index < habits.done ? palette.sage : nil, palette: palette, size: 14)
                    }
                }
                .padding(.top, 4)
            } else {
                VStack(alignment: .leading, spacing: WidgetMetrics.rowSpacing) {
                    ForEach(Array(visible.enumerated()), id: \.offset) { _, item in
                        HStack(spacing: 8) {
                            Dot(fill: item.done ? palette.sage : nil, palette: palette)
                            Text(item.name)
                                .font(.footnote)
                                .foregroundStyle(item.done ? palette.secondary : palette.text)
                                .lineLimit(1)
                        }
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
