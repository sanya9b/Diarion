using System;
using System.Collections.Generic;
using LiteDB;

namespace Diarion.Models;

public class Note
{
    [BsonId]
    public ObjectId Id { get; set; } = ObjectId.NewObjectId();

    public string Title { get; set; } = string.Empty;

    public string Content { get; set; } = string.Empty;

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
