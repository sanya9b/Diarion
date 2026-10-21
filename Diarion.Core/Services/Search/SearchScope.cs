using System;

namespace Diarion.Services.Search;

/// <summary>Which documents a search looks through.</summary>
[Flags]
public enum SearchScope
{
    Diary = 1,
    Notes = 2,
    All = Diary | Notes,
}
