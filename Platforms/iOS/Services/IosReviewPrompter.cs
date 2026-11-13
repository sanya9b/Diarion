using System;
using System.Linq;
using System.Threading.Tasks;
using Diarion.Services.Review;
using Microsoft.Maui.ApplicationModel;
using StoreKit;
using UIKit;

namespace Diarion.Services;

/// <summary>
/// The system rating sheet on iOS. StoreKit shows it at most three times a year and never in
/// TestFlight or debug builds, so a "true" here means "asked", not "shown"; the policy upstream counts
/// the ask.
/// </summary>
public sealed class IosReviewPrompter : IReviewPrompter
{
    public Task<bool> RequestAsync()
    {
        var done = new TaskCompletionSource<bool>();
        MainThread.BeginInvokeOnMainThread(() =>
        {
            try
            {
                var scene = UIApplication.SharedApplication.ConnectedScenes
                    .OfType<UIWindowScene>()
                    .FirstOrDefault(s => s.ActivationState == UISceneActivationState.ForegroundActive)
                    ?? UIApplication.SharedApplication.ConnectedScenes.OfType<UIWindowScene>().FirstOrDefault();
                if (scene is null)
                {
                    done.TrySetResult(false);
                    return;
                }

                // iOS 18 moved this to StoreKit 2's AppStore.requestReview(in:), which has no .NET
                // binding that works from UIKit yet; the UIKit entry point still works on every
                // supported iOS and is only marked deprecated, not removed.
#pragma warning disable CA1422
                SKStoreReviewController.RequestReview(scene);
#pragma warning restore CA1422
                done.TrySetResult(true);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"SKStoreReviewController failed: {ex.Message}");
                done.TrySetResult(false);
            }
        });
        return done.Task;
    }
}
