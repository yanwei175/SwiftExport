namespace {{ProjectName}}.Contracts.Wrappers;

public class PagedRequestDto
{
    public string? Keyword { get; set; }
    public int PageIndex { get; set; } = 1;
    public int PageSize { get; set; } = 20;
    public List<string> BatchKeywords { get; set; } = [];
}
