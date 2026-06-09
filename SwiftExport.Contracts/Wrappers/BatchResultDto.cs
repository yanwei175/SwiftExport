using System.Collections.Generic;
using System;

namespace SwiftExport.Contracts.Wrappers
{
    public class BatchResultDto
    {
        public List<Guid> AddIdList { get; set; } = new List<Guid>();
        public List<Guid> UpdateIdList { get; set; } = new List<Guid>();
        public List<Guid> DeleteIdList { get; set; } = new List<Guid>();
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
}
