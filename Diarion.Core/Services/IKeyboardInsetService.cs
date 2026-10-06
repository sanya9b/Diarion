using System;

namespace Diarion.Services;

/// <summary>
/// How much of the window the soft keyboard is covering, in device-independent units.
/// </summary>
/// <remarks>
/// MAUI does not report the keyboard at all, and the platforms disagree about what one even does to
/// a window. Android moves the layout up, so the bottom of the page is already above the keyboard and
/// the overlap is nothing. iOS slides the keyboard over the window and leaves the layout where it is,
/// so anything docked to the bottom is behind it until it is lifted by exactly this much. Windows and
/// Mac have no soft keyboard.
///
/// One number, asked once, keeps that argument out of the XAML: the note's formatting bar is spaced
/// off the bottom by <see cref="Overlap"/> and does not know which platform it is on.
/// </remarks>
public interface IKeyboardInsetService
{
    /// <summary>Device-independent units of the window hidden by the keyboard; zero when it is down.</summary>
    double Overlap { get; }

    /// <summary>Raised when the keyboard opens, closes or changes height.</summary>
    event EventHandler? OverlapChanged;
}

/// <summary>
/// The answer where nothing ever covers the window: desktop, and any platform without a registered
/// implementation.
/// </summary>
public sealed class NoKeyboardInsetService : IKeyboardInsetService
{
    public double Overlap => 0;

    /// <summary>Never raised. Kept so consumers can subscribe without asking which platform they are on.</summary>
    public event EventHandler? OverlapChanged
    {
        add { }
        remove { }
    }
}
