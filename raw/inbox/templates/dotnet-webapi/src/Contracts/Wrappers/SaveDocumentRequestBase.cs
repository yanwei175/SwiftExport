namespace {{ProjectName}}.Contracts.Wrappers;

public class SaveDocumentRequestBase<THead, TLine>
{
    public THead? Header { get; set; }
    public List<TLine> AddLines { get; set; } = [];
    public List<TLine> UpdateLines { get; set; } = [];
    public List<Guid> DeleteLineIds { get; set; } = [];

    public bool HasChanges => AddLines.Count > 0 || UpdateLines.Count > 0 || DeleteLineIds.Count > 0;
}
