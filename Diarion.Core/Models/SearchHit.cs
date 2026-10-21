using System;

namespace Diarion.Models;

/// <summary>A search result, pointing back at the document it came from.</summary>
/// <param name="SourceKind">One of the <see cref="DocumentKind"/> constants.</param>
public sealed record SearchHit(
    string SourceKind,
    string SourceId,
    DateTime SourceDate,
    string Snippet,
    float Score);
