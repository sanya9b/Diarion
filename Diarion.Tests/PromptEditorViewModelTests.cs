using System;
using System.Threading.Tasks;
using Diarion.Models;
using Diarion.Resources.Localization;
using Diarion.Services;
using Diarion.ViewModels;
using FluentAssertions;
using Moq;
using Xunit;

namespace Diarion.Tests;

public class PromptEditorViewModelTests
{
    private readonly Mock<IGuidedPromptService> _promptService = new();
    private readonly Mock<INavigationService> _navigation = new();
    private readonly Mock<IDialogService> _dialogs = new();

    private PromptEditorViewModel CreateVm() =>
        new(_promptService.Object, _navigation.Object, _dialogs.Object);

    [Fact]
    public async Task Save_WithBothLanguagesBlank_IsRejected()
    {
        var vm = CreateVm();

        await vm.SaveCommand.ExecuteAsync(null);

        vm.ValidationMessage.Should().Be(AppResources.PromptEditorTextRequired);
        vm.HasValidationMessage.Should().BeTrue();
        _promptService.Verify(s => s.AddAsync(It.IsAny<GuidedPrompt>()), Times.Never);
    }

    [Fact]
    public async Task Save_WithOnlyOneLanguage_IsAccepted()
    {
        var vm = CreateVm();
        vm.TextUk = "Що сьогодні вдалося?";

        await vm.SaveCommand.ExecuteAsync(null);

        _promptService.Verify(s => s.AddAsync(It.Is<GuidedPrompt>(p => p.TextUk == "Що сьогодні вдалося?")), Times.Once);
        _navigation.Verify(n => n.NavigateBackAsync(), Times.Once);
    }

    [Fact]
    public async Task Save_OverlyLongText_IsRejected()
    {
        var vm = CreateVm();
        vm.TextEn = new string('x', PromptEditorViewModel.MaxTextLength + 1);

        await vm.SaveCommand.ExecuteAsync(null);

        vm.ValidationMessage.Should().Be(AppResources.PromptEditorTextTooLong);
        _promptService.Verify(s => s.AddAsync(It.IsAny<GuidedPrompt>()), Times.Never);
    }

    [Fact]
    public async Task EditingABuiltIn_KeepsItsResourceKey()
    {
        var builtIn = new GuidedPrompt
        {
            ResourceKey = "PromptCbt03",
            Category = PromptCategory.CbtReframe,
            TextEn = "original",
            TextUk = "оригінал"
        };
        _promptService.Setup(s => s.GetByIdAsync(builtIn.Id)).ReturnsAsync(builtIn);

        var vm = CreateVm();
        vm.PromptId = builtIn.Id.ToString();
        await Task.Delay(20); // the query property kicks off the load

        vm.TextEn = "reworded";
        await vm.SaveCommand.ExecuteAsync(null);

        // Clearing it — as the habit editor does — would orphan every diary entry that referenced
        // this built-in by its resource key.
        _promptService.Verify(s => s.UpdateAsync(It.Is<GuidedPrompt>(p =>
            p.ResourceKey == "PromptCbt03" && p.TextEn == "reworded")), Times.Once);
    }

    [Fact]
    public async Task Delete_WithoutConfirmation_DoesNothing()
    {
        var prompt = new GuidedPrompt { TextEn = "mine" };
        _promptService.Setup(s => s.GetByIdAsync(prompt.Id)).ReturnsAsync(prompt);
        _dialogs.Setup(d => d.ShowConfirmationAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync(false);

        var vm = CreateVm();
        vm.PromptId = prompt.Id.ToString();
        await Task.Delay(20);

        await vm.DeleteCommand.ExecuteAsync(null);

        _promptService.Verify(s => s.DeleteAsync(It.IsAny<Guid>(), It.IsAny<DateTime>()), Times.Never);
    }
}
