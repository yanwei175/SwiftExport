using SwiftExport.Contracts.SysEnums.StaticEnums;
using SwiftExport.View.FW.Mvvm;
using System;

namespace SwiftExport.View.FW.ProductViews.Models
{
    /// <summary>
    /// 产品行模型。
    /// 继承 BaseLineModel：State 状态机、MarkAsModified 均由基类 SetProperty 自动处理，无需手动调用。
    /// IBaseModel、IDataErrorInfo 已由 BaseLineModel 实现，无需在类声明重复列出。
    /// </summary>
    public class ProductModel : BaseLineModel
    {
        public override void LoadFromDto<TDto>(TDto dto, bool 抑制清理脏数据 = false)
        {
            if (dto == null) return;
            using (程序内部修改Scope())
            {
                Mapster.TypeAdapter.Adapt(dto, this);
                if (this.Id == 0)
                    this.MarkAsAdded();

                if (!抑制清理脏数据)
                    CommitChanges();
            }
        }

        private string _masterSku = string.Empty;
        public string MasterSku
        {
            get => _masterSku;
            set => SetProperty(ref _masterSku, value);
        }

        private string _sku = string.Empty;
        public string Sku
        {
            get => _sku;
            set => SetProperty(ref _sku, value);
        }

        private string _descriptionEn = string.Empty;
        public string DescriptionEn
        {
            get => _descriptionEn;
            set => SetProperty(ref _descriptionEn, value);
        }

        private string _descriptionZh;
        public string DescriptionZh
        {
            get => _descriptionZh;
            set => SetProperty(ref _descriptionZh, value);
        }

        private string _materialType = string.Empty;
        public string MaterialType
        {
            get => _materialType;
            set => SetProperty(ref _materialType, value);
        }

        // 这个颜色得修改成持有ProductColor的Id,需要在修改颜色的时候修改掉这个Id. 因为颜色会有中文和英文名称.
        // 那么怎么在修改颜色的时候即修改掉ID又修改掉这个finish呢?
        private int _colorId;
        public int ColorId
        {
            get => _colorId;
            set => SetProperty(ref _colorId, value);
        }

        private string _finish = string.Empty;
        public string Finish
        {
            get => _finish;
            set => SetProperty(ref _finish, value);
        }

        private string _featuresEn = string.Empty;
        public string FeaturesEn
        {
            get => _featuresEn;
            set => SetProperty(ref _featuresEn, value);
        }

        private string _featuresZh = string.Empty;
        public string FeaturesZh
        {
            get => _featuresZh;
            set => SetProperty(ref _featuresZh, value);
        }

        // 价格字段（与 DTO 对应）
        private decimal _cnyPrice;
        /// <summary>CNYPrice</summary>
        public decimal CNYPrice
        {
            get => _cnyPrice;
            set => SetProperty(ref _cnyPrice, value);
        }

        private decimal _usdPrice;
        /// <summary>美元单价</summary>
        public decimal USDPrice
        {
            get => _usdPrice;
            set => SetProperty(ref _usdPrice, value);
        }

        // 装箱基础数据（供销售订单行带出）
        private decimal? _pcsOfCarton;
        public decimal? PcsOfCarton
        {
            get => _pcsOfCarton;
            set => SetProperty(ref _pcsOfCarton, value);
        }

        private decimal? _nwOfCarton;
        public decimal? NwOfCarton
        {
            get => _nwOfCarton;
            set => SetProperty(ref _nwOfCarton, value);
        }

        private decimal? _gwOfCarton;
        public decimal? GwOfCarton
        {
            get => _gwOfCarton;
            set => SetProperty(ref _gwOfCarton, value);
        }

        private decimal? _cbmOfCarton;
        public decimal? CbmOfCarton
        {
            get => _cbmOfCarton;
            set => SetProperty(ref _cbmOfCarton, value);
        }

        private string _supplyType = string.Empty;
        public string SupplyType
        {
            get => _supplyType;
            set
            {
                // 当供应类型变化时，需要触发 BomNo 的重新验证，
                // 否则 DataGrid 可能不会立即在 BomNo 单元格上显示/清除红框。
                if (SetProperty(ref _supplyType, value))
                    RaisePropertyChanged(nameof(BomNo));
            }
        }

        private string _bomNo = string.Empty;
        public string BomNo
        {
            get => _bomNo;
            set => SetProperty(ref _bomNo, value);
        }

        // 这个分类得修改成持有ProductCategory的Id对象,Id是唯一的. 因为分类会有中文和英文名称,如果直接持有名称的话,修改分类名称的时候就麻烦了.
        private int _categoryId;
        public int CategoryId
        {
            get => _categoryId;
            set => SetProperty(ref _categoryId, value);
        }

        private string _categoryName = string.Empty;
        public string CategoryName
        {
            get => _categoryName;
            set => SetProperty(ref _categoryName, value);
        }

        private string _note = string.Empty;
        public string Note
        {
            get => _note;
            set => SetProperty(ref _note, value);
        }

        private string _supplierName = string.Empty;
        /// <summary>供应商名称（DTO 中的聚合字段）</summary>
        public string SupplierName
        {
            get => _supplierName;
            set => SetProperty(ref _supplierName, value);
        }

        private string _linkSku = string.Empty;
        public string LinkSku
        {
            get => _linkSku;
            set => SetProperty(ref _linkSku, value);
        }

        private string _mainImage = string.Empty;
        public string MainImage
        {
            get => _mainImage;
            set => SetProperty(ref _mainImage, value);
        }

        private string _customerCode = string.Empty;
        public string CustomerCode
        {
            get => _customerCode;
            set => SetProperty(ref _customerCode, value);
        }

        private string _cSku = string.Empty;
        public string CSku
        {
            get => _cSku;
            set => SetProperty(ref _cSku, value);
        }

        private string _supplierCode = string.Empty;
        public string SupplierCode
        {
            get => _supplierCode;
            set => SetProperty(ref _supplierCode, value);
        }

        private string _sSku = string.Empty;
        public string SSku
        {
            get => _sSku;
            set => SetProperty(ref _sSku, value);
        }


        #region IDataErrorInfo Members

        // 行级错误信息：统一走 ValidateRow，确保跨字段校验可以被 IDataErrorInfo 正确感知。
        public override string Error => ValidateRow();

        /// <summary>
        /// 行级验证（跨字段）：
        /// 当 SupplyTypeEnums 不等于“直接采购”时，BomNo 必填。
        /// </summary>
        /// <returns>返回错误信息；无错误返回 null。</returns>
        public override string ValidateRow()
        {
            // 供应类型去除首尾空格后参与判断，避免因为输入空格导致规则失效。
            var supplyType = (SupplyType ?? string.Empty).Trim();

            // 业务规则：只要不是“直接采购”，就必须填写 BOM 编号。
            // 供应类型为空时由字段级校验处理，这里不重复报错。
            if (!string.IsNullOrWhiteSpace(supplyType)
                && !string.Equals(supplyType, SupplyTypeEnums.直接采购, StringComparison.Ordinal)
                && string.IsNullOrWhiteSpace(BomNo))
            {
                return "当供应类型不为“直接采购”时，BOM编号不能为空";
            }

            return null;
        }

        public override string this[string columnName]
        {
            get
            {
                if (columnName == nameof(Sku))
                {
                    if (string.IsNullOrWhiteSpace(Sku))
                        return "产品型号不能为空";
                }
                if (columnName==nameof(SupplyType))
                {
                    if (string.IsNullOrWhiteSpace(SupplyType))
                        return "供应类型不能为空";
                }

                // 为了让 BomNo 单元格出现红框，这里返回跨字段规则错误。
                if (columnName == nameof(BomNo))
                    return ValidateRow();

                return null;
            }
        }



        #endregion
    }
}
