using System;
using System.Collections.Generic;
using System.Text;

namespace SwiftExport.Contracts.DTOs.PackingLists
{
    public class PackingListPostRequestDto
    {
        public int PackingListId { get; set; }
        public bool AllowNegativeStock { get; set; }
        public string Operator { get; set; }
    }
}
