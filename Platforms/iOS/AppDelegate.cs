using Diarion.Services.Ai;
using Foundation;
using UIKit;

namespace Diarion;

[Register("AppDelegate")]
public class AppDelegate : MauiUIApplicationDelegate
{
	protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();

	/// <summary>
	/// The system finished a model download while the app was suspended or gone, and has woken it up
	/// to hand the file over.
	/// </summary>
	/// <remarks>
	/// Without this the woken app never recreates the session, the delegate callbacks never arrive,
	/// and the finished file is discarded — which is to say the whole background download quietly
	/// achieves nothing. The handler must be called once the deliveries are done; that happens in
	/// <c>DidFinishEventsForBackgroundSession</c> inside <see cref="BackgroundSessionTransfer"/>.
	///
	/// Exported rather than overridden: <see cref="MauiUIApplicationDelegate"/> descends from
	/// <see cref="UIResponder"/> and implements the delegate protocol as an interface, so an
	/// optional member like this one has no base method to override — the selector is the binding.
	/// </remarks>
	[Export("application:handleEventsForBackgroundURLSession:completionHandler:")]
	public void HandleEventsForBackgroundUrl(
		UIApplication application,
		string sessionIdentifier,
		Action completionHandler) =>
		BackgroundSessionTransfer.HandleEventsForBackgroundSession(sessionIdentifier, completionHandler);
}
