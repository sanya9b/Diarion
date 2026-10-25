// Runs against the local StoreKit configuration (Diarion.storekit) through SKTestSession: no
// Apple ID, no sandbox, no App Store Connect. Spec 15, AC13. The facade is exercised through its
// C entry points, exactly as the C# side calls them.
//
// Every test is async and awaits the facade's callback through a continuation. The first version
// blocked on a semaphore instead, and the run hung for half an hour with no output: a test thread
// parked on a semaphore is a thread the Swift concurrency pool cannot hand to the facade's Task.

import Foundation
import StoreKit
import StoreKitTest
import XCTest
@testable import DiarionStoreKit

final class DiarionStoreKitTests: XCTestCase {
    private var session: SKTestSession!

    override func setUp() async throws {
        try await super.setUp()
        let url = try XCTUnwrap(Bundle.module.url(forResource: "Diarion", withExtension: "storekit"))
        session = try SKTestSession(contentsOf: url)
        session.disableDialogs = true
        session.clearTransactions()
        session.resetToDefaultState()
    }

    override func tearDown() async throws {
        session.clearTransactions()
        session = nil
        try await super.tearDown()
    }

    func testLoadProductsReportsAllThreePlansWithTheFreeTrial() async throws {
        let products = try await call(json: #"["diarion.base.monthly","diarion.base.quarterly","diarion.base.yearly"]"#) { json, ctx, cb in
            dsk_load_products(json, ctx, cb)
        }
        let array = try XCTUnwrap(products as? [[String: Any]])
        XCTAssertEqual(array.count, 3)

        let quarterly = try XCTUnwrap(array.first { $0["id"] as? String == "diarion.base.quarterly" })
        XCTAssertEqual(quarterly["price"] as? String, "4.99")
        let quarter = try XCTUnwrap(quarterly["period"] as? [String: Any])
        XCTAssertEqual(quarter["unit"] as? String, "month")
        XCTAssertEqual(quarter["count"] as? Int, 3)

        let monthly = try XCTUnwrap(array.first { $0["id"] as? String == "diarion.base.monthly" })
        XCTAssertEqual(monthly["price"] as? String, "1.99")
        XCTAssertEqual(monthly["currencyCode"] as? String, "USD")
        XCTAssertEqual(monthly["isEligibleForIntroOffer"] as? Bool, true)
        let period = try XCTUnwrap(monthly["period"] as? [String: Any])
        XCTAssertEqual(period["unit"] as? String, "month")
        XCTAssertEqual(period["count"] as? Int, 1)
        let intro = try XCTUnwrap(monthly["introOffer"] as? [String: Any])
        XCTAssertEqual(intro["kind"] as? String, "freeTrial")
        let introPeriod = try XCTUnwrap(intro["period"] as? [String: Any])
        XCTAssertEqual(introPeriod["unit"] as? String, "day")
        XCTAssertEqual(introPeriod["count"] as? Int, 3)
    }

    func testNothingIsOwnedOnAFreshSession() async throws {
        let entitlements = try await call { ctx, cb in dsk_current_entitlements(ctx, cb) }
        XCTAssertEqual((entitlements as? [Any])?.count, 0)
    }

    func testRenewalStatusesOnAFreshSessionAreEmpty() async throws {
        let statuses = try await call(json: #"["diarion.base.monthly","diarion.base.quarterly","diarion.base.yearly"]"#) { json, ctx, cb in
            dsk_subscription_statuses(json, ctx, cb)
        }
        XCTAssertEqual((statuses as? [Any])?.count, 0)
    }

    func testAnUnknownProductIsSimplyAbsent() async throws {
        let products = try await call(json: #"["diarion.base.monthly","no.such.product"]"#) { json, ctx, cb in
            dsk_load_products(json, ctx, cb)
        }
        let ids = try XCTUnwrap(products as? [[String: Any]]).compactMap { $0["id"] as? String }
        XCTAssertEqual(ids, ["diarion.base.monthly"])
    }

    /// A package test bundle has no host app, and StoreKit will not produce an AppTransaction (or
    /// complete a purchase) without one — it answers "unknown". What can be checked here is that the
    /// facade turns that into a failure through the callback, which C# maps to StoreException,
    /// instead of crashing or never answering. Purchases, refunds and the receipt itself are
    /// checked on a phone against the sandbox (spec 15, test plan).
    func testTheReceiptAnswersThroughTheCallbackEvenWithoutAHostApp() async throws {
        do {
            let receipt = try await call { ctx, cb in dsk_app_transaction(ctx, cb) }
            let object = try XCTUnwrap(receipt as? [String: Any])
            XCTAssertNotNil(object["originalAppVersion"] as? String)
            XCTAssertNotNil(object["environment"] as? String)
        } catch let failure as FacadeFailure {
            XCTAssertFalse(failure.message.isEmpty, "a failure must say something")
        }
    }

    // MARK: - Bridging the C callback into async/await

    private struct FacadeFailure: Error { let message: String }

    private final class Box {
        let continuation: CheckedContinuation<(Bool, String), Never>
        init(_ continuation: CheckedContinuation<(Bool, String), Never>) { self.continuation = continuation }
    }

    private static let callback: DskCallback = { context, ok, json in
        let box = Unmanaged<Box>.fromOpaque(context!).takeRetainedValue()
        box.continuation.resume(returning: (ok == 1, json.map { String(cString: $0) } ?? ""))
    }

    private func call(_ invoke: (UnsafeMutableRawPointer, DskCallback) -> Void) async throws -> Any {
        let (ok, json) = await withCheckedContinuation { continuation in
            let context = Unmanaged.passRetained(Box(continuation)).toOpaque()
            invoke(context, Self.callback)
        }
        guard ok else { throw FacadeFailure(message: json) }
        if json == "null" { return NSNull() }
        return try JSONSerialization.jsonObject(with: Data(json.utf8), options: [.fragmentsAllowed])
    }

    private func call(json: String, _ invoke: (UnsafePointer<CChar>, UnsafeMutableRawPointer, DskCallback) -> Void) async throws -> Any {
        // Copied, not borrowed with withCString: the pointer has to outlive this synchronous call
        // only for as long as the facade takes to copy it, which it does before its first await.
        let pointer = try XCTUnwrap(strdup(json))
        defer { free(pointer) }
        return try await call { ctx, cb in invoke(UnsafePointer(pointer), ctx, cb) }
    }
}
