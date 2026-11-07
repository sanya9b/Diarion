using Android.App;
using Android.Appwidget;
using Android.Content;
using Android.Views;
using Android.Widget;
using Diarion.Models.Widgets;
using Diarion.Services.Widgets;

namespace Diarion.Widgets;

/// <summary>
/// W2 "Tasks of the day": the count and a progress bar at every level; at the with-text level also up
/// to five rows — open ones first, a priority dot in front, the time at the end, done ones greyed.
/// Five fixed rows rather than a ListView: a list needs a RemoteViewsService and a second component,
/// and the widget shows what is left to do, not the whole planner.
/// </summary>
[BroadcastReceiver(Name = "org.diarion.app.widgets.TasksWidgetProvider", Label = "@string/widget_tasks_label", Exported = true)]
[IntentFilter(new[] { AppWidgetManager.ActionAppwidgetUpdate })]
[MetaData(AppWidgetManager.MetaDataAppwidgetProvider, Resource = "@xml/widget_tasks_info")]
public sealed class TasksWidgetProvider : DiarionWidgetProvider
{
    private static readonly int[] Rows = { Resource.Id.widget_row_1, Resource.Id.widget_row_2, Resource.Id.widget_row_3, Resource.Id.widget_row_4, Resource.Id.widget_row_5 };
    private static readonly int[] Dots = { Resource.Id.widget_row_dot_1, Resource.Id.widget_row_dot_2, Resource.Id.widget_row_dot_3, Resource.Id.widget_row_dot_4, Resource.Id.widget_row_dot_5 };
    private static readonly int[] Titles = { Resource.Id.widget_row_title_1, Resource.Id.widget_row_title_2, Resource.Id.widget_row_title_3, Resource.Id.widget_row_title_4, Resource.Id.widget_row_title_5 };
    private static readonly int[] Times = { Resource.Id.widget_row_time_1, Resource.Id.widget_row_time_2, Resource.Id.widget_row_time_3, Resource.Id.widget_row_time_4, Resource.Id.widget_row_time_5 };

    protected override int LayoutId => Resource.Layout.widget_tasks;

    protected override WidgetDestination Destination => WidgetDestination.Tasks;

    protected override void Render(Context context, RemoteViews views, WidgetSnapshot snapshot, WidgetPalette palette)
    {
        var tasks = snapshot.Tasks;
        views.SetTextViewText(Resource.Id.widget_title, tasks?.Label ?? string.Empty);
        views.SetTextColor(Resource.Id.widget_title, palette.Text);
        views.SetTextColor(Resource.Id.widget_more, palette.Secondary);
        SetProgress(views, Resource.Id.widget_progress, tasks?.Done ?? 0, tasks?.Total ?? 0, palette);

        var items = tasks?.Items;
        for (var i = 0; i < Rows.Length; i++)
        {
            if (items is null || i >= items.Count)
            {
                views.SetViewVisibility(Rows[i], ViewStates.Gone);
                continue;
            }

            var item = items[i];
            views.SetViewVisibility(Rows[i], ViewStates.Visible);
            SetDot(views, Dots[i], item.Done ? palette.Sage : palette.Priority(item.Priority), palette);
            views.SetTextViewText(Titles[i], item.Title);
            views.SetTextColor(Titles[i], item.Done ? palette.Secondary : palette.Text);
            views.SetTextViewText(Times[i], item.Time ?? string.Empty);
            views.SetTextColor(Times[i], palette.Secondary);
            views.SetViewVisibility(Times[i], item.Time is null ? ViewStates.Gone : ViewStates.Visible);
        }

        var hidden = items is null ? 0 : (tasks!.Total - System.Math.Min(items.Count, Rows.Length));
        views.SetViewVisibility(Resource.Id.widget_more, hidden > 0 ? ViewStates.Visible : ViewStates.Gone);
        views.SetTextViewText(Resource.Id.widget_more, hidden > 0 ? $"+{hidden}" : string.Empty);
    }
}
