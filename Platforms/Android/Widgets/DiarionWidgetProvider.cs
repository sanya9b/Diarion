using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using Android.App;
using Android.Appwidget;
using Android.Content;
using Android.Views;
using Android.Widget;
using AndroidX.Core.Content;
using Diarion.Models.Widgets;
using Diarion.Services.Widgets;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui;

namespace Diarion.Widgets;

/// <summary>
/// What the three Android widgets share (spec 20, R7): reading the snapshot file, the tap that opens
/// the app on a <c>diarion://</c> link, the button-only overlay, and the mood and priority dots.
/// <para>
/// The provider runs inside the app's own process — Android creates <c>MainApplication</c>, and with
/// it the service container, before any receiver — but it still draws from the file and never from the
/// database: the file is instant, needs no key, and is the same contract the iOS widget reads. The one
/// thing it asks the app for is a fresh snapshot when the file is from another day (see
/// <see cref="WidgetSnapshotStore.RefreshIfStale"/>).
/// </para>
/// </summary>
public abstract class DiarionWidgetProvider : AppWidgetProvider
{
    protected abstract int LayoutId { get; }

    protected abstract WidgetDestination Destination { get; }

    public override void OnUpdate(Context? context, AppWidgetManager? appWidgetManager, int[]? appWidgetIds)
    {
        if (context is null || appWidgetManager is null || appWidgetIds is null) return;

        var snapshot = WidgetSnapshotStore.Read(context);
        foreach (var id in appWidgetIds)
        {
            try
            {
                appWidgetManager.UpdateAppWidget(id, Build(context, snapshot));
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Widget {id} not drawn: {ex}");
            }
        }

        WidgetSnapshotStore.RefreshIfStale(snapshot);
    }

    private RemoteViews Build(Context context, WidgetSnapshot? snapshot)
    {
        var views = new RemoteViews(context.PackageName, LayoutId);
        views.SetOnClickPendingIntent(Resource.Id.widget_root, TapIntent(context, Destination));

        var showsData = snapshot is not null && snapshot.PrivacyLevel != WidgetPrivacyLevel.ButtonOnly;
        views.SetViewVisibility(Resource.Id.widget_content, showsData ? ViewStates.Visible : ViewStates.Gone);
        views.SetViewVisibility(Resource.Id.widget_button_only, showsData ? ViewStates.Gone : ViewStates.Visible);
        views.SetTextViewText(Resource.Id.widget_open_label,
            snapshot?.Labels.Open ?? context.GetString(Resource.String.widget_open_fallback));

        if (showsData)
        {
            views.SetTextViewText(Resource.Id.widget_date, snapshot!.Labels.DateShort);
            views.SetTextViewText(Resource.Id.widget_updated, snapshot.Labels.Updated);
            Render(context, views, snapshot);
        }

        return views;
    }

    /// <summary>Fills the data half. Called only with a snapshot above the button-only level.</summary>
    protected abstract void Render(Context context, RemoteViews views, WidgetSnapshot snapshot);

    /// <summary>
    /// Opens <c>MainActivity</c> with the link as both data and extra. Data makes the three intents
    /// distinct to the system, so one widget's PendingIntent is never recycled for another's; the extra
    /// is what the activity reads. SingleTop plus ClearTop lands in OnNewIntent when the app is alive.
    /// </summary>
    protected static PendingIntent TapIntent(Context context, WidgetDestination destination)
    {
        var link = WidgetDeepLink.For(destination);
        var intent = new Intent(context, typeof(MainActivity));
        intent.SetData(global::Android.Net.Uri.Parse(link));
        intent.PutExtra(MainActivity.DeepLinkExtra, link);
        intent.SetFlags(ActivityFlags.NewTask | ActivityFlags.SingleTop | ActivityFlags.ClearTop);
        return PendingIntent.GetActivity(
            context, (int)destination, intent, PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable)!;
    }

    /// <summary>The quadrant's brand hue as a filled dot, or a ring when nothing is recorded (AGENTS.md palette).</summary>
    protected static int MoodDrawable(string? quadrant) => quadrant switch
    {
        nameof(Models.EmotionQuadrant.PleasantEnergetic) => Resource.Drawable.widget_dot_coral,
        nameof(Models.EmotionQuadrant.PleasantCalm) => Resource.Drawable.widget_dot_sage,
        nameof(Models.EmotionQuadrant.UnpleasantEnergetic) => Resource.Drawable.widget_dot_amber,
        nameof(Models.EmotionQuadrant.UnpleasantCalm) => Resource.Drawable.widget_dot_ocean,
        nameof(Models.EmotionQuadrant.Neutral) => Resource.Drawable.widget_dot_dust,
        _ => Resource.Drawable.widget_ring
    };

    /// <summary>Coral / Amber / Sage for high / medium / low, as the planner colours them.</summary>
    protected static int PriorityDrawable(string priority) => priority switch
    {
        nameof(Models.TodoPriority.High) => Resource.Drawable.widget_dot_coral,
        nameof(Models.TodoPriority.Low) => Resource.Drawable.widget_dot_sage,
        _ => Resource.Drawable.widget_dot_amber
    };

    protected static global::Android.Graphics.Color TextColor(Context context, bool secondary)
        => new(ContextCompat.GetColor(context, secondary ? Resource.Color.widget_text_secondary : Resource.Color.widget_text));
}

/// <summary>The snapshot file on Android, and the two things the providers may ask the app for.</summary>
public static class WidgetSnapshotStore
{
    public static string PathFor(Context context)
        => Path.Combine(context.FilesDir!.AbsolutePath, WidgetSnapshotJson.FileName);

    public static WidgetSnapshot? Read(Context context)
    {
        try
        {
            var path = PathFor(context);
            return File.Exists(path) ? WidgetSnapshotJson.Deserialize(File.ReadAllText(path)) : null;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Widget snapshot not read: {ex}");
            return null;
        }
    }

    /// <summary>
    /// The file is from another day, or there is none yet: ask the coordinator for a new one. The
    /// answer comes back through <see cref="RequestUpdate"/>, so the widget rolls over to the new day
    /// on its next periodic update without the app being opened. Nothing happens if the container is
    /// not there, which only a process in the middle of dying can manage.
    /// </summary>
    public static void RefreshIfStale(WidgetSnapshot? snapshot)
    {
        var today = DateTime.Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        if (snapshot is not null && snapshot.Date == today) return;

        try
        {
            IPlatformApplication.Current?.Services.GetService<WidgetSnapshotCoordinator>()?.RequestRefresh();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Widget refresh not requested: {ex}");
        }
    }

    /// <summary>Tells every placed instance of every Diarion widget to redraw from the file.</summary>
    public static void RequestUpdate(Context context)
    {
        var manager = AppWidgetManager.GetInstance(context);
        if (manager is null) return;

        foreach (var provider in new[] { typeof(TodayWidgetProvider), typeof(TasksWidgetProvider), typeof(HabitsWidgetProvider) })
        {
            var component = new ComponentName(context, Java.Lang.Class.FromType(provider));
            var ids = manager.GetAppWidgetIds(component);
            if (ids is null || ids.Length == 0) continue;

            var intent = new Intent(context, provider);
            intent.SetAction(AppWidgetManager.ActionAppwidgetUpdate);
            intent.PutExtra(AppWidgetManager.ExtraAppwidgetIds, ids);
            context.SendBroadcast(intent);
        }
    }
}
