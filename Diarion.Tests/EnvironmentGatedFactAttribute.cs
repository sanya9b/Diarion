using System;
using Xunit;

namespace Diarion.Tests;

/// <summary>
/// A fact that only runs when an environment variable is set to <c>1</c>.
/// </summary>
/// <remarks>
/// xUnit 2 has no runtime skip, so the decision is made at discovery: the test reports as skipped
/// with its reason rather than passing silently, which is the distinction that matters when the
/// gated test is the only one that can catch a whole class of bug.
/// </remarks>
[AttributeUsage(AttributeTargets.Method)]
public sealed class EnvironmentGatedFactAttribute : FactAttribute
{
    public EnvironmentGatedFactAttribute(string variable, string reason)
    {
        if (Environment.GetEnvironmentVariable(variable) != "1")
        {
            Skip = $"Set {variable}=1 to run. {reason}";
        }
    }
}
