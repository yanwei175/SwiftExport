using SwiftExport.Contracts.DTOs.Base;
using System;
using System.Collections.Generic;
using System.Text;

namespace SwiftExport.Contracts.DTOs.PackingLists
{
    public class PackingListLineDto : BaseDto
    {
        public  string PackingListNo { get; set; }
        public  int PackingListHeadId { get; set; }
        public  string Sku { get; set; }
        public  decimal Qty { get; set; } = 0;
        public string CustomerSku { get; set; }
        public string SealNo { get; set; }
        public string InvoiceNo { get; set; }
        public string SalesOrderNo { get; set; }
        public int? SoLineId { get; set; }

        public string DescriptionEn { get; set; }
        public string FinishEn { get; set; }
        public int PcsPerPackage { get; set; } = 1;
        public decimal? Nw_KG { get; set; }
        public decimal? Gw_KG { get; set; }
        public decimal? Cbm_m3 { get; set; }
        public decimal? TotalCtns { get; set; }
        public decimal? TotalNw_KG { get; set; }
        public decimal? TotalGw_KG { get; set; }
        public decimal? TotalCbm_m3 { get; set; }

        public string Unit { get; set; }
        public string Remark { get; set; }



    }
}
