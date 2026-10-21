using System.Linq;
using System.Threading.Tasks;
using Diarion.Models;
using Diarion.ViewModels;
using Microsoft.Maui.Controls;

namespace Diarion.Views;

public partial class StatisticsPage : ContentPage
{
    private readonly StatisticsViewModel _viewModel;

    public StatisticsPage(StatisticsViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        BindingContext = viewModel;

        TourOverlay.AnchorResolver = AnchorFor;
        TourOverlay.PrepareStepAsync = PrepareTourStepAsync;
        TourOverlay.ScrollHost = PageScroll;
        TourOverlay.Tour = viewModel.Tour;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        // Before loading: the cycle tab appears and disappears with the setting, and selecting a tab
        // that has just been removed would leave the page showing nothing.
        await _viewModel.RefreshCycleTabAvailabilityAsync();
        await _viewModel.LoadStatisticsAsync();

        // After loading, not before: two of the four steps point at cards that only exist once there
        // is something to put in them.
        await _viewModel.Tour.StartAsync(TourId.Statistics);
    }

    // --- Statistics guide (onboarding stage 3) ----------------------------------------------------
    // TourOverlayView does the drawing. This half answers what only the page can: which element an
    // id means, and what has to be on screen before it can be measured.

    private Task PrepareTourStepAsync(TourStep step)
    {
        if (step.Id is TourStepId.StatsInfluences)
        {
            // The card lives on the general tab. On a first visit that is already the open one, so
            // this is normally a no-op — it matters only when the guide is replayed from settings.
            var general = _viewModel.Tabs.FirstOrDefault(tab => tab.Option == StatisticsTabOption.General);
            if (general is not null && !general.IsSelected)
            {
                _viewModel.SelectTab(general);
            }
        }

        return Task.CompletedTask;
    }

    /// <remarks>
    /// A null here is expected rather than exceptional: the influence card appears only
    /// once enough has been written, and on the first visit that is never. The card still says what
    /// will show up there; the overlay simply draws no hole.
    /// </remarks>
    private VisualElement? AnchorFor(TourStepId id) => id switch
    {
        TourStepId.StatsPeriod => PeriodAnchor,
        TourStepId.StatsTabs => TabsAnchor,
        // Exactly one of these two is on screen: the progress card stands in for the influences card
        // until there are influences to show.
        TourStepId.StatsInfluences => Visible(InfluencesAnchor) ?? Visible(InsightProgressAnchor),
        _ => null
    };

    private static VisualElement? Visible(VisualElement element) => element.IsVisible ? element : null;
}
