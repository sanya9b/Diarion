using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Diarion.Models;
using Diarion.Services;
using Diarion.ViewModels;
using FluentAssertions;
using Moq;
using Xunit;

namespace Diarion.Tests;

public class FinanceViewModelTests
{
    /// <summary>
    /// LoadAsync reads accounts and transfers as well as transactions; an unstubbed Moq member returns
    /// null there, so every test that loads needs these defaults.
    /// </summary>
    private static Mock<IFinanceService> FinanceMock(
        List<Account>? accounts = null,
        List<Transfer>? transfers = null)
    {
        var mock = new Mock<IFinanceService>();
        mock.Setup(s => s.GetAccountsAsync(It.IsAny<bool>())).ReturnsAsync(accounts ?? new List<Account>());
        mock.Setup(s => s.GetTransfersAsync()).ReturnsAsync(transfers ?? new List<Transfer>());
        mock.Setup(s => s.GetFinanceTransactionsAsync()).ReturnsAsync(new List<FinanceTransaction>());
        mock.Setup(s => s.GetBudgetsAsync()).ReturnsAsync(new List<Budget>());
        return mock;
    }

    private static FinanceViewModel NewViewModel(Mock<IFinanceService> finance, IDialogService? dialog = null) =>
        new(finance.Object, dialog ?? new Mock<IDialogService>().Object, new Mock<IProfileService>().Object);

    [Fact]
    public async Task LoadAsync_WithSavedTransactions_CalculatesBalancesCorrectly()
    {
        // Arrange
        var currentMonth = DateTime.Today.Month;
        var currentYear = DateTime.Today.Year;

        var diaryServiceMock = FinanceMock();
        diaryServiceMock
            .Setup(s => s.GetFinanceTransactionsAsync())
            .ReturnsAsync(new List<FinanceTransaction>
            {
                // This month
                new() { Type = TransactionType.Income, Amount = 1000m, Date = new DateTime(currentYear, currentMonth, 1) },
                new() { Type = TransactionType.Expense, Amount = 200m, Date = new DateTime(currentYear, currentMonth, 5) },
                new() { Type = TransactionType.Expense, Amount = 50.5m, Date = new DateTime(currentYear, currentMonth, 10) },
                
                // Last month
                new() { Type = TransactionType.Income, Amount = 500m, Date = new DateTime(currentYear, currentMonth, 1).AddMonths(-1) },
                new() { Type = TransactionType.Expense, Amount = 100m, Date = new DateTime(currentYear, currentMonth, 1).AddMonths(-1) }
            });

        var viewModel = new FinanceViewModel(diaryServiceMock.Object, new Mock<IDialogService>().Object, new Mock<IProfileService>().Object);

        // Act
        await viewModel.LoadAsync();

        // Assert
        viewModel.Transactions.Should().HaveCount(5);
        
        // Total balance = (1000 + 500) - (200 + 50.5 + 100) = 1500 - 350.5 = 1149.5
        viewModel.TotalBalance.Should().Be(1149.5m);
        
        // This month income = 1000
        viewModel.MonthIncome.Should().Be(1000m);
        
        // This month expense = 200 + 50.5 = 250.5
        viewModel.MonthExpense.Should().Be(250.5m);
    }

    [Fact]
    public async Task SaveTransactionAsync_WithValidData_SavesAndReloads()
    {
        // Arrange
        var storedTransactions = new List<FinanceTransaction>();
        var diaryServiceMock = FinanceMock();
        diaryServiceMock
            .Setup(s => s.GetFinanceTransactionsAsync())
            .ReturnsAsync(() => storedTransactions.OrderByDescending(x => x.Date).ToList());

        diaryServiceMock
            .Setup(s => s.SaveFinanceTransactionAsync(It.IsAny<FinanceTransaction>()))
            .Returns<FinanceTransaction>(transaction =>
            {
                storedTransactions.Add(transaction);
                return Task.CompletedTask;
            });

        var viewModel = new FinanceViewModel(diaryServiceMock.Object, new Mock<IDialogService>().Object, new Mock<IProfileService>().Object);
        await viewModel.LoadAsync();

        viewModel.NewTransactionType = TransactionType.Expense;
        viewModel.NewAmountText = "150,75"; // Testing comma as decimal separator
        viewModel.NewCategory = " Groceries ";
        viewModel.NewDate = new DateTime(2025, 6, 15);

        // Act
        await viewModel.SaveTransactionCommand.ExecuteAsync(null);

        // Assert
        storedTransactions.Should().ContainSingle();
        storedTransactions[0].Amount.Should().Be(150.75m);
        storedTransactions[0].Type.Should().Be(TransactionType.Expense);
        storedTransactions[0].Category.Should().Be("Groceries");
        
        viewModel.Transactions.Should().HaveCount(1);
        viewModel.NewAmountText.Should().BeEmpty();
        viewModel.NewCategory.Should().BeEmpty();
        viewModel.IsAddTransactionVisible.Should().BeFalse();
    }

    [Fact]
    public async Task SaveTransactionAsync_WithInvalidAmount_DoesNotSave()
    {
        // Arrange
        var diaryServiceMock = new Mock<IFinanceService>();
        var viewModel = new FinanceViewModel(diaryServiceMock.Object, new Mock<IDialogService>().Object, new Mock<IProfileService>().Object);
        
        viewModel.NewAmountText = "invalid_number";

        // Act
        await viewModel.SaveTransactionCommand.ExecuteAsync(null);

        // Assert
        diaryServiceMock.Verify(s => s.SaveFinanceTransactionAsync(It.IsAny<FinanceTransaction>()), Times.Never);
    }

    [Fact]
    public async Task SaveTransactionAsync_WithNegativeAmount_DoesNotSave()
    {
        // Arrange
        var diaryServiceMock = new Mock<IFinanceService>();
        var viewModel = new FinanceViewModel(diaryServiceMock.Object, new Mock<IDialogService>().Object, new Mock<IProfileService>().Object);
        
        viewModel.NewAmountText = "-50";

        // Act
        await viewModel.SaveTransactionCommand.ExecuteAsync(null);

        // Assert
        diaryServiceMock.Verify(s => s.SaveFinanceTransactionAsync(It.IsAny<FinanceTransaction>()), Times.Never);
    }

    [Fact]
    public async Task ToggleAddTransaction_LoadsCategoriesAndPopulatesSuggestions()
    {
        // Arrange
        var financeServiceMock = new Mock<IFinanceService>();
        financeServiceMock
            .Setup(s => s.GetCategoriesAsync(TransactionType.Expense))
            .ReturnsAsync(new List<string> { "Groceries", "Transport", "Entertainment" });
            
        var viewModel = new FinanceViewModel(financeServiceMock.Object, new Mock<IDialogService>().Object, new Mock<IProfileService>().Object);
        
        // Act - Open the add dialog
        await viewModel.ToggleAddTransactionCommand.ExecuteAsync(null);
        
        // Assert
        viewModel.IsAddTransactionVisible.Should().BeTrue();
        financeServiceMock.Verify(s => s.GetCategoriesAsync(TransactionType.Expense), Times.Once);
        viewModel.SuggestedCategories.Should().HaveCount(3);
        viewModel.SuggestedCategories.Should().Contain("Groceries");
    }

    [Fact]
    public async Task SetTransactionType_ReloadsCategoriesForSelectedType()
    {
        // Arrange
        var financeServiceMock = new Mock<IFinanceService>();
        financeServiceMock
            .Setup(s => s.GetCategoriesAsync(TransactionType.Expense))
            .ReturnsAsync(new List<string> { "Groceries" });
        financeServiceMock
            .Setup(s => s.GetCategoriesAsync(TransactionType.Income))
            .ReturnsAsync(new List<string> { "Salary", "Bonus" });
            
        var viewModel = new FinanceViewModel(financeServiceMock.Object, new Mock<IDialogService>().Object, new Mock<IProfileService>().Object);
        await viewModel.ToggleAddTransactionCommand.ExecuteAsync(null); // Defaults to Expense
        
        // Act
        await viewModel.SetTransactionTypeCommand.ExecuteAsync("Income");
        
        // Assert
        viewModel.IsIncomeTypeSelected.Should().BeTrue();
        financeServiceMock.Verify(s => s.GetCategoriesAsync(TransactionType.Income), Times.Once);
        viewModel.SuggestedCategories.Should().HaveCount(2);
        viewModel.SuggestedCategories.Should().Contain("Salary");
    }

    [Fact]
    public async Task SelectCategory_SetsNewCategoryAndClearsSuggestions()
    {
        // Arrange
        var financeServiceMock = new Mock<IFinanceService>();
        financeServiceMock
            .Setup(s => s.GetCategoriesAsync(It.IsAny<TransactionType>()))
            .ReturnsAsync(new List<string> { "Groceries" });
            
        var viewModel = new FinanceViewModel(financeServiceMock.Object, new Mock<IDialogService>().Object, new Mock<IProfileService>().Object);
        await viewModel.ToggleAddTransactionCommand.ExecuteAsync(null);
        
        // Ensure suggestions are initially populated
        viewModel.SuggestedCategories.Should().NotBeEmpty();

        // Act
        viewModel.SelectCategoryCommand.Execute("Groceries");

        // Assert
        viewModel.NewCategory.Should().Be("Groceries");
        viewModel.SuggestedCategories.Should().BeEmpty();
    }

    [Fact]
    public async Task LoadAsync_ComputesBudgetsForCurrentMonth()
    {
        var financeMock = FinanceMock();
        financeMock.Setup(s => s.GetFinanceTransactionsAsync()).ReturnsAsync(new List<FinanceTransaction>
        {
            new() { Type = TransactionType.Expense, Category = "Food", Amount = 40m, Date = DateTime.Today }
        });
        financeMock.Setup(s => s.GetBudgetsAsync()).ReturnsAsync(new List<Budget>
        {
            new() { Category = "Food", MonthlyLimit = 100m }
        });

        var viewModel = new FinanceViewModel(financeMock.Object, new Mock<IDialogService>().Object, new Mock<IProfileService>().Object);
        await viewModel.LoadAsync();

        viewModel.HasBudgets.Should().BeTrue();
        var b = viewModel.Budgets.Should().ContainSingle().Subject;
        b.Category.Should().Be("Food");
        b.IsOverspent.Should().BeFalse();
        b.AmountText.Should().Contain("40");
    }

    [Fact]
    public async Task SaveBudget_WithValidInput_SavesAndClosesForm()
    {
        var financeMock = FinanceMock();

        var viewModel = NewViewModel(financeMock);
        viewModel.NewBudgetCategory = "Food";
        viewModel.NewBudgetLimitText = "150";

        await viewModel.SaveBudgetCommand.ExecuteAsync(null);

        financeMock.Verify(s => s.SaveBudgetAsync(It.Is<Budget>(b => b.Category == "Food" && b.MonthlyLimit == 150m)), Times.Once);
        viewModel.IsBudgetFormVisible.Should().BeFalse();
    }

    [Fact]
    public async Task LoadAsync_ShowBudgets_ReflectsProfileFlag()
    {
        var financeMock = FinanceMock();
        var profileMock = new Mock<IProfileService>();
        profileMock.Setup(p => p.GetUserProfileAsync()).ReturnsAsync(new UserProfile { IsBudgetsEnabled = false });

        var viewModel = new FinanceViewModel(financeMock.Object, new Mock<IDialogService>().Object, profileMock.Object);
        await viewModel.LoadAsync();

        viewModel.ShowBudgets.Should().BeFalse();
    }

    [Fact]
    public async Task ShowCategoryDetail_FiltersCurrentMonthExpensesForCategory()
    {
        var today = DateTime.Today;
        var financeMock = FinanceMock();
        financeMock.Setup(s => s.GetFinanceTransactionsAsync()).ReturnsAsync(new List<FinanceTransaction>
        {
            new() { Type = TransactionType.Expense, Category = "Food", Amount = 10m, Date = today },
            new() { Type = TransactionType.Expense, Category = "Food", Amount = 20m, Date = today.AddMonths(-1) }, // other month
            new() { Type = TransactionType.Income, Category = "Food", Amount = 99m, Date = today },                // income
            new() { Type = TransactionType.Expense, Category = "Transport", Amount = 5m, Date = today }            // other category
        });
        financeMock.Setup(s => s.GetBudgetsAsync()).ReturnsAsync(new List<Budget> { new() { Category = "Food", MonthlyLimit = 100m } });

        var viewModel = new FinanceViewModel(financeMock.Object, new Mock<IDialogService>().Object, new Mock<IProfileService>().Object);
        await viewModel.LoadAsync();

        var foodBudget = viewModel.Budgets.First(b => b.Category == "Food");
        viewModel.ShowCategoryDetailCommand.Execute(foodBudget);

        viewModel.IsCategoryDetailVisible.Should().BeTrue();
        viewModel.CategoryDetailTitle.Should().Be("Food");
        viewModel.HasCategoryTransactions.Should().BeTrue();
        viewModel.CategoryTransactions.Should().ContainSingle(t => t.Amount == 10m);
    }

    // --- Accounts ---

    private static Mock<IDialogService> ConfirmingDialog()
    {
        var dialog = new Mock<IDialogService>();
        dialog.Setup(d => d.ShowConfirmationAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
              .ReturnsAsync(true);
        return dialog;
    }

    [Fact]
    public async Task SaveAccount_TrimsName_ClearsResourceKey_AndPersists()
    {
        var seeded = new Account { Name = "Main", ResourceKey = "DefaultAccountName" };
        var financeMock = FinanceMock(new List<Account> { seeded });

        var viewModel = NewViewModel(financeMock);
        await viewModel.LoadAsync();

        viewModel.SelectAccountCommand.Execute(viewModel.Accounts.Single());
        viewModel.EditSelectedAccountCommand.Execute(null);
        viewModel.NewAccountName = "  Wallet  ";
        viewModel.NewAccountInitialBalanceText = "120,50";
        await viewModel.SaveAccountCommand.ExecuteAsync(null);

        financeMock.Verify(s => s.SaveAccountAsync(It.Is<Account>(a =>
            a.Name == "Wallet" && a.InitialBalance == 120.50m && a.ResourceKey == null)), Times.Once);
        viewModel.IsAccountFormVisible.Should().BeFalse();
    }

    [Fact]
    public async Task SaveAccount_WithBlankName_DoesNotPersist()
    {
        var financeMock = FinanceMock();
        var viewModel = NewViewModel(financeMock);

        viewModel.ShowAccountFormCommand.Execute(null);
        viewModel.NewAccountName = "   ";
        await viewModel.SaveAccountCommand.ExecuteAsync(null);

        financeMock.Verify(s => s.SaveAccountAsync(It.IsAny<Account>()), Times.Never);
    }

    [Fact]
    public async Task SelectAccount_FiltersTransactionsAndBalance_AndAllRestoresThem()
    {
        var cash = new Account { Name = "Cash", InitialBalance = 100m };
        var card = new Account { Name = "Card", InitialBalance = 50m };
        var financeMock = FinanceMock(new List<Account> { cash, card });
        financeMock.Setup(s => s.GetFinanceTransactionsAsync()).ReturnsAsync(new List<FinanceTransaction>
        {
            new() { Type = TransactionType.Expense, Amount = 30m, Date = DateTime.Today, AccountId = cash.Id },
            new() { Type = TransactionType.Income, Amount = 200m, Date = DateTime.Today, AccountId = card.Id }
        });

        var viewModel = NewViewModel(financeMock);
        await viewModel.LoadAsync();

        viewModel.TotalBalance.Should().Be(320m);      // 150 opening + 200 − 30

        viewModel.SelectAccountCommand.Execute(viewModel.Accounts.Single(a => a.Name == "Cash"));

        viewModel.IsAllAccountsSelected.Should().BeFalse();
        viewModel.Transactions.Should().ContainSingle(t => t.Amount == 30m);
        viewModel.TotalBalance.Should().Be(70m);       // 100 opening − 30
        viewModel.MonthIncome.Should().Be(0m);

        viewModel.SelectAllAccountsCommand.Execute(null);

        viewModel.IsAllAccountsSelected.Should().BeTrue();
        viewModel.Transactions.Should().HaveCount(2);
        viewModel.TotalBalance.Should().Be(320m);
    }

    [Fact]
    public async Task ArchivedAccount_IsHiddenFromStrip_ButStillCountsTowardsTheTotal()
    {
        var active = new Account { Name = "Cash", InitialBalance = 100m };
        var archived = new Account { Name = "Old", InitialBalance = 60m, IsArchived = true };
        var financeMock = FinanceMock(new List<Account> { active, archived });
        financeMock.Setup(s => s.GetFinanceTransactionsAsync()).ReturnsAsync(new List<FinanceTransaction>
        {
            new() { Type = TransactionType.Expense, Amount = 10m, Date = DateTime.Today, AccountId = archived.Id }
        });

        var viewModel = NewViewModel(financeMock);
        await viewModel.LoadAsync();

        viewModel.Accounts.Should().ContainSingle(a => a.Name == "Cash");
        viewModel.TotalBalance.Should().Be(150m);      // 160 opening − 10, archived included

        viewModel.ToggleShowArchivedAccountsCommand.Execute(null);

        viewModel.Accounts.Should().HaveCount(2);
        viewModel.Accounts.Should().ContainSingle(a => a.Name == "Old" && a.IsArchived);
    }

    [Fact]
    public async Task ToggleArchive_OnTheLastActiveAccount_IsRefused()
    {
        var only = new Account { Name = "Cash" };
        var financeMock = FinanceMock(new List<Account> { only });
        var dialog = new Mock<IDialogService>();

        var viewModel = NewViewModel(financeMock, dialog.Object);
        await viewModel.LoadAsync();

        viewModel.SelectAccountCommand.Execute(viewModel.Accounts.Single());
        viewModel.EditSelectedAccountCommand.Execute(null);
        await viewModel.ToggleArchiveAccountCommand.ExecuteAsync(null);

        only.IsArchived.Should().BeFalse();
        financeMock.Verify(s => s.SaveAccountAsync(It.IsAny<Account>()), Times.Never);
        dialog.Verify(d => d.ShowAlertAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public async Task DeleteAccount_WhenItIsTheOnlyOne_ShowsAlertAndKeepsIt()
    {
        var only = new Account { Name = "Cash" };
        var financeMock = FinanceMock(new List<Account> { only });
        var dialog = ConfirmingDialog();

        var viewModel = NewViewModel(financeMock, dialog.Object);
        await viewModel.LoadAsync();

        viewModel.SelectAccountCommand.Execute(viewModel.Accounts.Single());
        viewModel.EditSelectedAccountCommand.Execute(null);
        await viewModel.DeleteAccountCommand.ExecuteAsync(null);

        financeMock.Verify(s => s.DeleteAccountAsync(It.IsAny<Guid>(), It.IsAny<Guid>()), Times.Never);
        dialog.Verify(d => d.ShowAlertAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public async Task DeleteAccount_ReassignsToTheAccountTheUserPicked()
    {
        var doomed = new Account { Name = "Doomed" };
        var first = new Account { Name = "First" };
        var chosen = new Account { Name = "Chosen" };
        var financeMock = FinanceMock(new List<Account> { doomed, first, chosen });
        financeMock.Setup(s => s.GetFinanceTransactionsAsync()).ReturnsAsync(new List<FinanceTransaction>
        {
            new() { Type = TransactionType.Expense, Amount = 5m, Date = DateTime.Today, AccountId = doomed.Id }
        });

        var viewModel = NewViewModel(financeMock, ConfirmingDialog().Object);
        await viewModel.LoadAsync();

        viewModel.SelectAccountCommand.Execute(viewModel.Accounts.Single(a => a.Name == "Doomed"));
        viewModel.EditSelectedAccountCommand.Execute(null);

        viewModel.HasReassignTargets.Should().BeTrue();
        viewModel.SelectReassignTargetCommand.Execute(viewModel.ReassignTargets.Single(a => a.Name == "Chosen"));

        await viewModel.DeleteAccountCommand.ExecuteAsync(null);

        financeMock.Verify(s => s.DeleteAccountAsync(doomed.Id, chosen.Id), Times.Once);
    }

    [Fact]
    public void SelectAccountIcon_MovesTheSelectionFlag()
    {
        var viewModel = NewViewModel(FinanceMock());
        viewModel.ShowAccountFormCommand.Execute(null);

        viewModel.AvailableIcons[0].IsSelected.Should().BeTrue();

        viewModel.SelectAccountIconCommand.Execute(viewModel.AvailableIcons[3].Value);

        viewModel.NewAccountIcon.Should().Be(viewModel.AvailableIcons[3].Value);
        viewModel.AvailableIcons[3].IsSelected.Should().BeTrue();
        viewModel.AvailableIcons[0].IsSelected.Should().BeFalse();
    }

    // --- Transfers ---

    [Fact]
    public async Task SaveTransfer_BetweenTwoAccounts_PersistsAndShowsInTheList()
    {
        var from = new Account { Name = "Card", InitialBalance = 500m };
        var to = new Account { Name = "Cash" };
        var stored = new List<Transfer>();
        var financeMock = FinanceMock(new List<Account> { from, to }, stored);
        financeMock.Setup(s => s.SaveTransferAsync(It.IsAny<Transfer>()))
                   .Returns<Transfer>(t => { stored.Add(t); return Task.CompletedTask; });

        var viewModel = NewViewModel(financeMock);
        await viewModel.LoadAsync();

        viewModel.ShowTransferFormCommand.Execute(null);
        viewModel.SelectTransferFromCommand.Execute(viewModel.TransferFromOptions.Single(a => a.Name == "Card"));
        viewModel.SelectTransferToCommand.Execute(viewModel.TransferToOptions.Single(a => a.Name == "Cash"));
        viewModel.NewTransferAmountText = "150";
        await viewModel.SaveTransferCommand.ExecuteAsync(null);

        stored.Should().ContainSingle();
        stored[0].Amount.Should().Be(150m);
        stored[0].FromAccountId.Should().Be(from.Id);
        stored[0].ToAccountId.Should().Be(to.Id);

        viewModel.IsTransferFormVisible.Should().BeFalse();
        viewModel.HasTransfers.Should().BeTrue();
        viewModel.TransferItems.Single().FromName.Should().Be("Card");

        // Money only moved between accounts, so the aggregate balance is untouched.
        viewModel.TotalBalance.Should().Be(500m);
    }

    [Fact]
    public async Task SaveTransfer_WithTheSameAccountOnBothSides_IsRejected()
    {
        var only = new Account { Name = "Cash" };
        var financeMock = FinanceMock(new List<Account> { only });

        var viewModel = NewViewModel(financeMock);
        await viewModel.LoadAsync();

        viewModel.ShowTransferFormCommand.Execute(null);
        viewModel.NewTransferAmountText = "50";
        await viewModel.SaveTransferCommand.ExecuteAsync(null);

        financeMock.Verify(s => s.SaveTransferAsync(It.IsAny<Transfer>()), Times.Never);
        viewModel.HasTransferError.Should().BeTrue();
        viewModel.IsTransferFormVisible.Should().BeTrue();
    }

    [Fact]
    public async Task SaveTransfer_WithANonPositiveAmount_IsRejected()
    {
        var from = new Account { Name = "Card" };
        var to = new Account { Name = "Cash" };
        var financeMock = FinanceMock(new List<Account> { from, to });

        var viewModel = NewViewModel(financeMock);
        await viewModel.LoadAsync();

        viewModel.ShowTransferFormCommand.Execute(null);
        viewModel.NewTransferAmountText = "0";
        await viewModel.SaveTransferCommand.ExecuteAsync(null);

        financeMock.Verify(s => s.SaveTransferAsync(It.IsAny<Transfer>()), Times.Never);
        viewModel.HasTransferError.Should().BeTrue();
    }
}
