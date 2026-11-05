using Android.App;
using Android.Appwidget;
using Android.Content;
using Android.Views;
using Android.Widget;
using Diarion.Models.Widgets;
using Diarion.Services.Widgets;

namespace Diarion.Widgets;

/// <summary>
/// W3 "Habits": the count, then either a row of dots — one per habit scheduled today, filled when
/// done — or, at the with-text level, up to six rows with a mark and the habit's name.
/// </summary>
[BroadcastReceiver(Name = "org.diarion.app.widgets.HabitsWidgetProvider", Label = "@string/widget_habits_label", Exported = true)]
[IntentFilter(new[] { AppWidgetManager.ActionAppwidgetUpdate })]
[MetaData(AppWidgetManager.MetaDataAppwidgetProvider, Resource = "@xml/widget_habits_info")]
public sealed class HabitsWidgetProvider : DiarionWidgetProvider
{
    private static readonly int[] DotIds =
    {
        Resource.Id.widget_dot_1, Resource.Id.widget_dot_2, Resource.Id.widget_dot_3, Resource.Id.widget_dot_4, Resource.Id.widget_dot_5,
        Resource.Id.widget_dot_6, Resource.Id.widget_dot_7, Resource.Id.widget_dot_8, Resource.Id.widget_dot_9, Resource.Id.widget_dot_10
    };

    private static readonly int[] Rows = { Resource.Id.widget_row_1, Resource.Id.widget_row_2, Resource.Id.widget_row_3, Resource.Id.widget_row_4, Resource.Id.widget_row_5, Resource.Id.widget_row_6 };
    private static readonly int[] Marks = { Resource.Id.widget_row_dot_1, Resource.Id.widget_row_dot_2, Resource.Id.widget_row_dot_3, Resource.Id.widget_row_dot_4, Resource.Id.widget_row_dot_5, Resource.Id.widget_row_dot_6 };
    private static readonly int[] Titles = { Resource.Id.widget_row_title_1, Resource.Id.widget_row_title_2, Resource.Id.widget_row_title_3, Resource.Id.widget_row_title_4, Resource.Id.widget_row_title_5, Resource.Id.widget_row_title_6 };

    protected override int LayoutId => Resource.Layout.widget_habits;

    protected override WidgetDestination Destination => WidgetDestination.Habits;

    protected override void Render(Context context, RemoteViews views, WidgetSnapshot snapshot)
    {
        var habits = snapshot.Habits;
        views.SetTextViewText(Resource.Id.widget_title, habits?.Label ?? string.Empty);

        var items = habits?.Items;
        var withText = items is not null;
        views.SetViewVisibility(Resource.Id.widget_dots, withText ? ViewStates.Gone : ViewStates.Visible);
        views.SetViewVisibility(Resource.Id.widget_rows, withText ? ViewStates.Visible : ViewStates.Gone);

        if (!withText)
        {
            // Dots in the habits' own order are not known at this level, only how many are done:
            // the done ones are drawn first, which reads as a bar filling from the left.
            var total = habits?.Total ?? 0;
            var done = habits?.Done ?? 0;
            for (var i = 0; i < DotIds.Length; i++)
            {
                views.SetViewVisibility(DotIds[i], i < total ? ViewStates.Visible : ViewStates.Gone);
                views.SetImageViewResource(DotIds[i], i < done ? Resource.Drawable.widget_dot_done : Resource.Drawable.widget_ring);
            }
            return;
        }

        var list = items!;
        for (var i = 0; i < Rows.Length; i++)
        {
            if (i >= list.Count)
            {
                views.SetViewVisibility(Rows[i], ViewStates.Gone);
                continue;
            }

            views.SetViewVisibility(Rows[i], ViewStates.Visible);
            views.SetImageViewResource(Marks[i], list[i].Done ? Resource.Drawable.widget_dot_done : Resource.Drawable.widget_ring);
            views.SetTextViewText(Titles[i], list[i].Name);
            views.SetTextColor(Titles[i], TextColor(context, secondary: list[i].Done));
        }

        var hidden = habits!.Total - System.Math.Min(list.Count, Rows.Length);
        views.SetViewVisibility(Resource.Id.widget_more, hidden > 0 ? ViewStates.Visible : ViewStates.Gone);
        views.SetTextViewText(Resource.Id.widget_more, hidden > 0 ? $"+{hidden}" : string.Empty);
    }
}
