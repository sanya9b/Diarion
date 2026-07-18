using System;

namespace Diarion.Services;

/// <summary>Abstracts marshalling work to the UI thread so Core ViewModels stay testable.</summary>
public interface IDispatcherService
{
    void InvokeOnMainThread(Action action);
}
