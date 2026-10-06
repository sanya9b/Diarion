using System;
using System.Linq;
using System.Threading.Tasks;
using Diarion.Models;
using Diarion.Services;
using Diarion.Services.Database;
using FluentAssertions;
using Xunit;

namespace Diarion.Tests;

public class FinanceAccountServiceTests : IDisposable
{
    private readonly DatabaseContext _dbContext;
    private readonly FinanceService _service;

    public FinanceAccountServiceTests()
    {
        _dbContext = new DatabaseContext(useInMemory: true);
        _service = new FinanceService(_dbContext);

        // The context runs migrations on construction, which seeds a default account.
        _dbContext.GetCollection<Account>(DatabaseConstants.AccountsCollection).DeleteAll();
    }

    public void Dispose() => _dbContext.Dispose();

    private async Task<Account> AddAccountAsync(string name, bool archived = false)
    {
        var account = new Account { Name = name, IsArchived = archived };
        await _service.SaveAccountAsync(account);
        return account;
    }

    [Fact]
    public async Task GetAccountsAsync_HidesArchivedUnlessAsked()
    {
        await AddAccountAsync("Cash");
        await AddAccountAsync("Old", archived: true);

        (await _service.GetAccountsAsync()).Should().ContainSingle(a => a.Name == "Cash");
        (await _service.GetAccountsAsync(includeArchived: true)).Should().HaveCount(2);
    }

    [Fact]
    public async Task DeleteAccountAsync_MovesTransactionsToTheChosenAccount()
    {
        var doomed = await AddAccountAsync("Doomed");
        var keeper = await AddAccountAsync("Keeper");

        var tx = new FinanceTransaction { Amount = 10m, Date = DateTime.Today, AccountId = doomed.Id };
        await _service.SaveFinanceTransactionAsync(tx);

        await _service.DeleteAccountAsync(doomed.Id, keeper.Id);

        (await _service.GetAccountsAsync()).Should().ContainSingle(a => a.Id == keeper.Id);
        (await _service.GetFinanceTransactionsAsync()).Single().AccountId.Should().Be(keeper.Id);
    }

    [Fact]
    public async Task DeleteAccountAsync_RepointsTransfersOnBothLegs()
    {
        var doomed = await AddAccountAsync("Doomed");
        var keeper = await AddAccountAsync("Keeper");
        var third = await AddAccountAsync("Third");

        await _service.SaveTransferAsync(new Transfer { FromAccountId = doomed.Id, ToAccountId = third.Id, Amount = 50m });
        await _service.SaveTransferAsync(new Transfer { FromAccountId = third.Id, ToAccountId = doomed.Id, Amount = 20m });

        await _service.DeleteAccountAsync(doomed.Id, keeper.Id);

        var transfers = await _service.GetTransfersAsync();
        transfers.Should().HaveCount(2);
        transfers.Should().ContainSingle(t => t.FromAccountId == keeper.Id && t.ToAccountId == third.Id);
        transfers.Should().ContainSingle(t => t.FromAccountId == third.Id && t.ToAccountId == keeper.Id);
    }

    [Fact]
    public async Task DeleteAccountAsync_DropsTransfersThatCollapseOntoOneAccount()
    {
        var doomed = await AddAccountAsync("Doomed");
        var keeper = await AddAccountAsync("Keeper");

        await _service.SaveTransferAsync(new Transfer { FromAccountId = doomed.Id, ToAccountId = keeper.Id, Amount = 50m });

        await _service.DeleteAccountAsync(doomed.Id, keeper.Id);

        // Both legs now point at Keeper, so the transfer moves nothing and would double-count nowhere.
        (await _service.GetTransfersAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task SaveTransferAsync_RoundTripsAndDeletes()
    {
        var a = await AddAccountAsync("A");
        var b = await AddAccountAsync("B");
        var transfer = new Transfer { FromAccountId = a.Id, ToAccountId = b.Id, Amount = 75m, Note = "Top-up" };

        await _service.SaveTransferAsync(transfer);

        var stored = (await _service.GetTransfersAsync()).Should().ContainSingle().Subject;
        stored.Amount.Should().Be(75m);
        stored.Note.Should().Be("Top-up");

        await _service.DeleteTransferAsync(transfer.Id);
        (await _service.GetTransfersAsync()).Should().BeEmpty();
    }
}
