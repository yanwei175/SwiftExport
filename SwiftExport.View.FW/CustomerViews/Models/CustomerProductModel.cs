using Mapster;
using SwiftExport.Contracts.DTOs.Customers;
using SwiftExport.Contracts.DTOs.Products;
using SwiftExport.View.FW.Mvvm;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SwiftExport.View.FW.CustomerViews.Models
{
    public class CustomerProductModel : BaseLineModel, IBaseModel, IDataErrorInfo
    {
        public override void LoadFromDto<CustomerProductDto>(CustomerProductDto dto, bool 抑制清理脏数据 = false) //加载数据方法
        {
            if (dto == null) return;

            using (程序内部修改Scope())
            {

                dto.Adapt(this);//使用 Mapster 将 DTO 的属性值映射到 Model(this) 上
                if (this.Id == 0)
                { this.MarkAsAdded(); }

                // 根据调用方需要决定是否立即提交/清理脏数据
                if (!抑制清理脏数据)
                    CommitChanges();
            }
        }
        #region 字段属性

        // 外键
        private int _customerId;
        public int CustomerId
        {
            get => _customerId;
            set => SetProperty(ref _customerId, value);
        }

        private int _skuId;
        public int SkuId
        {
            get => _skuId;
            set => SetProperty(ref _skuId, value);
        }

        private string _sku;
        public string Sku
        {
            get => _sku;
            set => SetProperty(ref _sku, value);
        }

        private string _description;
        public string Description
        {
            get => _description;
            set => SetProperty(ref _description, value);
        }

        // 与 DTO 对齐：分类可能为空
        private int? _categoryId;
        public int? CategoryId
        {
            get => _categoryId;
            set => SetProperty(ref _categoryId, value);
        }

        // 分类名称（只读展示，来自 DTO）
        private string _categoryName;
        public string CategoryName
        {
            get => _categoryName;
            set => SetProperty(ref _categoryName, value);
        }

        private string _materialType;
        public string MaterialType
        {
            get => _materialType;
            set => SetProperty(ref _materialType, value);
        }

        // 客户自定义字段
        private string _customerSku;
        public string CustomerSku
        {
            get => _customerSku;
            set => SetProperty(ref _customerSku, value);
        }

        private string _customerPdDescription;
        public string CustomerPdDescription
        {
            get => _customerPdDescription;
            set => SetProperty(ref _customerPdDescription, value);
        }

        private string _customerPdSeries;
        /// <summary>
        /// 客户产品系列
        /// </summary>
        public string CustomerPdSeries
        {
            get => _customerPdSeries;
            set => SetProperty(ref _customerPdSeries, value);
        }

        private string _customerColorName;
        public string CustomerColorName
        {
            get => _customerColorName;
            set => SetProperty(ref _customerColorName, value);
        }

        private string _customerPdFeatures;
        public string CustomerPdFeatures
        {
            get => _customerPdFeatures;
            set => SetProperty(ref _customerPdFeatures, value);
        }

        private string _linkSku;
        /// <summary>
        /// Link SKU（关联料号），DTO 中存在
        /// </summary>
        public string LinkSku
        {
            get => _linkSku;
            set => SetProperty(ref _linkSku, value);
        }

        private string _customerBarcode1;
        public string CustomerBarcode1
        {
            get => _customerBarcode1;
            set => SetProperty(ref _customerBarcode1, value);
        }
        private string _barcode1Type;
        public string Barcode1Type
        {
            get => _barcode1Type;
            set => SetProperty(ref _barcode1Type, value);
        }

        private string _customerBarcode2;
        public string CustomerBarcode2
        {
            get => _customerBarcode2;
            set => SetProperty(ref _customerBarcode2, value);
        }
        private string _barcode2Type;
        public string Barcode2Type
        {
            get => _barcode2Type;
            set => SetProperty(ref _barcode2Type, value);
        }

        private string _customerSkuUnit;
        public string CustomerSkuUnit
        {
            get => _customerSkuUnit;
            set => SetProperty(ref _customerSkuUnit, value);
        }

        // 价格相关（DTO 中为可空）
        private decimal? _usdPrice;
        /// <summary>
        /// 客户专用美元单价。
        /// </summary>
        public decimal? USDPrice
        {
            get => _usdPrice;
            set => SetProperty(ref _usdPrice, value);
        }

        private decimal? _cnyPrice;
        /// <summary>
        /// 客户专用人民币单价。
        /// </summary>
        public decimal? CNYPrice
        {
            get => _cnyPrice;
            set => SetProperty(ref _cnyPrice, value);
        }

        private string _currency;
        public string Currency
        {
            get => _currency;
            set => SetProperty(ref _currency, value);
        }

        // 业务要求

        // 业务要求（可空，与 DTO 对齐）
        private int? _leadTimeDays;
        public int? LeadTimeDays
        {
            get => _leadTimeDays;
            set => SetProperty(ref _leadTimeDays, value);
        }

        private int? _moq;
        public int? MOQ
        {
            get => _moq;
            set => SetProperty(ref _moq, value);
        }

        // 状态
        private bool _isActive;
        public bool IsActive
        {
            get => _isActive;
            set => SetProperty(ref _isActive, value);
        }

        private bool _isDeleted;
        public bool IsDeleted
        {
            get => _isDeleted;
            set => SetProperty(ref _isDeleted, value);
        }

        // DTO 中存在的客户编码
        private string _customerCode;
        public string CustomerCode
        {
            get => _customerCode;
            set => SetProperty(ref _customerCode, value);
        }

        // 审计字段
        private string _createdBy;
        public string CreatedBy
        {
            get => _createdBy;
            set => SetProperty(ref _createdBy, value);
        }

        private DateTime _createdDate;
        public DateTime CreatedDate
        {
            get => _createdDate;
            set => SetProperty(ref _createdDate, value);
        }

        private string _updatedBy;
        public string UpdatedBy
        {
            get => _updatedBy;
            set => SetProperty(ref _updatedBy, value);
        }

        private DateTime? _updatedDate;
        public DateTime? UpdatedDate
        {
            get => _updatedDate;
            set => SetProperty(ref _updatedDate, value);
        }

        private string _mainImage;
        public string MainImage
        {
            get => _mainImage;
            set => SetProperty(ref _mainImage, value);
        }
        #endregion



        // ============================================================
        // 验证（IDataErrorInfo）用于字段级验证，适合验证单个字段的有效性，例如：必填项、格式验证等。
        // ============================================================
        #region IDataErrorInfo Members

        public override string Error => null;// 告诉 WPF：行级别没有整体错误，不要动整行的样式,必须返回 null，避免整行标红。如果要跨行验证，请使用 ValidateRow() 方法。

        public override string this[string columnName]
        {
            get
            {
                if (columnName == nameof(Sku) && string.IsNullOrWhiteSpace(Sku))
                        return "产品型号不能为空";
       
                if (columnName == nameof(CustomerSku)&& string.IsNullOrWhiteSpace(CustomerSku))
                        return "客户产品编号不能为空";
                return null;
            }
        }
        /// <summary>
        /// 行级验证（跨字段验证）
        /// 用于验证“多个字段组合是否有效”
        /// 例如：数量和单价必须同时有效、包装信息必须完整等。
        /// 保存时会自动调用 IsValid → ValidateRow。
        /// </summary>
        public override string ValidateRow()
        {
            //// 示例：数量和单价必须同时有效
            //if (QTY > 0 && USDPrice > 0)
            return null;

            //return "数量和单价必须同时有效";
        }

        /// <summary>
        /// 异步验证（可选）
        /// 用于数据库验证，例如：SKU 是否存在、产品是否停产。
        /// 默认不做任何事，不会影响性能，也不会影响 UI。
        /// 保存时你可以手动调用：await line.ValidateAsync()
        /// </summary>
        public override async Task<string> ValidateAsync()
        {
            // 示例（伪代码）：
            // if (!await ProductService.ExistsAsync(Sku))
            //     return "SKU 不存在于数据库";

            return null; // 默认无错误
        }

        #endregion



        // ============================================================
        // 状态管理（提交/撤销）如果有需要，可以在这里实现更复杂的状态管理逻辑，例如：记录修改历史、支持多级撤销等。
        // ============================================================
        #region 状态管理

        public override void CommitChanges()
        {
            base.CommitChanges();
        }

        public override void RollbackChanges()
        {
            base.RollbackChanges();
        }

        #endregion
    }
}
