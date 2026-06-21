using SwiftExport.Core.Common;

namespace SwiftExport.Core.Entities.Systems
{

    public class SysDicCurrencyExchange : BaseEntity
    {

        public bool IsEnabled { get; set; } = true;


        public string? FromCurrencyCode { get; set; }


        public string? ToCurrencyCode { get; set; }


        public int Precision { get; set; } = 6;


        public decimal? Rate { get; set; }

        public string? Remark { get; set; }
    }
}
