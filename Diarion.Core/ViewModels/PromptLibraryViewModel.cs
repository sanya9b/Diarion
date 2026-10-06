using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Diarion.Models;
using Diarion.Resources.Localization;
using Diarion.Services;

namespace Diarion.ViewModels;

public partial class PromptItemViewModel : ObservableObject
{
    public Guid Id { get; init; }
    public string Text { get; init; } = string.Empty;

    /// <summary>Seeded prompts are labelled so the user can tell their own additions apart.</summary>
    public bool IsBuiltIn { get; init; }
}

public partial class PromptCategoryGroupViewModel : ObservableObject
{
    public PromptCategory Category { get; init; }
    public string Name { get; init; } = string.Empty;
    public ObservableCollection<PromptItemViewModel> Prompts { get; } = new();

    public bool IsEmpty => Prompts.Count == 0;
}

public partial class PromptLibraryViewModel : BaseViewModel
{
    private readonly IGuidedPromptService _promptService;
    private readonly INavigationService _navigationService;

    public ObservableCollection<PromptCategoryGroupViewModel> Groups { get; } = new();

    public PromptLibraryViewModel(IGuidedPromptService promptService, INavigationService navigationService)
    {
        _promptService = promptService;
        _navigationService = navigationService;
        Title = AppResources.PromptLibraryTitle;
    }

    public static string CategoryName(PromptCategory category) => category switch
    {
        PromptCategory.CbtReframe => AppResources.PromptCategoryCbtReframe,
        PromptCategory.Savouring => AppResources.PromptCategorySavouring,
        PromptCategory.OpenReflection => AppResources.PromptCategoryOpenReflection,
        PromptCategory.EveningGratitude => AppResources.PromptCategoryEveningGratitude,
        _ => category.ToString()
    };

    public async Task LoadAsync()
    {
        var library = await _promptService.GetLibraryAsync();
        var live = library.Ordered;

        Groups.Clear();
        foreach (var category in PromptCatalog.SeedKeys.Keys)
        {
            var group = new PromptCategoryGroupViewModel
            {
                Category = category,
                Name = CategoryName(category)
            };

            foreach (var prompt in live.Where(p => p.Category == category))
            {
                group.Prompts.Add(new PromptItemViewModel
                {
                    Id = prompt.Id,
                    Text = PromptLocalization.ResolveText(prompt),
                    IsBuiltIn = !string.IsNullOrEmpty(prompt.ResourceKey)
                });
            }

            Groups.Add(group);
        }
    }

    [RelayCommand]
    private async Task AddPromptAsync() => await _navigationService.NavigateToAsync("PromptEditor");

    [RelayCommand]
    private async Task EditPromptAsync(PromptItemViewModel? item)
    {
        if (item == null) return;

        await _navigationService.NavigateToAsync("PromptEditor", new Dictionary<string, object>
        {
            { "PromptId", item.Id.ToString() }
        });
    }

    [RelayCommand]
    private async Task CloseAsync() => await _navigationService.NavigateBackAsync();
}
