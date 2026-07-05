using System;
using LiteDB;

namespace Diarion.Models;

public class Note
{
    [BsonId]
    public ObjectId Id { get; set; } = ObjectId.NewObjectId();

    public string Title { get; set; } = string.Empty;

    public string Content { get; set; } = string.Empty;

    public string ColorTheme { get; set; } = "Theme_Amber";

    public DateTime CreatedAt { get; set; } = DateTime.Now;

    public DateTime UpdatedAt { get; set; } = DateTime.Now;
}
