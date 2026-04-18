using System.Collections.Generic;

namespace SwiftExport.Contracts.Wrappers
{
    public class BatchRequestGeneric<T>
    {
        public List<T> AddList { get; set; } = new List<T>();
        public List<T> UpdateList { get; set; } = new List<T>();
        public List<int> DeleteIdList { get; set; } = new List<int>();
    }
}
