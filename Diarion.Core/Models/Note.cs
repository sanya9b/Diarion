using System;
using System.Collections.Generic;
using Diarion.Services;
using LiteDB;

namespace Diarion.Models;

public class Note
{
    [BsonId]
    public ObjectId Id { get; set; } = ObjectId.NewObjectId();

    public string Title { get; set; } = string.Empty;

    public string Content { get; set; } = string.Empty;

    /// <summary>
    /// The body as one line of readable text, for the row in the notes list. Computed rather than
    /// stored: the list would otherwise show "# Покупки" and "- [x] молоко" back at the user.
    /// </summary>
    [BsonIgnore]
    public string Preview => MarkdownParser.ToPlainText(Content);

    public string ColorTheme { get; set; } = "Theme_Amber";

    /// <summary>Lowercased <c>#tags</c> parsed from <see cref="Content"/> on save.</summary>
    public List<string> Tags { get; set; } = new();

    /// <summary>Normalized titles of notes this one links to via <c>[[Title]]</c>, parsed on save.</summary>
    public List<string> LinkedTitles { get; set; } = new();

    /// <summary>True for quick-captured notes that haven't been filed yet.</summary>
    public bool IsInInbox { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.Now;

    public DateTime UpdatedAt { get; set; } = DateTime.Now;
}
