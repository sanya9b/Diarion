using System;
using System.Threading.Tasks;
using Diarion.Models.Widgets;

namespace Diarion.Services.Widgets;

/// <summary>Builds the day's widget snapshot at a given privacy level (spec 20, R2). Pure read; writes nothing.</summary>
public interface IWidgetSnapshotService
{
    Task<WidgetSnapshot> BuildAsync(DateTime today, WidgetPrivacyLevel level);
}
