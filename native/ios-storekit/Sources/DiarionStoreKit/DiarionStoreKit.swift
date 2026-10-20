// DiarionStoreKit.swift — the C ABI facade over StoreKit 2 that Diarion's C# side P/Invokes
// (Platforms/iOS/Store/StoreKitClient.cs). Spec 15, R3.2: no logic lives here. Every function
// calls StoreKit, serialises what came back and completes exactly once through the callback
// (context, ok, utf8Json) — except dsk_start_updates, which completes once per transaction update
// for as long as the app runs.
//
// Contract, mirrored by StoreKitJson.cs in Diarion.Core — change both or neither:
//   ok=1 → json is the payload; ok=0 → json is the error text
//   camelCase keys, ISO 8601 UTC dates, prices as decimal strings ("2.49"), never doubles

import Foundation
import StoreKit
import UIKit

public typealias DskCallback = @convention(c) (UnsafeMutableRawPointer?, UInt8, UnsafePointer<CChar>?) -> Void

// MARK: - Completion

private let encoder: JSONEncoder = {
    let encoder = JSONEncoder()
    encoder.dateEncodingStrategy = .iso8601
    return encoder
}()

private func complete(_ context: UnsafeMutableRawPointer?, _ callback: DskCallback, ok: Bool, _ json: String) {
    json.withCString { callback(context, ok ? 1 : 0, $0) }
}

private func completeError(_ context: UnsafeMutableRawPointer?, _ callback: DskCallback, _ error: Error) {
    complete(context, callback, ok: false, String(describing: error))
}

private func completeValue<T: Encodable>(_ context: UnsafeMutableRawPointer?, _ callback: DskCallback, _ value: T) {
    do {
        let data = try encoder.encode(value)
        complete(context, callback, ok: true, String(decoding: data, as: UTF8.self))
    } catch {
        completeError(context, callback, error)
    }
}

private struct FacadeError: LocalizedError {
    let message: String
    var errorDescription: String? { message }
}

private func checked<T>(_ result: VerificationResult<T>) throws -> T {
    switch result {
    case .verified(let value): return value
    case .unverified(_, let error): throw error
    }
}

/// Copies a C string at call time, before any `Task` runs: the caller frees it as soon as the
/// function returns.
private func string(_ pointer: UnsafePointer<CChar>?) -> String {
    pointer.map { String(cString: $0) } ?? ""
}

private func decodeIds(_ json: String) throws -> [String] {
    try JSONDecoder().decode([String].self, from: Data(json.utf8))
}

// MARK: - Wire shapes

private struct PeriodJson: Encodable {
    let unit: String
    let count: Int

    init(_ period: Product.SubscriptionPeriod) {
        count = period.value
        switch period.unit {
        case .day: unit = "day"
        case .week: unit = "week"
        case .month: unit = "month"
        case .year: unit = "year"
        @unknown default: unit = "day"
        }
    }
}

private struct IntroOfferJson: Encodable {
    let kind: String
    let period: PeriodJson
    let periodCount: Int
    let price: String
    let displayPrice: String

    init(_ offer: Product.SubscriptionOffer) {
        switch offer.paymentMode {
        case .freeTrial: kind = "freeTrial"
        case .payAsYouGo: kind = "payAsYouGo"
        case .payUpFront: kind = "payUpFront"
        default: kind = "payUpFront"
        }
        period = PeriodJson(offer.period)
        periodCount = offer.periodCount
        price = "\(offer.price)"
        displayPrice = offer.displayPrice
    }
}

private struct ProductJson: Encodable {
    let id: String
    let displayName: String
    let displayPrice: String
    let price: String
    let currencyCode: String
    let period: PeriodJson?
    let introOffer: IntroOfferJson?
    let isEligibleForIntroOffer: Bool
}

private struct EntitlementJson: Encodable {
    let productId: String
    let expiresAt: Date?
    let offerType: String
    let environment: String
    let revokedAt: Date?

    init(_ transaction: Transaction) {
        productId = transaction.productID
        expiresAt = transaction.expirationDate
        offerType = offerTypeName(transaction)
        environment = environmentName(transaction.environment)
        revokedAt = transaction.revocationDate
    }
}

private struct StatusJson: Encodable {
    let productId: String
    let state: String
    let willAutoRenew: Bool
    let gracePeriodExpiresAt: Date?
}

private struct AppTransactionJson: Encodable {
    let originalAppVersion: String
    let environment: String
    let originalPurchaseDate: Date?
}

private struct PurchaseJson: Encodable {
    let state: String
    let entitlement: EntitlementJson?
}

private func environmentName(_ environment: AppStore.Environment) -> String {
    switch environment {
    case .production: return "production"
    case .sandbox: return "sandbox"
    case .xcode: return "xcode"
    default: return "unknown"
    }
}

private func offerTypeName(_ transaction: Transaction) -> String {
    if #available(iOS 17.2, *) {
        guard let type = transaction.offer?.type else { return "none" }
        return offerTypeName(type)
    }
    guard let type = transaction.offerType else { return "none" }
    return offerTypeName(type)
}

private func offerTypeName(_ type: Transaction.OfferType) -> String {
    switch type {
    case .introductory: return "introductory"
    case .promotional: return "promotional"
    case .code: return "code"
    default:
        if #available(iOS 18.0, *), type == .winBack { return "winBack" }
        return "none"
    }
}

private func renewalStateName(_ state: Product.SubscriptionInfo.RenewalState) -> String {
    switch state {
    case .subscribed: return "subscribed"
    case .expired: return "expired"
    case .inBillingRetryPeriod: return "inBillingRetry"
    case .inGracePeriod: return "inGracePeriod"
    case .revoked: return "revoked"
    default: return "unknown"
    }
}

@MainActor
private func foregroundScene() -> UIWindowScene? {
    let scenes = UIApplication.shared.connectedScenes.compactMap { $0 as? UIWindowScene }
    return scenes.first { $0.activationState == .foregroundActive } ?? scenes.first
}

// MARK: - Products

@_cdecl("dsk_load_products")
public func dsk_load_products(
    _ productIdsJson: UnsafePointer<CChar>?,
    _ context: UnsafeMutableRawPointer?,
    _ callback: @escaping DskCallback
) {
    let idsJson = string(productIdsJson)
    Task {
        do {
            let products = try await Product.products(for: try decodeIds(idsJson))
            var mapped: [ProductJson] = []
            for product in products {
                let subscription = product.subscription
                var eligible = false
                if let subscription {
                    eligible = await subscription.isEligibleForIntroOffer
                }
                mapped.append(ProductJson(
                    id: product.id,
                    displayName: product.displayName,
                    displayPrice: product.displayPrice,
                    price: "\(product.price)",
                    currencyCode: product.priceFormatStyle.currencyCode,
                    period: subscription.map { PeriodJson($0.subscriptionPeriod) },
                    introOffer: subscription?.introductoryOffer.map(IntroOfferJson.init),
                    isEligibleForIntroOffer: eligible))
            }
            completeValue(context, callback, mapped)
        } catch {
            completeError(context, callback, error)
        }
    }
}

// MARK: - Purchase

@_cdecl("dsk_purchase")
public func dsk_purchase(
    _ productIdC: UnsafePointer<CChar>?,
    _ context: UnsafeMutableRawPointer?,
    _ callback: @escaping DskCallback
) {
    let productId = string(productIdC)
    Task {
        do {
            guard let product = try await Product.products(for: [productId]).first else {
                throw FacadeError(message: "unknown product: \(productId)")
            }

            let result: Product.PurchaseResult
            if #available(iOS 17.0, *), let scene = await foregroundScene() {
                result = try await product.purchase(confirmIn: scene)
            } else {
                result = try await product.purchase()
            }

            switch result {
            case .success(let verification):
                let transaction = try checked(verification)
                await transaction.finish()
                completeValue(context, callback, PurchaseJson(state: "purchased", entitlement: EntitlementJson(transaction)))
            case .pending:
                completeValue(context, callback, PurchaseJson(state: "pending", entitlement: nil))
            case .userCancelled:
                completeValue(context, callback, PurchaseJson(state: "cancelled", entitlement: nil))
            @unknown default:
                completeValue(context, callback, PurchaseJson(state: "pending", entitlement: nil))
            }
        } catch {
            completeError(context, callback, error)
        }
    }
}

// MARK: - Entitlements and renewal state

@_cdecl("dsk_current_entitlements")
public func dsk_current_entitlements(
    _ context: UnsafeMutableRawPointer?,
    _ callback: @escaping DskCallback
) {
    Task {
        var entitlements: [EntitlementJson] = []
        for await verification in Transaction.currentEntitlements {
            if let transaction = try? checked(verification) {
                entitlements.append(EntitlementJson(transaction))
            }
        }
        completeValue(context, callback, entitlements)
    }
}

@_cdecl("dsk_subscription_statuses")
public func dsk_subscription_statuses(
    _ productIdsJson: UnsafePointer<CChar>?,
    _ context: UnsafeMutableRawPointer?,
    _ callback: @escaping DskCallback
) {
    let idsJson = string(productIdsJson)
    Task {
        do {
            let products = try await Product.products(for: try decodeIds(idsJson))
            // Every product in the group shares one status list; any one of them answers for all.
            guard let subscription = products.compactMap({ $0.subscription }).first else {
                completeValue(context, callback, [StatusJson]())
                return
            }
            var statuses: [StatusJson] = []
            for status in try await subscription.status {
                let renewal = try? checked(status.renewalInfo)
                let transaction = try? checked(status.transaction)
                statuses.append(StatusJson(
                    productId: renewal?.currentProductID ?? transaction?.productID ?? "",
                    state: renewalStateName(status.state),
                    willAutoRenew: renewal?.willAutoRenew ?? false,
                    gracePeriodExpiresAt: renewal?.gracePeriodExpirationDate))
            }
            completeValue(context, callback, statuses)
        } catch {
            completeError(context, callback, error)
        }
    }
}

// MARK: - Restore, receipt, management

@_cdecl("dsk_sync")
public func dsk_sync(
    _ context: UnsafeMutableRawPointer?,
    _ callback: @escaping DskCallback
) {
    Task {
        do {
            try await AppStore.sync()
            complete(context, callback, ok: true, "null")
        } catch {
            completeError(context, callback, error)
        }
    }
}

@_cdecl("dsk_app_transaction")
public func dsk_app_transaction(
    _ context: UnsafeMutableRawPointer?,
    _ callback: @escaping DskCallback
) {
    Task {
        do {
            let appTransaction = try checked(try await AppTransaction.shared)
            completeValue(context, callback, AppTransactionJson(
                originalAppVersion: appTransaction.originalAppVersion,
                environment: environmentName(appTransaction.environment),
                originalPurchaseDate: appTransaction.originalPurchaseDate))
        } catch {
            completeError(context, callback, error)
        }
    }
}

@_cdecl("dsk_manage_subscriptions")
public func dsk_manage_subscriptions(
    _ context: UnsafeMutableRawPointer?,
    _ callback: @escaping DskCallback
) {
    Task { @MainActor in
        do {
            guard let scene = foregroundScene() else {
                throw FacadeError(message: "no window scene to present in")
            }
            try await AppStore.showManageSubscriptions(in: scene)
            complete(context, callback, ok: true, "null")
        } catch {
            completeError(context, callback, error)
        }
    }
}

// MARK: - Updates

private var updatesStarted = false

/// Listens for renewals, refunds and Ask-to-Buy approvals for the life of the process. Each one
/// is finished here and reported through the callback with an empty object; the C# side then
/// re-reads the entitlements. Calling this twice is harmless.
@_cdecl("dsk_start_updates")
public func dsk_start_updates(
    _ context: UnsafeMutableRawPointer?,
    _ callback: @escaping DskCallback
) {
    guard !updatesStarted else { return }
    updatesStarted = true
    Task.detached {
        for await verification in Transaction.updates {
            if let transaction = try? checked(verification) {
                await transaction.finish()
            }
            complete(context, callback, ok: true, "{}")
        }
    }
}
