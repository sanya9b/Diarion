import SwiftUI
import UIKit
import DiarionWidgetsShared

/// The colours one widget is drawn with (spec 20, D7). Built from the snapshot's `theme` — the app's
/// own Light, Dark or Pink, chosen by hand — and falling back to the brand palette in the system's
/// appearance when a file carries no theme (an older file, the gallery samples).
struct WidgetPalette {
    let background: Color
    let text: Color
    let secondary: Color
    let track: Color
    let accent: Color
    let sage: Color
    let amber: Color
    let ocean: Color

    /// Brand palette following the system appearance: the same hex values as Colors.xaml.
    static let fallback = WidgetPalette(
        background: Color.dynamic(light: 0xFFFFFF, dark: 0x2B3136),
        text: Color.dynamic(light: 0x22282C, dark: 0xFFFFFF),
        secondary: Color.dynamic(light: 0x6B7680, dark: 0xE0E6EA),
        track: Color.dynamic(light: 0xE0E6EA, dark: 0x929FA7),
        accent: Color(hex: 0xC26D53),
        sage: Color(hex: 0x8FA083),
        amber: Color(hex: 0xC9985A),
        ocean: Color(hex: 0x929FA7))

    init(background: Color, text: Color, secondary: Color, track: Color, accent: Color, sage: Color, amber: Color, ocean: Color) {
        self.background = background
        self.text = text
        self.secondary = secondary
        self.track = track
        self.accent = accent
        self.sage = sage
        self.amber = amber
        self.ocean = ocean
    }

    init(theme: WidgetSnapshot.Theme) {
        self.init(
            background: Color(hexString: theme.background),
            text: Color(hexString: theme.text),
            secondary: Color(hexString: theme.secondary),
            track: Color(hexString: theme.track),
            accent: Color(hexString: theme.accent),
            sage: Color(hexString: theme.sage),
            amber: Color(hexString: theme.amber),
            ocean: Color(hexString: theme.ocean))
    }

    /// The quadrant's hue, or nil when nothing is recorded (the view draws a ring then).
    func mood(_ quadrant: String?) -> Color? {
        switch quadrant {
        case "PleasantEnergetic": return accent
        case "PleasantCalm": return sage
        case "UnpleasantEnergetic": return amber
        case "UnpleasantCalm": return ocean
        case "Neutral": return track
        default: return nil
        }
    }

    /// Accent / Amber / Sage for high / medium / low, as the planner colours them.
    func priority(_ priority: String) -> Color {
        switch priority {
        case "High": return accent
        case "Low": return sage
        default: return amber
        }
    }
}

extension Color {
    init(hex: UInt32) {
        self.init(uiColor: UIColor(hex: hex))
    }

    /// "#RRGGBB" as the app writes it; anything else is Ocean grey rather than a crash.
    init(hexString: String) {
        var value = hexString.trimmingCharacters(in: .whitespacesAndNewlines)
        if value.hasPrefix("#") { value.removeFirst() }
        var number: UInt64 = 0
        if value.count == 6, Scanner(string: value).scanHexInt64(&number) {
            self.init(hex: UInt32(number))
        } else {
            self.init(hex: 0x929FA7)
        }
    }

    /// A light/dark pair as one dynamic colour that follows the appearance of the widget.
    static func dynamic(light: UInt32, dark: UInt32) -> Color {
        Color(uiColor: UIColor { traits in
            UIColor(hex: traits.userInterfaceStyle == .dark ? dark : light)
        })
    }
}

extension UIColor {
    convenience init(hex: UInt32) {
        self.init(
            red: CGFloat((hex >> 16) & 0xFF) / 255,
            green: CGFloat((hex >> 8) & 0xFF) / 255,
            blue: CGFloat(hex & 0xFF) / 255,
            alpha: 1)
    }
}

/// Spacing shared by the three faces, in points.
enum WidgetMetrics {
    static let padding: CGFloat = 14
    static let dot: CGFloat = 12
    static let smallDot: CGFloat = 8
    static let rowSpacing: CGFloat = 4
}
