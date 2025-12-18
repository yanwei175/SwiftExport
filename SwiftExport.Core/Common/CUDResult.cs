using System;
using System.Collections.Generic;
using System.Text;

namespace SwiftExport.Core.Common
{
    public class CUDResult
    {
        public List<int> AddIIds { get;  set; }
        public int AffectedRowsTotal { get;  set; }
        public List<int> UpdateIIds { get;  set; }
        public List<int> DeleteIIds { get;  set; }
        public CUDResult()
        {
            AddIIds = new List<int>();
            UpdateIIds = new List<int>();
            DeleteIIds = new List<int>();
        }

        public List<int> GetAllAffectedIDs()
        {
            var all = new List<int>();
            all.AddRange(AddIIds);
            all.AddRange(UpdateIIds);
            all.AddRange(DeleteIIds);
            return all;
        }

    }
}
