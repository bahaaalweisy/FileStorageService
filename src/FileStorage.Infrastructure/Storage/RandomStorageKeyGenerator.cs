using FileStorage.Application.Abstractions;
using FileStorage.Domain.ValueObjects;

namespace FileStorage.Infrastructure.Storage;

public sealed class RandomStorageKeyGenerator : IStorageKeyGenerator
{
    public string Generate() => StorageKey.Generate();
}
