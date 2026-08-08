using ShoeTracker.Api.Services;

namespace ShoeTracker.Api.Tests;

public class PasswordHasherTests
{
    [Fact]
    public void Verify_WithCorrectPassword_ReturnsTrue()
    {
        var hash = PasswordHasher.Hash("correct-horse-battery-staple");

        Assert.True(PasswordHasher.Verify("correct-horse-battery-staple", hash));
    }

    [Fact]
    public void Verify_WithWrongPassword_ReturnsFalse()
    {
        var hash = PasswordHasher.Hash("correct-horse-battery-staple");

        Assert.False(PasswordHasher.Verify("wrong-password", hash));
    }

    [Fact]
    public void Hash_CalledTwiceWithSamePassword_ProducesDifferentHashes()
    {
        var first = PasswordHasher.Hash("same-password");
        var second = PasswordHasher.Hash("same-password");

        Assert.NotEqual(first, second);
    }

    [Fact]
    public void Verify_WithMalformedStoredValue_ReturnsFalseInsteadOfThrowing()
    {
        Assert.False(PasswordHasher.Verify("any-password", "not-a-valid-hash"));
    }
}
