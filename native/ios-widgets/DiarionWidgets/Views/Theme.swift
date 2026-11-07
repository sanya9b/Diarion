import SwiftUI
import UIKit

/// The brand palette (AGENTS.md): monochrome surfaces, semantic accents only on dots. The same hex
/// values as Resources/Styles/Colors.xaml and the Android widget_colors.xml, light and dark.
enum DiarionPalette {
    static let snow = Color(hex: 0xFFFFFF)
    static let midnight = Color(hex: 0x22282C)
    static let earth = Color(hex: 0xE9E7E1)
    static let dust = Color(hex: 0xE0E6EA)
    static let ocean = Color(hex: 0x929FA7)
    static let coral = Color(hex: 0xC26D53)
    static let amber = Color(hex: 0xC9985A)
    static let sage = Color(hex: 0x8FA083)

    static let background = Color.dynamic(light: 0xFFFFFF, dark: 0x22282C)
    static let text = Color.dynamic(light: 0x22282C, dark: 0xE9E7E1)
    static let secondary = ocean
    static let track = Color.dynamic(light: 0xE9E7E1, dark: 0x3A4247)
    static let dustDot = Color.dynamic(light: 0xE0E6EA, dark: 0x4A545B)

    /// The quadrant's hue, or nil when nothing is recorded (the view draws a ring then).
    static func mood(_ quadrant: String?) -> Color? {
        switch quadrant {
        case "PleasantEnergetic": return coral
        case "PleasantCalm": return sage
        case "UnpleasantEnergetic": return amber
        case "UnpleasantCalm": return ocean
        case "Neutral": return dustDot
        default: return nil
        }
    }

    /// Coral / Amber / Sage for high / medium / low, as the planner colours them.
    static func priority(_ priority: String) -> Color {
        switch priority {
        case "High": return coral
        case "Low": return sage
        default: return amber
        }
    }
}

extension Color {
    init(hex: UInt32) {
        self.init(uiColor: UIColor(hex: hex))
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
