using SwiftExport.Contracts.DTOs.Base;
using SwiftExport.Contracts.DTOs.Orders;
using System;
using System.Collections.Generic;
using System.Text;

namespace SwiftExport.Contracts.DTOs.PackingLists
{
    public class PackingListDetailDto : BaseDto
    {
        public PackingListHeadDto Header { get; set; }
        public List<PackingListLineDto> Lines { get; set; } = new List<PackingListLineDto>();
    }
}
