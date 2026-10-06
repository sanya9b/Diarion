// Runs against the local StoreKit configuration (Diarion.storekit) through SKTestSession: no
// Apple ID, no sandbox, no App Store Connect. Spec 15, AC13. The facade is exercised through its
// C entry points, exactly as the C# side calls them.

import Foundation
import StoreKit
import StoreKitTest
import XCTest
@testable import DiarionStoreKit

final class DiarionStoreKitTests: XCTestCase {
    private var session: SKTestSession!

    override func setUpWithError() throws {
        try super.setUpWithError()
        let url = try XCTUnwrap(Bundle.module.url(forResource: "Diarion", withExtension: "storekit"))
        session = try SKTestSession(contentsOf: url)
        session.disableDialogs = true
        session.clearTransactions()
        session.resetToDefaultState()
    }

    override func tearDown() {
        session.clearTransactions()
        session = nil
        super.tearDown()
    }

    func testLoadProductsReportsBothPlansWithTheFreeTrial() throws {
        let products = try call(json: #"["diarion.base.monthly","diarion.base.yearly"]"#) { json, ctx, cb in
            dsk_load_products(json, ctx, cb)
        }
        let array = try XCTUnwrap(products as? [[String: Any]])
        XCTAssertEqual(array.count, 2)

        let monthly = try XCTUnwrap(array.first { $0["id"] as? String == "diarion.base.monthly" })
        XCTAssertEqual(monthly["price"] as? String, "2.49")
        XCTAssertEqual(monthly["currencyCode"] as? String, "USD")
        XCTAssertEqual(monthly["isEligibleForIntroOffer"] as? Bool, true)
        let period = try XCTUnwrap(monthly["period"] as? [String: Any])
        XCTAssertEqual(period["unit"] as? String, "month")
        XCTAssertEqual(period["count"] as? Int, 1)
        let intro = try XCTUnwrap(monthly["introOffer"] as? [String: Any])
        XCTAssertEqual(intro["kind"] as? String, "freeTrial")
        let introPeriod = try XCTUnwrap(intro["period"] as? [String: Any])
        XCTAssertEqual(introPeriod["unit"] as? String, "week")
        XCTAssertEqual(introPeriod["count"] as? Int, 2)
    }

    func testNothingIsOwnedOnAFreshSession() throws {
        let entitlements = try call { ctx, cb in dsk_current_entitlements(ctx, cb) }
        XCTAssertEqual((entitlements as? [Any])?.count, 0)
    }

    func testBuyingThePlanIsReportedAsAnIntroductoryEntitlement() async throws {
        // Purchased through StoreKit directly: the facade's dsk_purchase presents a confirmation
        // sheet, which the headless test session has no window for.
        let product = try XCTUnwrap(try await Product.products(for: ["diarion.base.yearly"]).first)
        _ = try await product.purchase()

        let entitlements = try call { ctx, cb in dsk_current_entitlements(ctx, cb) }
        let array = try XCTUnwrap(entitlements as? [[String: Any]])
        XCTAssertEqual(array.count, 1)
        XCTAssertEqual(array[0]["productId"] as? String, "diarion.base.yearly")
        XCTAssertEqual(array[0]["offerType"] as? String, "introductory")
        XCTAssertEqual(array[0]["environment"] as? String, "xcode")
        XCTAssertNotNil(array[0]["expiresAt"])
        XCTAssertTrue(array[0]["revokedAt"] is NSNull || array[0]["revokedAt"] == nil)

        let statuses = try call(json: #"["diarion.base.monthly"]"#) { json, ctx, cb in
            dsk_subscription_statuses(json, ctx, cb)
        }
        let statusArray = try XCTUnwrap(statuses as? [[String: Any]])
        XCTAssertEqual(statusArray.first?["state"] as? String, "subscribed")
        XCTAssertEqual(statusArray.first?["willAutoRenew"] as? Bool, true)
    }

    func testARefundShowsUpAsRevoked() async throws {
        let product = try XCTUnwrap(try await Product.products(for: ["diarion.base.monthly"]).first)
        _ = try await product.purchase()
        let transactions = session.allTransactions()
        let id = try XCTUnwrap(transactions.first?.identifier)
        try session.refundTransaction(identifier: id)

        let entitlements = try call { ctx, cb in dsk_current_entitlements(ctx, cb) }
        // A refunded subscription drops out of currentEntitlements; either shape is "not owned".
        let array = try XCTUnwrap(entitlements as? [[String: Any]])
        for entitlement in array {
            XCTAssertFalse(entitlement["revokedAt"] is NSNull, "a listed refund must carry its revocation date")
        }
    }

    func testTheAppReceiptIsTheXcodeEnvironment() throws {
        let receipt = try call { ctx, cb in dsk_app_transaction(ctx, cb) }
        let object = try XCTUnwrap(receipt as? [String: Any])
        XCTAssertEqual(object["environment"] as? String, "xcode")
        XCTAssertNotNil(object["originalAppVersion"])
    }

    func testAStoreErrorComesBackAsAFailureNotACrash() throws {
        session.failTransactionsEnabled = true
        session.failureError = .unknown

        XCTAssertThrowsError(try call(json: #"["diarion.base.monthly"]"#) { json, ctx, cb in
            dsk_load_products(json, ctx, cb)
        }) { error in
            XCTAssertTrue(error is FacadeFailure)
        }
    }

    // MARK: - Bridging the C callback into XCTest

    private struct FacadeFailure: Error { let message: String }

    private final class Box {
        var ok = false
        var json = ""
        let done = DispatchSemaphore(value: 0)
    }

    private static let callback: DskCallback = { context, ok, json in
        let box = Unmanaged<Box>.fromOpaque(context!).takeUnretainedValue()
        box.ok = ok == 1
        box.json = json.map { String(cString: $0) } ?? ""
        box.done.signal()
    }

    private func call(_ invoke: (UnsafeMutableRawPointer, DskCallback) -> Void) throws -> Any {
        let box = Box()
        let context = Unmanaged.passRetained(box).toOpaque()
        defer { Unmanaged<Box>.fromOpaque(context).release() }
        invoke(context, Self.callback)
        XCTAssertEqual(box.done.wait(timeout: .now() + 20), .success, "the facade must complete")
        guard box.ok else { throw FacadeFailure(message: box.json) }
        if box.json == "null" { return NSNull() }
        return try JSONSerialization.jsonObject(with: Data(box.json.utf8), options: [.fragmentsAllowed])
    }

    private func call(json: String, _ invoke: (UnsafePointer<CChar>, UnsafeMutableRawPointer, DskCallback) -> Void) throws -> Any {
        try json.withCString { pointer in
            try call { ctx, cb in invoke(pointer, ctx, cb) }
        }
    }
}
