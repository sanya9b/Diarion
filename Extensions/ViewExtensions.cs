using System.Threading.Tasks;
using Microsoft.Maui.Controls;

namespace Diarion.Extensions;

public static class ViewExtensions
{
    public static Task<bool> HeightRequestTo(this VisualElement view, double height, uint length = 250, Easing? easing = null)
    {
        var tcs = new TaskCompletionSource<bool>();
        var animation = new Animation(v => view.HeightRequest = v, view.Height, height, easing);
        animation.Commit(view, "HeightRequestTo", 16, length, finished: (v, c) => tcs.SetResult(c));
        return tcs.Task;
    }
}
