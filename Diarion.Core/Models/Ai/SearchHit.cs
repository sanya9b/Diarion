using System;

namespace Diarion.Models.Ai;

/// <summary>A ranked search result, pointing back at the document it came from.</summary>
public sealed record SearchHit(
    string SourceKind,
    string SourceId,
    DateTime SourceDate,
    string Snippet,
    float Score);
