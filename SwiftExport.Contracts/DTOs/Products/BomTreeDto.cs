using System.Collections.Generic;

namespace SwiftExport.Contracts.DTOs.Products
{
    public class BomTreeDto : BomDto
    {
        public List<BomTreeDto> Children { get; set; } = new List<BomTreeDto>();
    }
}