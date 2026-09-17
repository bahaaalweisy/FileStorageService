using System.Text;

namespace FileStorage.Domain.ValueObjects;

public sealed class TagSet
{
    public const char Delimiter = '|';
    public const int MaxTagLength = 64;
    public const int MaxTagCount = 20;

    public IReadOnlyList<string> Values { get; }

    private TagSet(IReadOnlyList<string> values) => Values = values;

    public static TagSet Empty { get; } = new(Array.Empty<string>());

    public static TagSet FromInput(IEnumerable<string>? rawTags)
    {
        if (rawTags is null)
        {
            return Empty;
        }

        var normalized = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var raw in rawTags)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                continue;
            }

            var candidate = raw.Trim().ToLowerInvariant();
            candidate = candidate.Replace(Delimiter.ToString(), string.Empty);
            candidate = new string(candidate.Where(c => !char.IsControl(c)).ToArray());

            if (candidate.Length == 0)
            {
                continue;
            }

            if (candidate.Length > MaxTagLength)
            {
                candidate = candidate[..MaxTagLength];
            }

            if (seen.Add(candidate))
            {
                normalized.Add(candidate);
            }

            if (normalized.Count >= MaxTagCount)
            {
                break;
            }
        }

        normalized.Sort(StringComparer.Ordinal);
        return new TagSet(normalized);
    }

    public static TagSet FromStorage(string? stored)
    {
        if (string.IsNullOrEmpty(stored))
        {
            return Empty;
        }

        var parts = stored.Split(Delimiter, StringSplitOptions.RemoveEmptyEntries);
        return new TagSet(parts);
    }

    public string? ToStorageString()
    {
        if (Values.Count == 0)
        {
            return null;
        }

        var sb = new StringBuilder();
        sb.Append(Delimiter);
        foreach (var tag in Values)
        {
            sb.Append(tag).Append(Delimiter);
        }

        return sb.ToString();
    }

    public static string ToQueryNeedle(string tag)
    {
        var normalized = tag.Trim().ToLowerInvariant().Replace(Delimiter.ToString(), string.Empty);
        return $"{Delimiter}{normalized}{Delimiter}";
    }
}
