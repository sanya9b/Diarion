using Android.App;
using Android.Appwidget;
using Android.Content;
using Android.Views;
using Android.Widget;
using Diarion.Models.Widgets;
using Diarion.Services.Widgets;

namespace Diarion.Widgets;

/// <summary>W1 "Today": the date, the mood dot, the streak as the big number, and the two counts under it.</summary>
[BroadcastReceiver(Name = "org.diarion.app.widgets.TodayWidgetProvider", Label = "@string/widget_today_label", Exported = true)]
[IntentFilter(new[] { AppWidgetManager.ActionAppwidgetUpdate })]
[MetaData(AppWidgetManager.MetaDataAppwidgetProvider, Resource = "@xml/widget_today_info")]
public sealed class TodayWidgetProvider : DiarionWidgetProvider
{
    protected override int LayoutId => Resource.Layout.widget_today;

    protected override WidgetDestination Destination => WidgetDestination.Today;

    protected override void Render(Context context, RemoteViews views, WidgetSnapshot snapshot, WidgetPalette palette)
    {
        SetDot(views, Resource.Id.widget_mood, palette.Mood(snapshot.Mood?.Quadrant), palette);
        views.SetContentDescription(Resource.Id.widget_mood, snapshot.Mood?.Label ?? string.Empty);

        views.SetTextColor(Resource.Id.widget_headline, palette.Text);
        views.SetTextColor(Resource.Id.widget_subline, palette.Secondary);
        views.SetTextColor(Resource.Id.widget_tasks_label, palette.Text);
        views.SetTextColor(Resource.Id.widget_habits_label, palette.Text);

        var streak = snapshot.Streak;
        if (streak is { Days: > 0 })
        {
            views.SetTextViewText(Resource.Id.widget_headline, streak.Days.ToString());
            views.SetTextViewText(Resource.Id.widget_subline, streak.Label);
            views.SetViewVisibility(Resource.Id.widget_headline, ViewStates.Visible);
        }
        else
        {
            // No run to count: the nudge takes the headline's place rather than a lonely zero.
            views.SetViewVisibility(Resource.Id.widget_headline, ViewStates.Gone);
            views.SetTextViewText(Resource.Id.widget_subline, streak?.Label ?? string.Empty);
        }

        views.SetTextViewText(Resource.Id.widget_tasks_label, snapshot.Tasks?.Label ?? string.Empty);
        views.SetTextViewText(Resource.Id.widget_habits_label, snapshot.Habits?.Label ?? string.Empty);
    }
}
