using Microsoft.UI.Xaml;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace Diarion.WinUI;

/// <summary>
/// Provides application-specific behavior to supplement the default Application class.
/// </summary>
public partial class App : MauiWinUIApplication
{
	/// <summary>
	/// Initializes the singleton application object.  This is the first line of authored code
	/// executed, and as such is the logical equivalent of main() or WinMain().
	/// </summary>
	public App()
	{
		this.InitializeComponent();

#if DEBUG
		// WinUI turns an exception thrown on the UI thread — a bad binding, a layout fault, a XAML
		// element that will not load — into a stowed exception (exit 0xC000027B) and takes the process
		// down with no managed trace anywhere: not on stderr, not in the event log, not through
		// AppDomain.UnhandledException, so the crash reporter never sees it. This writes what WinUI
		// knows next to the app's data so a developer can read it. Nothing about the failure changes.
		UnhandledException += (_, e) =>
		{
			try
			{
				var path = System.IO.Path.Combine(
					Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
					"diarion-winui-crash.log");
				System.IO.File.WriteAllText(path, $"{DateTime.Now:O}\n{e.Message}\n\n{e.Exception}");
			}
			catch
			{
				// A recorder must not turn a diagnosable crash into an undiagnosable one.
			}
		};
#endif
	}

	protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();
}
