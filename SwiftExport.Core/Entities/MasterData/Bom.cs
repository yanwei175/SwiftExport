using SwiftExport.Core.Common;
using System.ComponentModel.DataAnnotations.Schema;

namespace SwiftExport.Core.Entities.MasterData
{
    [Table("Bom")]
    public class Bom : BaseEntity
    {
        public string BomNo { get; set; } = default!;
        public int BomVersion { get; set; }
        public Guid? ParentSkuId { get; set; }
        public string ParentSku { get; set; } = default!;
        public int LineNumber { get; set; } = 1;
        public Guid? SubSkuId { get; set; }
        public string SubSku { get; set; } = default!;
        public int Numerator { get; set; } = 1;
        public int Denominator { get; set; } = 1;
        public string? Note { get; set; }
    }
}
