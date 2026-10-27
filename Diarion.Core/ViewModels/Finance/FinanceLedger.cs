using System;
using System.Collections.Generic;
using System.Linq;
using Diarion.Models;
using Diarion.Services;

namespace Diarion.ViewModels.Finance;

/// <summary>
/// The finance page's loaded data, shared by the page's sections. <see cref="FinanceViewModel"/> fills
/// it on every load; the sections read it and never fetch on their own, so one load cannot show two
/// different pictures of the ledger. Plain state, no notifications: the sections rebuild their own
/// observable collections from it when the owner says the load is done.
/// </summary>
public sealed class FinanceLedger
{
    public List<Account> Accounts { get; set; } = new();

    /// <summary>All transactions unfiltered; the feed and the balances apply the account filter.</summary>
    public List<FinanceTransaction> Transactions { get; set; } = new();

    public List<Transfer> Transfers { get; set; } = new();

    public List<PendingOccurrence> Pending { get; set; } = new();

    /// <summary>
    /// The currency every amount on the page is rendered in. Held once rather than read per row so a
    /// single load cannot show two different currencies if the profile changes mid-flight.
    /// </summary>
    public string CurrencyCode { get; set; } = MoneyFormatter.FallbackCode;

    /// <summary>Account the strip is filtered to; null means all accounts.</summary>
    public Guid? SelectedAccountId { get; set; }

    /// <summary>
    /// Archived accounts stay in <see cref="Accounts"/> because their opening balance and transactions
    /// still count towards the total; they are only hidden from the strip.
    /// </summary>
    public IEnumerable<Account> ActiveAccounts => Accounts.Where(a => !a.IsArchived);

    public Guid? DefaultAccountId => (ActiveAccounts.FirstOrDefault() ?? Accounts.FirstOrDefault())?.Id;

    public Account? Find(Guid? id) => id == null ? null : Accounts.FirstOrDefault(a => a.Id == id);

    public string ResolveAccountName(Guid id) => AccountLocalization.ResolveName(Find(id));

    public string Money(decimal amount) => MoneyFormatter.Format(amount, CurrencyCode);

    /// <summary>Parses an amount the way every finance form does: comma or dot, invariant, positive.</summary>
    public static bool TryParseAmount(string? text, out decimal amount)
    {
        amount = 0;
        if (string.IsNullOrWhiteSpace(text)) return false;

        return decimal.TryParse(text.Replace(",", "."),
                   System.Globalization.NumberStyles.Any,
                   System.Globalization.CultureInfo.InvariantCulture,
                   out amount)
               && amount > 0;
    }
}
