namespace {{ProjectName}}.Contracts.Wrappers;

public class BatchResultDto
{
    public List<Guid> AddIdList { get; set; } = [];
    public List<Guid> UpdateIdList { get; set; } = [];
    public List<Guid> DeleteIdList { get; set; } = [];
    public int AffectedRowsTotal => AddIdList.Count + UpdateIdList.Count + DeleteIdList.Count;

    public List<Guid> GetAllAffectedIdList()
    {
        var all = new List<Guid>();
        all.AddRange(AddIdList);
        all.AddRange(UpdateIdList);
        all.AddRange(DeleteIdList);
        return all;
    }
}
