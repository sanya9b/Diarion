import SwiftUI
import WidgetKit
import DiarionWidgetsShared

/// W1 "Today": date and mood dot, the streak as the big number, the two counts under it. Also the
/// lock-screen faces: a circle with the streak, a rectangle with the counts, an inline streak line.
struct TodayWidget: Widget {
    static let kind = "org.diarion.app.widgets.today"

    var body: some WidgetConfiguration {
        StaticConfiguration(kind: Self.kind, provider: SnapshotProvider()) { entry in
            TodayEntryView(entry: entry)
        }
        .configurationDisplayName("today.name")
        .description("today.description")
        .supportedFamilies([.systemSmall, .systemMedium, .accessoryCircular, .accessoryRectangular, .accessoryInline])
    }
}

struct TodayEntryView: View {
    @Environment(\.widgetFamily) private var family
    let entry: SnapshotEntry

    var body: some View {
        content
            .diarionBackground(entry.palette, family: family)
            .widgetURL(URL(string: "diarion://today"))
    }

    @ViewBuilder
    private var content: some View {
        let palette = entry.palette
        if let snapshot = entry.current, !snapshot.isButtonOnly {
            switch family {
            case .accessoryCircular: CircularStreakView(snapshot: snapshot, palette: palette)
            case .accessoryRectangular: RectangularTodayView(snapshot: snapshot)
            case .accessoryInline: InlineTodayView(snapshot: snapshot)
            default: TodayView(snapshot: snapshot, palette: palette)
            }
        } else {
            switch family {
            case .accessoryCircular:
                ZStack {
                    AccessoryWidgetBackground()
                    DiarionGlyph(size: 26)
                }
            case .accessoryRectangular, .accessoryInline:
                Label { Text(entry.openLabel) } icon: { Image(systemName: "book.closed") }
            default:
                ButtonOnlyView(label: entry.openLabel, palette: palette)
            }
        }
    }
}

struct TodayView: View {
    let snapshot: WidgetSnapshot
    let palette: WidgetPalette

    var body: some View {
        VStack(alignment: .leading, spacing: 2) {
            HStack {
                DateLabel(text: snapshot.labels.dateShort, palette: palette)
                Spacer(minLength: 4)
                Dot(fill: palette.mood(snapshot.mood?.quadrant), palette: palette, size: 16)
                    .accessibilityLabel(Text(snapshot.mood?.label ?? ""))
            }
            if let streak = snapshot.streak {
                if streak.days > 0 {
                    Text(verbatim: "\(streak.days)")
                        .font(.system(size: 30, weight: .bold))
                        .foregroundStyle(palette.text)
                        .padding(.top, 4)
                }
                Text(streak.label)
                    .font(.caption)
                    .foregroundStyle(palette.secondary)
                    .lineLimit(2)
            }
            Spacer(minLength: 0)
            if let tasks = snapshot.tasks {
                Text(tasks.label).font(.footnote).foregroundStyle(palette.text).lineLimit(1)
            }
            if let habits = snapshot.habits {
                Text(habits.label).font(.footnote).foregroundStyle(palette.text).lineLimit(1)
            }
            UpdatedLabel(text: snapshot.labels.updated, palette: palette).padding(.top, 2)
        }
        .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .topLeading)
    }
}

/// Lock screen circle: the streak number, or the glyph when there is no run yet. Vibrant rendering
/// ignores colours, so the palette only shapes the glyph.
struct CircularStreakView: View {
    let snapshot: WidgetSnapshot
    let palette: WidgetPalette

    var body: some View {
        ZStack {
            AccessoryWidgetBackground()
            if let streak = snapshot.streak, streak.days > 0 {
                VStack(spacing: 0) {
                    Text(verbatim: "\(streak.days)")
                        .font(.system(size: 20, weight: .bold))
                        .minimumScaleFactor(0.6)
                        .widgetAccentable()
                    Image(systemName: "flame.fill").font(.system(size: 9))
                }
            } else {
                DiarionGlyph(size: 26, palette: palette)
            }
        }
        .accessibilityLabel(Text(snapshot.streak?.label ?? snapshot.labels.appName))
    }
}

/// Lock screen rectangle: the date line, then tasks and habits counts.
struct RectangularTodayView: View {
    let snapshot: WidgetSnapshot

    var body: some View {
        VStack(alignment: .leading, spacing: 1) {
            HStack(spacing: 4) {
                Image(systemName: "book.closed").imageScale(.small)
                Text(snapshot.streak?.label ?? snapshot.labels.dateShort)
                    .font(.headline)
                    .lineLimit(1)
                    .minimumScaleFactor(0.7)
                    .widgetAccentable()
            }
            if let tasks = snapshot.tasks {
                Text(tasks.label).font(.system(size: 13)).lineLimit(1)
            }
            if let habits = snapshot.habits {
                Text(habits.label).font(.system(size: 13)).lineLimit(1)
            }
        }
        .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .leading)
    }
}

/// Lock screen inline line: the streak, or the open label before the first run.
struct InlineTodayView: View {
    let snapshot: WidgetSnapshot

    var body: some View {
        Label {
            Text(snapshot.streak?.label ?? snapshot.labels.open)
        } icon: {
            Image(systemName: "flame.fill")
        }
    }
}
