using System;
using System.Collections.Generic;

namespace SwiftExport.Contracts.Wrappers
{
    public class BatchSaveRequestBase<TDto>
    {
        public List<TDto> AddList { get; set; } = new List<TDto>();
        public List<TDto> UpdateList { get; set; } = new List<TDto>();
        public List<Guid> DeleteIdList { get; set; } = new List<Guid>();
        public bool HasChanges => AddList.Count > 0 || UpdateList.Count > 0 || DeleteIdList.Count > 0;
    }
}
