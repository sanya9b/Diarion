using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using Android.App;
using Android.Appwidget;
using Android.Content;
using Android.Content.Res;
using Color = Android.Graphics.Color;
using Android.Views;
using Android.Widget;
using Diarion.Models.Widgets;
using Diarion.Services.Widgets;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui;

namespace Diarion.Widgets;

/// <summary>
/// What the three Android widgets share (spec 20, R7): reading the snapshot file, the tap that opens
/// the app on a <c>diarion://</c> link, the button-only overlay, and the theme (D7): every colour on
/// the widget comes from the snapshot's theme section — the app's own Light, Dark or Pink — and only
/// the gallery preview, which cannot run code, uses the fixed resources in the layout.
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

        var palette = WidgetPalette.For(snapshot?.Theme);
        views.SetInt(Resource.Id.widget_root, "setBackgroundResource", palette.BackgroundDrawable);
        views.SetTextColor(Resource.Id.widget_open_label, palette.Text);
        views.SetTextColor(Resource.Id.widget_date, palette.Secondary);
        views.SetTextColor(Resource.Id.widget_updated, palette.Secondary);

        var showsData = snapshot is not null && snapshot.PrivacyLevel != WidgetPrivacyLevel.ButtonOnly;
        views.SetViewVisibility(Resource.Id.widget_content, showsData ? ViewStates.Visible : ViewStates.Gone);
        views.SetViewVisibility(Resource.Id.widget_button_only, showsData ? ViewStates.Gone : ViewStates.Visible);
        views.SetTextViewText(Resource.Id.widget_open_label,
            snapshot?.Labels.Open ?? context.GetString(Resource.String.widget_open_fallback));

        if (showsData)
        {
            views.SetTextViewText(Resource.Id.widget_date, snapshot!.Labels.DateShort);
            views.SetTextViewText(Resource.Id.widget_updated, snapshot.Labels.Updated);
            Render(context, views, snapshot, palette);
        }

        return views;
    }

    /// <summary>Fills the data half. Called only with a snapshot above the button-only level.</summary>
    protected abstract void Render(Context context, RemoteViews views, WidgetSnapshot snapshot, WidgetPalette palette);

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

    /// <summary>A filled dot in <paramref name="fill"/>, or the ring in the secondary colour when null.</summary>
    protected static void SetDot(RemoteViews views, int viewId, Color? fill, WidgetPalette palette)
    {
        views.SetImageViewResource(viewId, fill is null ? Resource.Drawable.widget_ring_tint : Resource.Drawable.widget_dot);
        views.SetInt(viewId, "setColorFilter", (fill ?? palette.Secondary).ToArgb());
    }

    /// <summary>Progress tint follows the theme where RemoteViews allows it (API 31); older systems keep the drawable's Sage.</summary>
    protected static void SetProgress(RemoteViews views, int viewId, int done, int total, WidgetPalette palette)
    {
        views.SetProgressBar(viewId, total > 0 ? total : 1, done, false);
        if (OperatingSystem.IsAndroidVersionAtLeast(31))
        {
            views.SetColorStateList(viewId, "setProgressTintList", ColorStateList.ValueOf(palette.Sage));
            views.SetColorStateList(viewId, "setProgressBackgroundTintList", ColorStateList.ValueOf(palette.Track));
        }
    }
}

/// <summary>The snapshot's theme as Android colours, with the brand light palette when a file carries none.</summary>
public readonly record struct WidgetPalette(
    int BackgroundDrawable, Color Text, Color Secondary, Color Track, Color Accent, Color Sage, Color Amber, Color Ocean)
{
    public static WidgetPalette For(WidgetTheme? theme)
    {
        if (theme is null)
        {
            return new WidgetPalette(Resource.Drawable.widget_background, Parse("#22282C", Color.Black), Parse("#6B7680", Color.Gray),
                Parse("#E0E6EA", Color.LightGray), Parse("#C26D53", Color.Red), Parse("#8FA083", Color.Green),
                Parse("#C9985A", Color.Yellow), Parse("#929FA7", Color.Gray));
        }

        var background = theme.Name switch
        {
            "Dark" => Resource.Drawable.widget_background_dark,
            "Pink" => Resource.Drawable.widget_background_pink,
            _ => Resource.Drawable.widget_background
        };
        return new WidgetPalette(background, Parse(theme.Text, Color.Black), Parse(theme.Secondary, Color.Gray),
            Parse(theme.Track, Color.LightGray), Parse(theme.Accent, Color.Red), Parse(theme.Sage, Color.Green),
            Parse(theme.Amber, Color.Yellow), Parse(theme.Ocean, Color.Gray));
    }

    /// <summary>The quadrant's hue, or null when nothing is recorded (a ring then).</summary>
    public Color? Mood(string? quadrant) => quadrant switch
    {
        nameof(Models.EmotionQuadrant.PleasantEnergetic) => Accent,
        nameof(Models.EmotionQuadrant.PleasantCalm) => Sage,
        nameof(Models.EmotionQuadrant.UnpleasantEnergetic) => Amber,
        nameof(Models.EmotionQuadrant.UnpleasantCalm) => Ocean,
        nameof(Models.EmotionQuadrant.Neutral) => Track,
        _ => null
    };

    /// <summary>Accent / Amber / Sage for high / medium / low, as the planner colours them.</summary>
    public Color Priority(string priority) => priority switch
    {
        nameof(Models.TodoPriority.High) => Accent,
        nameof(Models.TodoPriority.Low) => Sage,
        _ => Amber
    };

    private static Color Parse(string hex, Color fallback)
    {
        try
        {
            return Color.ParseColor(hex);
        }
        catch (Java.Lang.IllegalArgumentException)
        {
            return fallback;
        }
    }
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
