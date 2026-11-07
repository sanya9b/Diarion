import SwiftUI
import WidgetKit
import DiarionWidgetsShared

extension View {
    /// `containerBackground` on iOS 17+ so StandBy and tinted modes can drop it; a plain fill with
    /// the system margin on iOS 16. Accessory families never paint a background of their own.
    @ViewBuilder
    func diarionBackground(family: WidgetFamily) -> some View {
        if #available(iOS 17.0, *) {
            if family.isAccessory {
                containerBackground(for: .widget) { Color.clear }
            } else {
                containerBackground(for: .widget) { DiarionPalette.background }
            }
        } else if family.isAccessory {
            self
        } else {
            padding(WidgetMetrics.padding).background(DiarionPalette.background)
        }
    }
}

extension WidgetFamily {
    var isAccessory: Bool {
        switch self {
        case .accessoryCircular, .accessoryRectangular, .accessoryInline: return true
        default: return false
        }
    }
}

/// The button-only face (spec 20, D2 L0): the brand "D" with its Coral bookmark and the open label.
/// Also what every widget shows without a snapshot, or with one from another day.
struct ButtonOnlyView: View {
    let label: String

    var body: some View {
        VStack(spacing: 8) {
            DiarionGlyph(size: 40)
            Text(label)
                .font(.footnote)
                .foregroundStyle(DiarionPalette.text)
                .multilineTextAlignment(.center)
                .lineLimit(2)
        }
        .frame(maxWidth: .infinity, maxHeight: .infinity)
    }
}

/// A large serif "D" with the Coral bookmark hanging off its top right, the app icon in miniature.
struct DiarionGlyph: View {
    let size: CGFloat

    var body: some View {
        ZStack(alignment: .topTrailing) {
            Text(verbatim: "D")
                .font(.system(size: size, weight: .bold, design: .serif))
                .foregroundStyle(DiarionPalette.text)
            Rectangle()
                .fill(DiarionPalette.coral)
                .frame(width: size * 0.18, height: size * 0.42)
                .offset(x: size * 0.1, y: -size * 0.08)
        }
        .accessibilityHidden(true)
    }
}

/// A filled dot in a colour, or a ring when `fill` is nil.
struct Dot: View {
    let fill: Color?
    var size: CGFloat = WidgetMetrics.dot

    var body: some View {
        if let fill {
            Circle().fill(fill).frame(width: size, height: size)
        } else {
            Circle().strokeBorder(DiarionPalette.secondary, lineWidth: 1.5).frame(width: size, height: size)
        }
    }
}

/// The date on the right of a header, small and secondary.
struct DateLabel: View {
    let text: String

    var body: some View {
        Text(text)
            .font(.caption)
            .foregroundStyle(DiarionPalette.secondary)
            .lineLimit(1)
    }
}

/// "Updated 14:02" at the bottom, honest about how fresh the numbers are (iOS throttles refreshes).
struct UpdatedLabel: View {
    let text: String

    var body: some View {
        Text(text)
            .font(.system(size: 10))
            .foregroundStyle(DiarionPalette.secondary)
            .lineLimit(1)
    }
}

extension SnapshotEntry {
    /// The open label for the button-only face: the app's own words when a file exists, else the device language.
    var openLabel: String {
        snapshot?.labels.open ?? PreviewData.openLabel()
    }
}
