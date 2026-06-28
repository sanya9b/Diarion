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
    [Fact]
    public async Task LoadAsync_WithSavedTransactions_CalculatesBalancesCorrectly()
    {
        // Arrange
        var currentMonth = DateTime.Today.Month;
        var currentYear = DateTime.Today.Year;
        
        var diaryServiceMock = new Mock<IFinanceService>();
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

        var viewModel = new FinanceViewModel(diaryServiceMock.Object);

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
        var diaryServiceMock = new Mock<IFinanceService>();
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

        var viewModel = new FinanceViewModel(diaryServiceMock.Object);
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
        var viewModel = new FinanceViewModel(diaryServiceMock.Object);
        
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
        var viewModel = new FinanceViewModel(diaryServiceMock.Object);
        
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
            
        var viewModel = new FinanceViewModel(financeServiceMock.Object);
        
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
            
        var viewModel = new FinanceViewModel(financeServiceMock.Object);
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
            
        var viewModel = new FinanceViewModel(financeServiceMock.Object);
        await viewModel.ToggleAddTransactionCommand.ExecuteAsync(null);
        
        // Ensure suggestions are initially populated
        viewModel.SuggestedCategories.Should().NotBeEmpty();

        // Act
        viewModel.SelectCategoryCommand.Execute("Groceries");

        // Assert
        viewModel.NewCategory.Should().Be("Groceries");
        viewModel.SuggestedCategories.Should().BeEmpty();
    }
}
