import XCTest
@testable import DiarionWidgetsShared

/// The contract with the C# writer (spec 20, R1). The first fixture is a file copied verbatim from the
/// Android emulator on 2026-11-05 — including the narrow no-break space iOS-style time formatting puts
/// before "PM" — so the decoder is proven against what the app really writes, not against a hand-made ideal.
final class SnapshotCodecTests: XCTestCase {
    private let countsFixture = """
    {"schemaVersion":1,"date":"2026-11-05","generatedAt":"2026-11-05T20:48:29.7004998+00:00","locale":"en",\
    "privacyLevel":"Counts","labels":{"appName":"Diarion","open":"Open diary","dateShort":"Thu, 5 Nov",\
    "updated":"Updated 8:48\\u202FPM"},"streak":{"days":1,"heldByGrace":false,"label":"1 day in a row"},\
    "mood":{"label":"How are you?"},"tasks":{"done":0,"total":1,"label":"0 of 1 tasks"},\
    "habits":{"done":2,"total":5,"label":"2 of 5 habits"}}
    """

    private let withTextFixture = """
    {"schemaVersion":1,"date":"2026-11-05","generatedAt":"x","locale":"uk","privacyLevel":"WithText",\
    "labels":{"appName":"Diarion","open":"Відкрити щоденник","dateShort":"чт, 5 лист.","updated":"Оновлено 14:02"},\
    "tasks":{"done":1,"total":2,"label":"1 з 2 справ","items":[{"title":"Стоматолог","time":"14:00","done":false,"priority":"High"},\
    {"title":"Пошта","done":true,"priority":"Low"}]},\
    "habits":{"done":1,"total":1,"label":"1 з 1 звичок","items":[{"name":"Вода","done":true}]}}
    """

    func testDecodesTheCountsLevelFromTheEmulatorFile() throws {
        let snapshot = try XCTUnwrap(SnapshotCodec.decode(Data(countsFixture.utf8)))
        XCTAssertEqual(snapshot.date, "2026-11-05")
        XCTAssertEqual(snapshot.privacyLevel, "Counts")
        XCTAssertFalse(snapshot.isButtonOnly)
        XCTAssertFalse(snapshot.showsText)
        XCTAssertEqual(snapshot.labels.updated, "Updated 8:48\u{202F}PM")
        XCTAssertEqual(snapshot.streak?.days, 1)
        XCTAssertNil(snapshot.mood?.quadrant, "nothing recorded is a null quadrant, not a missing section")
        XCTAssertEqual(snapshot.mood?.label, "How are you?")
        XCTAssertEqual(snapshot.tasks?.total, 1)
        XCTAssertNil(snapshot.tasks?.items, "the counts level carries no rows")
        XCTAssertEqual(snapshot.habits?.done, 2)
    }

    func testDecodesRowsAtTheWithTextLevel() throws {
        let snapshot = try XCTUnwrap(SnapshotCodec.decode(Data(withTextFixture.utf8)))
        XCTAssertTrue(snapshot.showsText)
        XCTAssertNil(snapshot.streak, "a section the writer left out is nil")
        XCTAssertEqual(snapshot.tasks?.items?.count, 2)
        XCTAssertEqual(snapshot.tasks?.items?.first?.title, "Стоматолог")
        XCTAssertEqual(snapshot.tasks?.items?.first?.time, "14:00")
        XCTAssertNil(snapshot.tasks?.items?.last?.time)
        XCTAssertEqual(snapshot.habits?.items?.first?.name, "Вода")
    }

    func testButtonOnlyHasNoSections() throws {
        let json = """
        {"schemaVersion":1,"date":"2026-11-05","generatedAt":"x","locale":"en","privacyLevel":"ButtonOnly",\
        "labels":{"appName":"Diarion","open":"Open diary","dateShort":"Thu, 5 Nov","updated":"Updated 14:02"}}
        """
        let snapshot = try XCTUnwrap(SnapshotCodec.decode(Data(json.utf8)))
        XCTAssertTrue(snapshot.isButtonOnly)
        XCTAssertNil(snapshot.streak)
        XCTAssertNil(snapshot.tasks)
    }

    func testUnknownSchemaAndGarbageDecodeToNil() {
        let newer = countsFixture.replacingOccurrences(of: "\"schemaVersion\":1", with: "\"schemaVersion\":2")
        XCTAssertNil(SnapshotCodec.decode(Data(newer.utf8)), "a newer schema is a placeholder, not a guess")
        XCTAssertNil(SnapshotCodec.decode(Data("not json".utf8)))
        XCTAssertNil(SnapshotCodec.decode(Data()))
    }

    func testStalenessFollowsTheCalendarDay() throws {
        var calendar = Calendar(identifier: .gregorian)
        calendar.timeZone = TimeZone(identifier: "Europe/Kyiv")!
        let snapshot = try XCTUnwrap(SnapshotCodec.decode(Data(countsFixture.utf8)))
        let sameDay = calendar.date(from: DateComponents(year: 2026, month: 11, day: 5, hour: 23, minute: 50))!
        let nextDay = calendar.date(from: DateComponents(year: 2026, month: 11, day: 6, hour: 0, minute: 10))!
        XCTAssertTrue(snapshot.isFor(day: sameDay, calendar: calendar))
        XCTAssertFalse(snapshot.isFor(day: nextDay, calendar: calendar))
        XCTAssertEqual(WidgetSnapshot.dayKey(nextDay, calendar: calendar), "2026-11-06")
    }

    func testPreviewSamplesSpeakBothLanguages() {
        XCTAssertEqual(PreviewData.sample(locale: "uk").labels.open, "Відкрити щоденник")
        XCTAssertEqual(PreviewData.sample(locale: "en-GB").labels.open, "Open diary")
        XCTAssertEqual(PreviewData.sample(locale: "uk").tasks?.items?.count, 5)
        XCTAssertTrue(PreviewData.sample().isFor(day: Date()))
    }
}
