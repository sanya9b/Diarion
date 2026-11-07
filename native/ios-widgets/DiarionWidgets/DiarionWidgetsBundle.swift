import SwiftUI
import WidgetKit

/// The three widgets of spec 20 (MVP W1–W3). Each reads the same snapshot; they differ in what they draw.
@main
struct DiarionWidgetsBundle: WidgetBundle {
    var body: some Widget {
        TodayWidget()
        TasksWidget()
        HabitsWidget()
    }
}
