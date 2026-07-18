using Diarion.Helpers;
using FluentAssertions;
using Xunit;

namespace Diarion.Tests;

public class PinHasherTests
{
    [Fact]
    public void Verify_CorrectPin_ReturnsTrue()
    {
        var (salt, hash) = PinHasher.Hash("1234");

        PinHasher.Verify("1234", salt, hash).Should().BeTrue();
    }

    [Fact]
    public void Verify_WrongPin_ReturnsFalse()
    {
        var (salt, hash) = PinHasher.Hash("1234");

        PinHasher.Verify("0000", salt, hash).Should().BeFalse();
    }

    [Fact]
    public void Hash_SamePin_ProducesDifferentSaltAndHash()
    {
        var a = PinHasher.Hash("1234");
        var b = PinHasher.Hash("1234");

        a.Salt.Should().NotBe(b.Salt);
        a.Hash.Should().NotBe(b.Hash);
    }

    [Fact]
    public void Verify_TamperedOrEmpty_ReturnsFalse()
    {
        var (salt, _) = PinHasher.Hash("1234");

        PinHasher.Verify("1234", salt, "not-a-valid-hash").Should().BeFalse();
        PinHasher.Verify("1234", string.Empty, string.Empty).Should().BeFalse();
    }
}
