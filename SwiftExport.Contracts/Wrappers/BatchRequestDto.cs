using System.Collections.Generic;
using System;

namespace SwiftExport.Contracts.Wrappers
{
    public class BatchRequestDto<T>
    {
        public List<T> AddList { get; set; } = new List<T>();
        public List<T> UpdateList { get; set; } = new List<T>();
        public List<Guid> DeleteIdList { get; set; } = new List<Guid>();
        public bool HasChanges => AddList.Count > 0 || UpdateList.Count > 0 || DeleteIdList.Count > 0;
    }
}
