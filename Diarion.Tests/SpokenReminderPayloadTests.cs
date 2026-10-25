using Diarion.Services;
using FluentAssertions;
using Xunit;

namespace Diarion.Tests;

/// <summary>Spec 19: what rides inside a spoken notification, and who speaks when it lands.</summary>
public class SpokenReminderPayloadTests
{
    [Fact]
    public void Encode_RoundTripsThroughTryDecode()
    {
        var payload = SpokenReminderPayload.Encode("Нагадування: Сходити в магазин");

        SpokenReminderPayload.TryDecode(payload, out var phrase).Should().BeTrue();
        phrase.Should().Be("Нагадування: Сходити в магазин");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("spoken:")]
    [InlineData("todo:42")]
    [InlineData("Spoken:loud")]
    public void TryDecode_RejectsAnythingThatIsNotASpokenReminder(string? payload)
    {
        SpokenReminderPayload.TryDecode(payload, out var phrase).Should().BeFalse();
        phrase.Should().BeEmpty();
    }

    [Fact]
    public void ShouldSpeak_AndroidSpeaksEverySpokenReminder()
    {
        var payload = SpokenReminderPayload.Encode("Ліки");

        SpokenReminderPayload.ShouldSpeak(payload, sound: null, platformPlaysNamedSound: false, out var phrase).Should().BeTrue();
        phrase.Should().Be("Ліки");
    }

    [Fact]
    public void ShouldSpeak_AppleLeavesAReminderWithItsOwnSoundToTheSystem()
    {
        var payload = SpokenReminderPayload.Encode("Ліки");

        SpokenReminderPayload.ShouldSpeak(payload, "spoken-todo-1.caf", platformPlaysNamedSound: true, out _).Should().BeFalse();
    }

    [Fact]
    public void ShouldSpeak_AppleSpeaksItselfWhenNoSoundWasRendered()
    {
        var payload = SpokenReminderPayload.Encode("Ліки");

        SpokenReminderPayload.ShouldSpeak(payload, sound: "", platformPlaysNamedSound: true, out _).Should().BeTrue();
    }

    [Fact]
    public void ShouldSpeak_ASilentReminderIsNeverSpoken()
    {
        SpokenReminderPayload.ShouldSpeak(null, "chime.caf", platformPlaysNamedSound: false, out _).Should().BeFalse();
    }
}
