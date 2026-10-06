using System.Linq;
using Diarion.Models;
using LiteDB;

namespace Diarion.Services.Database.Migrations;

/// <summary>
/// Introduces money accounts: ensures a single default account exists and assigns every transaction that
/// has no account to it. Idempotent — with a default account already present and transactions already
/// assigned, re-running does nothing.
/// </summary>
public sealed class M003_BackfillDefaultAccount : IMigration
{
    public int ToVersion => 3;

    public void Up(LiteDatabase db)
    {
        var accounts = db.GetCollection<Account>(DatabaseConstants.AccountsCollection);

        var defaultAccount = accounts.FindAll().OrderBy(a => a.CreatedAt).FirstOrDefault();
        if (defaultAccount == null)
        {
            // Store the resource key rather than a resolved string: migrations run at database-init
            // time, which can precede culture setup, and the stored name would be frozen forever.
            defaultAccount = new Account
            {
                Name = "Main",
                ResourceKey = "DefaultAccountName",
            };
            accounts.Insert(defaultAccount);
        }

        var transactions = db.GetCollection<FinanceTransaction>(DatabaseConstants.FinanceCollection);
        foreach (var tx in transactions.FindAll())
        {
            if (tx.AccountId == null)
            {
                tx.AccountId = defaultAccount.Id;
                transactions.Update(tx);
            }
        }
    }
}
