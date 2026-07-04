namespace SwiftExportClient.Mvvm;

public sealed class GridChangeSet<TDto>(
    IReadOnlyList<TDto> added,
    IReadOnlyList<TDto> modified,
    IReadOnlyList<TDto> deleted)
{
    public IReadOnlyList<TDto> Added { get; } = added;

    public IReadOnlyList<TDto> Modified { get; } = modified;

    public IReadOnlyList<TDto> Deleted { get; } = deleted;

    public bool HasChanges => Added.Count > 0 || Modified.Count > 0 || Deleted.Count > 0;
}
