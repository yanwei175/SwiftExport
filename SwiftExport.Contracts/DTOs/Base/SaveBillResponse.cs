using System;
using System.Collections.Generic;
using System.Text;

namespace SwiftExport.Contracts.DTOs.Base
{
    public class SaveBillResponse<THeader, TItem>
    {
        public THeader Header { get; set; }
        public List<TItem> Items { get; set; } = new List<TItem>();
    }
}
