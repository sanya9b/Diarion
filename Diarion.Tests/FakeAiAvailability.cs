using System.Threading.Tasks;
using Diarion.Services.Ai;

namespace Diarion.Tests;

/// <summary>
/// Consent and capability, as two switches a test can flip.
/// </summary>
/// <remarks>
/// Shared rather than repeated per file because five services now depend on it, and the point of
/// the type under test is that they all answer to the same switch. A per-file mock would let one
/// of them quietly stop asking.
/// </remarks>
public sealed class FakeAiAvailability : IAiAvailability
{
    public bool CanEmbed { get; set; } = true;

    public bool CanGenerate { get; set; } = true;

    public Task<bool> CanEmbedAsync() => Task.FromResult(CanEmbed);

    public Task<bool> CanGenerateAsync() => Task.FromResult(CanEmbed && CanGenerate);
}
