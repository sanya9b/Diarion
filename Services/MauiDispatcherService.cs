using System;
using Diarion.Services;
using Microsoft.Maui.ApplicationModel;

namespace Diarion.Services;

public class MauiDispatcherService : IDispatcherService
{
    public void InvokeOnMainThread(Action action) => MainThread.BeginInvokeOnMainThread(action);
}
