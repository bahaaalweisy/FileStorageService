using FileStorage.Infrastructure.Storage;
using FluentAssertions;
using Xunit;

namespace FileStorage.UnitTests.Infrastructure;

public class StoragePathResolverTests : IDisposable
{
    private readonly string _root;
    private readonly StoragePathResolver _resolver;

    public StoragePathResolverTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "filestorage-resolver-tests-" + Guid.NewGuid().ToString("N"));
        _resolver = new StoragePathResolver(_root);
    }

    [Fact]
    public void GetContentPath_IsContainedWithinRoot()
    {
        var path = _resolver.GetContentPath("Abcdefgh12345678ijkl", DateTime.UtcNow);

        path.Should().StartWith(_resolver.RootFullPath);
    }

    [Fact]
    public void GetContentPath_UsesYearMonthDaySegments()
    {
        var date = new DateTime(2026, 3, 7, 0, 0, 0, DateTimeKind.Utc);

        var path = _resolver.GetContentPath("Abcdefgh12345678ijkl", date);

        path.Should().Contain(Path.Combine("2026", "03", "07"));
    }

    [Fact]
    public void GetContentPath_DifferentKeys_SameDay_NeverCollide()
    {
        var date = DateTime.UtcNow;

        var path1 = _resolver.GetContentPath("Abcdefgh12345678ijkl", date);
        var path2 = _resolver.GetContentPath("Zyxwvuts87654321zyxw", date);

        path1.Should().NotBe(path2);
    }

    [Theory]
    [InlineData("../../etc/passwd")]
    [InlineData("..\\..\\windows\\system32")]
    [InlineData("a/b")]
    [InlineData("a\\b")]
    [InlineData("")]
    [InlineData("short")]
    public void GetContentPath_RejectsMalformedKeys(string maliciousKey)
    {
        var act = () => _resolver.GetContentPath(maliciousKey, DateTime.UtcNow);

        act.Should().Throw<ArgumentException>();
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}
