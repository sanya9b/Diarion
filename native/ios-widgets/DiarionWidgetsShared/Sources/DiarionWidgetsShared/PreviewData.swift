import Foundation

/// Sample snapshots for the widget gallery and placeholders. The gallery is shown before the app
/// has written anything, so these carry their own two languages — the only strings the widgets own
/// besides the gallery names.
public enum PreviewData {
    /// A full "with text" sample in the given language ("uk" or anything else for English).
    public static func sample(locale: String = PreviewData.deviceLanguage(), day: Date = Date()) -> WidgetSnapshot {
        let uk = locale.lowercased().hasPrefix("uk")
        return WidgetSnapshot(
            date: WidgetSnapshot.dayKey(day),
            generatedAt: "",
            locale: uk ? "uk" : "en",
            privacyLevel: "WithText",
            labels: WidgetSnapshot.Labels(
                appName: "Diarion",
                open: uk ? "Відкрити щоденник" : "Open diary",
                dateShort: uk ? "чт, 5 лист." : "Thu, 5 Nov",
                updated: uk ? "Оновлено 14:02" : "Updated 14:02"),
            streak: WidgetSnapshot.Streak(days: 12, heldByGrace: false, label: uk ? "12 днів поспіль" : "12 days in a row"),
            mood: WidgetSnapshot.Mood(quadrant: "PleasantCalm", label: uk ? "Настрій дня" : "Mood of the day"),
            tasks: WidgetSnapshot.Tasks(done: 2, total: 5, label: uk ? "2 з 5 справ" : "2 of 5 tasks", items: [
                WidgetSnapshot.TaskItem(title: uk ? "Стоматолог" : "Dentist", time: "14:00", done: false, priority: "High"),
                WidgetSnapshot.TaskItem(title: uk ? "Подзвонити мамі" : "Call mum", time: "18:30", done: false, priority: "Medium"),
                WidgetSnapshot.TaskItem(title: uk ? "Купити хліб" : "Buy bread", time: nil, done: false, priority: "Low"),
                WidgetSnapshot.TaskItem(title: uk ? "Ранкова пробіжка" : "Morning run", time: "07:00", done: true, priority: "Medium"),
                WidgetSnapshot.TaskItem(title: uk ? "Пошта" : "Email", time: nil, done: true, priority: "Low")
            ]),
            habits: WidgetSnapshot.Habits(done: 3, total: 4, label: uk ? "3 з 4 звичок" : "3 of 4 habits", items: [
                WidgetSnapshot.HabitItem(name: uk ? "Вода" : "Water", done: true),
                WidgetSnapshot.HabitItem(name: uk ? "Прогулянка" : "Walk", done: true),
                WidgetSnapshot.HabitItem(name: uk ? "Читання" : "Reading", done: true),
                WidgetSnapshot.HabitItem(name: uk ? "Вітаміни" : "Vitamins", done: false)
            ]))
    }

    /// Two-letter language of the device, the best guess before the app has said which one it uses.
    public static func deviceLanguage() -> String {
        Locale.preferredLanguages.first.map { String($0.prefix(2)) } ?? "en"
    }

    /// The open-label fallback for a widget with no snapshot at all (fresh install, app never opened).
    public static func openLabel(locale: String = PreviewData.deviceLanguage()) -> String {
        locale.lowercased().hasPrefix("uk") ? "Відкрити Diarion" : "Open Diarion"
    }
}
