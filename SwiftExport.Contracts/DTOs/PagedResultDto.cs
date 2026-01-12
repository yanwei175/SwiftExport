namespace SwiftExport.Contracts.DTOs;

public class PagedResultDto<T>
{
    public IReadOnlyList<T> Items { get; set; } = new List<T>()!;
    public int TotalCount { get; set; }
    public int PageIndex { get; set; }
    public int PageSize { get; set; }
}