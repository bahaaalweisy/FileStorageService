using FileStorage.Application.Abstractions;
using FileStorage.Application.Exceptions;
using FileStorage.Application.Files.Dtos;
using FileStorage.Domain.ValueObjects;

namespace FileStorage.Application.Files.UseCases;

public sealed class ListFilesService
{
    private const int MaxPageSize = 100;
    private readonly IStoredObjectRepository _repository;

    public ListFilesService(IStoredObjectRepository repository) => _repository = repository;

    public async Task<PagedResult<StoredObjectDto>> ListAsync(ListFilesQuery query, CancellationToken cancellationToken)
    {
        var page = query.Page < 1 ? 1 : query.Page;
        var pageSize = query.PageSize switch
        {
            < 1 => 20,
            > MaxPageSize => MaxPageSize,
            _ => query.PageSize,
        };

        if (query.CreatedFromUtc.HasValue && query.CreatedToUtc.HasValue && query.CreatedFromUtc > query.CreatedToUtc)
        {
            throw new ValidationAppException("dateRange", "'from' must not be later than 'to'.");
        }

        var normalizedQuery = query with
        {
            Page = page,
            PageSize = pageSize,
            Tag = string.IsNullOrWhiteSpace(query.Tag) ? null : TagSet.ToQueryNeedle(query.Tag),
        };

        var result = await _repository.ListAsync(normalizedQuery, cancellationToken);

        var items = result.Items.Select(UploadFileService.Map).ToList();
        return new PagedResult<StoredObjectDto>(items, result.Page, result.PageSize, result.TotalCount);
    }
}
