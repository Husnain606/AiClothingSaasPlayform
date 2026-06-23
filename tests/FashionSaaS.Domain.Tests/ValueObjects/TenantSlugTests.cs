using FashionSaaS.Domain.ValueObjects;
using FluentAssertions;

namespace FashionSaaS.Domain.Tests.ValueObjects;

public class TenantSlugTests
{
    [Theory]
    [InlineData("nike")]
    [InlineData("my-brand")]
    [InlineData("brand123")]
    public void ValidSlug_CreatesSuccessfully(string slug)
    {
        var act = () => new TenantSlug(slug);
        act.Should().NotThrow();
    }

    [Theory]
    [InlineData("Nike")]             // uppercase
    [InlineData("my brand")]        // space
    [InlineData("brand!")]          // special char
    [InlineData("")]                // empty
    [InlineData("a-very-long-slug-that-exceeds-the-fifty-character-maximum-limit")] // >50 chars
    public void InvalidSlug_ThrowsArgumentException(string slug)
    {
        var act = () => new TenantSlug(slug);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void TwoSlugsWithSameValue_AreEqual()
    {
        var s1 = new TenantSlug("nike");
        var s2 = new TenantSlug("nike");
        s1.Should().Be(s2);
    }
}
