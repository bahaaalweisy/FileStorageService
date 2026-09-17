using FileStorage.Domain.Entities;
using FileStorage.Domain.Exceptions;
using FileStorage.Domain.ValueObjects;
using FluentAssertions;
using Xunit;

namespace FileStorage.UnitTests.Domain;

public class StoredObjectTests
{
    private static StoredObject CreateSample(string ownerId = "user-1") =>
        StoredObject.Create("abcdefghij0123456789", "report.pdf", 1024, "application/pdf", new string('a', 64), TagSet.Empty, ownerId, DateTime.UtcNow);

    [Fact]
    public void Create_SetsVersionToOne()
    {
        CreateSample().Version.Should().Be(1);
    }

    [Fact]
    public void IsOwnedBy_TrueForCreator_FalseForOthers()
    {
        var obj = CreateSample("user-1");

        obj.IsOwnedBy("user-1").Should().BeTrue();
        obj.IsOwnedBy("user-2").Should().BeFalse();
    }

    [Theory]
    [InlineData("owner", false, true)]
    [InlineData("someone-else", false, false)]
    [InlineData("someone-else", true, true)]
    public void CanBeAccessedBy_FollowsOwnershipOrAdminRule(string requester, bool isAdmin, bool expected)
    {
        var obj = CreateSample("owner");

        obj.CanBeAccessedBy(requester, isAdmin).Should().Be(expected);
    }

    [Fact]
    public void SoftDelete_SetsDeletedAtUtc()
    {
        var obj = CreateSample();
        var now = DateTime.UtcNow;

        obj.SoftDelete(now);

        obj.IsDeleted.Should().BeTrue();
        obj.DeletedAtUtc.Should().Be(now);
    }

    [Fact]
    public void SoftDelete_Twice_ThrowsAlreadyDeleted()
    {
        var obj = CreateSample();
        obj.SoftDelete(DateTime.UtcNow);

        var act = () => obj.SoftDelete(DateTime.UtcNow);

        act.Should().Throw<ObjectAlreadyDeletedException>();
    }

    [Fact]
    public void CanBeHardDeleted_FalseUntilSoftDeleted()
    {
        var obj = CreateSample();

        obj.CanBeHardDeleted.Should().BeFalse();

        obj.SoftDelete(DateTime.UtcNow);

        obj.CanBeHardDeleted.Should().BeTrue();
    }
}
