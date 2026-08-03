using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Diarion.Messages;
using Diarion.Models.Ai;
using Diarion.Resources.Localization;
using Diarion.Services;
using Diarion.Services.Ai;

namespace Diarion.ViewModels;

public partial class AiChatViewModel : BaseViewModel
{
    private readonly IDiaryChatService _chat;
    private readonly INavigationService _navigation;
    private readonly IDispatcherService _dispatcher;

    private CancellationTokenSource? _cts;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSend))]
    private string _question = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSend))]
    [NotifyPropertyChangedFor(nameof(IsNotAnswering))]
    private bool _isAnswering;

    public ObservableCollection<ChatTurn> Turns { get; } = [];

    public bool IsAvailable => _chat.IsAvailable;

    public bool IsNotAnswering => !IsAnswering;

    public bool CanSend => !IsAnswering && !string.IsNullOrWhiteSpace(Question);

    public AiChatViewModel(IDiaryChatService chat, INavigationService navigation, IDispatcherService dispatcher)
    {
        _chat = chat;
        _navigation = navigation;
        _dispatcher = dispatcher;
        Title = AppResources.AiChatTitle;
    }

    [RelayCommand]
    private async Task SendAsync()
    {
        if (!CanSend)
        {
            return;
        }

        var asked = Question.Trim();
        Question = string.Empty;
        IsAnswering = true;

        Turns.Add(new ChatTurn(asked, isFromUser: true));

        var answering = new ChatTurn(string.Empty, isFromUser: false);
        Turns.Add(answering);

        _cts = new CancellationTokenSource();
        try
        {
            await foreach (var delta in _chat.AskAsync(asked, _cts.Token))
            {
                if (!delta.IsComplete)
                {
                    // Streamed on the UI thread so the answer visibly arrives rather than appearing
                    // whole after a silence the user reads as a freeze.
                    _dispatcher.InvokeOnMainThread(() => answering.Append(delta.Delta));
                    continue;
                }

                var result = delta.Answer!;
                _dispatcher.InvokeOnMainThread(() => answering.Complete(result));
            }
        }
        catch (OperationCanceledException)
        {
            Turns.Remove(answering);
        }
        finally
        {
            _cts?.Dispose();
            _cts = null;
            IsAnswering = false;
        }
    }

    [RelayCommand]
    private void Stop() => _cts?.Cancel();

    /// <summary>Opens the day a citation came from, the same way a search result does.</summary>
    [RelayCommand]
    private async Task OpenCitationAsync(ChatCitation? citation)
    {
        if (citation is null)
        {
            return;
        }

        if (citation.SourceKind == EmbeddingSourceKind.Note)
        {
            await _navigation.NavigateToAsync(
                "NoteDetail",
                new Dictionary<string, object> { ["NoteId"] = citation.SourceId });
            return;
        }

        WeakReferenceMessenger.Default.Send(new NavigateToDateMessage(citation.SourceDate));
        await _navigation.NavigateToAsync("//MainPage");
    }
}

/// <summary>One message in the conversation. Mutable because the assistant's arrives a token at a time.</summary>
public partial class ChatTurn : ObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasText))]
    private string _text;

    [ObservableProperty]
    private bool _isRefusal;

    public ChatTurn(string text, bool isFromUser)
    {
        _text = text;
        IsFromUser = isFromUser;
    }

    public bool IsFromUser { get; }

    public bool IsFromAssistant => !IsFromUser;

    public bool HasText => !string.IsNullOrEmpty(Text);

    public ObservableCollection<CitationChip> Citations { get; } = [];

    public bool HasCitations => Citations.Count > 0;

    public void Append(string delta) => Text += delta;

    public void Complete(ChatResult result)
    {
        if (result.IsRefusal)
        {
            // Whatever streamed is replaced, not annotated. Leaving an ungrounded answer on screen
            // under a warning would still be leaving it on screen.
            Text = Explain(result.Refusal);
            IsRefusal = true;
            return;
        }

        Text = result.Text;

        foreach (var citation in result.Citations)
        {
            Citations.Add(new CitationChip(citation));
        }

        OnPropertyChanged(nameof(HasCitations));
    }

    private static string Explain(ChatRefusalReason reason) => reason switch
    {
        ChatRefusalReason.Unavailable => AppResources.AiChatUnavailable,
        ChatRefusalReason.Ungrounded => AppResources.AiChatUngrounded,
        _ => AppResources.AiChatNothingRelevant,
    };
}

/// <summary>A tappable source, labelled by date.</summary>
public sealed class CitationChip
{
    public CitationChip(ChatCitation citation)
    {
        Citation = citation;
        Label = citation.SourceDate.ToString("d MMM", CultureInfo.CurrentCulture);
    }

    public ChatCitation Citation { get; }

    public string Label { get; }
}
