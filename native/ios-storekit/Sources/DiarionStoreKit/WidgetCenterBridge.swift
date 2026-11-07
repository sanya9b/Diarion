import Foundation
import WidgetKit

// MARK: - Widgets (spec 20, R3)
//
// WidgetKit is Swift-only, so the one call the app needs from it — "redraw the widgets, the snapshot
// changed" — rides in this facade next to StoreKit rather than in a second native library or a
// NuGet binding: same static link, same symbol check in build-ios.yml, no new embedded framework.
// Reloading is cheap on the app's side; WidgetKit itself budgets how often it really redraws.

@_cdecl("dsk_reload_widgets")
public func dsk_reload_widgets() {
    if #available(iOS 14.0, *) {
        WidgetCenter.shared.reloadAllTimelines()
    }
}
