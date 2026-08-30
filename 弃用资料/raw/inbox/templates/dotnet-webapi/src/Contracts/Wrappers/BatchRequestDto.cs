namespace {{ProjectName}}.Contracts.Wrappers;

public class BatchRequestDto<T>
{
    public List<T> AddList { get; set; } = [];
    public List<T> UpdateList { get; set; } = [];
    public List<Guid> DeleteIdList { get; set; } = [];
    public bool HasChanges => AddList.Count > 0 || UpdateList.Count > 0 || DeleteIdList.Count > 0;
}
