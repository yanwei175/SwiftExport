using SwiftExport.Core.Common;

namespace SwiftExport.Core.Entities.MasterData
{
    public class Bom : BaseEntity
    {
        public string BomNo { get; set; } = default!;
        public Guid SubSkuId { get; set; }
        public string SubSku { get; set; } = default!;
        public int Numerator { get; set; } = 1;
        public int Denominator { get; set; } = 1;
        public string? Note { get; set; }
    }
}
