using FileStorage.Domain.ValueObjects;
using FluentAssertions;
using Xunit;

namespace FileStorage.UnitTests.Domain;

public class TagSetTests
{
    [Fact]
    public void FromInput_NormalizesCaseAndTrimsWhitespace()
    {
        var set = TagSet.FromInput(new[] { "  Invoice  ", "INVOICE", "invoice" });

        set.Values.Should().ContainSingle().Which.Should().Be("invoice");
    }

    [Fact]
    public void FromInput_DeduplicatesAfterNormalization()
    {
        var set = TagSet.FromInput(new[] { "a", "A", "a ", " a" });

        set.Values.Should().ContainSingle();
    }

    [Fact]
    public void FromInput_EnforcesMaxTagCount()
    {
        var many = Enumerable.Range(0, TagSet.MaxTagCount + 10).Select(i => $"tag{i}");

        var set = TagSet.FromInput(many);

        set.Values.Count.Should().Be(TagSet.MaxTagCount);
    }

    [Fact]
    public void FromInput_TruncatesOverlongTags()
    {
        var longTag = new string('x', TagSet.MaxTagLength + 20);

        var set = TagSet.FromInput(new[] { longTag });

        set.Values.Single().Length.Should().Be(TagSet.MaxTagLength);
    }

    [Fact]
    public void StorageRoundTrip_PreservesTags()
    {
        var set = TagSet.FromInput(new[] { "invoice", "2026", "urgent" });
        var stored = set.ToStorageString();

        var reloaded = TagSet.FromStorage(stored);

        reloaded.Values.Should().BeEquivalentTo(set.Values);
    }

    [Fact]
    public void QueryNeedle_DoesNotMatchAccidentalSubstring()
    {
        var stored = TagSet.FromInput(new[] { "category" }).ToStorageString();
        var needle = TagSet.ToQueryNeedle("cat");

        stored!.Contains(needle, StringComparison.Ordinal).Should().BeFalse();
    }

    [Fact]
    public void QueryNeedle_MatchesExactTag()
    {
        var stored = TagSet.FromInput(new[] { "invoice", "urgent" }).ToStorageString();
        var needle = TagSet.ToQueryNeedle("invoice");

        stored!.Contains(needle, StringComparison.Ordinal).Should().BeTrue();
    }

    [Fact]
    public void FromInput_StripsDelimiterCharacterFromTagContent()
    {
        var set = TagSet.FromInput(new[] { "a|b" });

        set.Values.Single().Should().NotContain("|");
    }

    [Fact]
    public void Empty_SerializesToNull()
    {
        TagSet.Empty.ToStorageString().Should().BeNull();
    }
}
