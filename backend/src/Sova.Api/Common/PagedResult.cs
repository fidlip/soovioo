namespace Sova.Api.Common;

/// <summary>A single page of results, with the total count so the client can
/// build pager UI (page count, "N of M") without a separate count request.</summary>
public record PagedResult<T>(IReadOnlyList<T> Items, int TotalCount, int Page, int PageSize);
