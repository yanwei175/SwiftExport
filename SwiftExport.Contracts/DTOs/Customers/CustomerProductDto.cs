using SwiftExport.Contracts.DTOs.Base;
using System;
using System.Collections.Generic;
using System.Text;

namespace SwiftExport.Contracts.DTOs.Customers
{
    public class CustomerProductDto : BaseDto
    {

        // 外键
        public Guid CustomerId { get; set; }
        public string CustomerCode { get; set; }
        public Guid? SkuId { get; set; }
        public string Sku { get; set; }
        public string LinkSku { get; set; }
        public string Description { get; set; }
        public string CategoryName { get; set; }
        public string MaterialType { get; set; }
        public string MainImage { get; set; }
        public string DescriptionEn { get; set; }
        public string FeaturesEn { get; set; }

        // 客户自定义字段
        public string CustomerSku { get; set; }      // 客户料号
        public string CustomerPdDescription { get; set; }      // 客户产品名称
        public string CustomerPdSeries { get; set; }//客户产品系列
        public string CustomerColorName { get; set; }        // 客户颜色叫法
        public string CustomerPdFeatures { get; set; }      // 客户规格
        public string CustomerBarcode1 { get; set; }          // 客户条码
        public string CustomerBarcode2 { get; set; }          // 客户条码
        public string CustomerSkuUnit { get; set; }             // 客户单位

        // 价格相关
        /// <summary>
        /// 客户专用美元单价。
        /// </summary>
        public decimal? USDPrice { get; set; }

        /// <summary>
        /// 客户专用人民币单价。
        /// </summary>
        public decimal? CNYPrice { get; set; }
        public string Currency { get; set; }  //货币,用户枚举值

        // 业务要求
        public int? LeadTimeDays { get; set; }               // 客户交期要求
        public int? MOQ { get; set; }                        // 最小起订量

        // 状态
        public bool IsActive { get; set; }
        public string Note { get; set; }

    }

}
