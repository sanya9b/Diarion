---
description: "Use when creating or updating unit tests for the Diarion project. Ensures proper testing of LiteDB services and ViewModels."
applyTo: ["**/*Tests.cs", "**/Tests/**/*.cs"]
---

# Diarion Testing Guidelines

When writing tests for the Diarion project, strictly adhere to the following rules to ensure reliability and maintainability.

## Frameworks
- Use **xUnit** as the primary testing framework.
- Use **Moq** for mocking dependencies.
- Use **FluentAssertions** for readable assertions.

## Testing LiteDB Services (Infrastructure)
1. **In-Memory Database**: ALWAYS use LiteDB's in-memory mode (`new LiteDatabase(new MemoryStream())`) for unit testing services to avoid file system locks and side effects.
2. **Isolation**: Each test MUST use a fresh instance of the database to ensure test isolation.
3. **Repository Pattern**: Test the CRUD operations thoroughly (Create, Read, Update, Delete). Verify that the correct items are returned and that exceptions are thrown for invalid IDs.
4. **Disposal**: Ensure the `LiteDatabase` instance is properly disposed of after each test (implement `IDisposable` or use `using` statements).

## Testing ViewModels (Logic)
1. **Mock Services**: Never use the real LiteDB service in ViewModel tests. Always mock `IDiaryService` (and any other services) using Moq.
2. **State Verification**: Verify that the ViewModel properties (like `IsBusy`, `Entries`) change to the expected states before, during, and after commands execute.
3. **Command Execution**: Test that `[RelayCommand]` methods behave correctly under both success and failure scenarios (e.g., catching exceptions and logging/displaying errors).
4. **Navigation**: If the ViewModel performs navigation, inject a mock navigation service or verify the expected navigation parameters/routes.

## Naming Conventions
- Test Classes: `[ClassName]Tests.cs` (e.g., `DiaryServiceTests.cs`, `MainViewModelTests.cs`).
- Test Methods: `[MethodName]_[Scenario]_[ExpectedBehavior]` (e.g., `GetAllEntriesAsync_WithExistingEntries_ReturnsAllEntries`, `LoadEntriesAsync_OnSuccess_PopulatesEntriesCollection`).

## Example ViewModel Test Setup
```csharp
[Fact]
public async Task LoadEntriesAsync_OnSuccess_PopulatesEntriesCollection()
{
    // Arrange
    var mockService = new Mock<IDiaryService>();
    var expectedEntries = new List<DiaryEntry> { new DiaryEntry { Title = "Test" } };
    mockService.Setup(s => s.GetAllEntriesAsync()).ReturnsAsync(expectedEntries);
    var viewModel = new MainViewModel(mockService.Object);

    // Act
    await viewModel.LoadEntriesAsync();

    // Assert
    viewModel.Entries.Should().HaveCount(1);
    viewModel.Entries.First().Title.Should().Be("Test");
    viewModel.IsBusy.Should().BeFalse();
}
```
