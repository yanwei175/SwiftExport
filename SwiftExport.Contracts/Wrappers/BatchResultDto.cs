using System.Collections.Generic;

namespace SwiftExport.Contracts.Wrappers
{
    public class BatchResultDto
    {
        public List<int> AddIdList { get; set; } = new List<int>();
        public List<int> UpdateIdList { get; set; } = new List<int>();
        public List<int> DeleteIdList { get; set; } = new List<int>();
        public int AffectedRowsTotal => AddIdList.Count + UpdateIdList.Count + DeleteIdList.Count;

        public List<int> GetAllAffectedIdList()
        {
            var all = new List<int>();
            all.AddRange(AddIdList);
            all.AddRange(UpdateIdList);
            all.AddRange(DeleteIdList);
            return all;
        }
    }
}
