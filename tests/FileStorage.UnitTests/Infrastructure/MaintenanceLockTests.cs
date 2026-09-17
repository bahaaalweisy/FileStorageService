using FileStorage.Infrastructure.Storage;
using FluentAssertions;
using Xunit;

namespace FileStorage.UnitTests.Infrastructure;

public class MaintenanceLockTests : IDisposable
{
    private readonly string _root;

    public MaintenanceLockTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "filestorage-lock-tests-" + Guid.NewGuid().ToString("N"));
    }

    [Fact]
    public void TryAcquire_SucceedsWhenNoOtherHolder()
    {
        using var lockHandle = MaintenanceLock.TryAcquire(_root);

        lockHandle.Acquired.Should().BeTrue();
    }

    [Fact]
    public void TryAcquire_FailsWhileAnotherHolderIsActive()
    {
        using var first = MaintenanceLock.TryAcquire(_root);
        first.Acquired.Should().BeTrue();

        using var second = MaintenanceLock.TryAcquire(_root);

        second.Acquired.Should().BeFalse("a second holder must not be able to acquire the same exclusive lock concurrently");
    }

    [Fact]
    public void TryAcquire_SucceedsAgain_AfterThePreviousHolderDisposes()
    {
        var first = MaintenanceLock.TryAcquire(_root);
        first.Acquired.Should().BeTrue();
        first.Dispose();

        using var second = MaintenanceLock.TryAcquire(_root);

        second.Acquired.Should().BeTrue("releasing the lock must allow a subsequent acquisition to succeed");
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}
