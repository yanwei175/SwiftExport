using SwiftExport.Contracts.DTOs.Orders;
using System;
using System.Collections.Generic;
using System.Text;

namespace SwiftExport.Contracts.DTOs.PackingLists.Responses
{
    public class PackingListPostResultDto
    {
        public string PackingListNo { get; set; }
        public string ReceiveNo { get; set; }
        public bool NeedNegativeStockConfirm { get; set; }
        public string WarningMessage { get; set; }
        public List<PackingListStockShortageDto> Shortages { get; set; } = new List<PackingListStockShortageDto>();
    }
}
