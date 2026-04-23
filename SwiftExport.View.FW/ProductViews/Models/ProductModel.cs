using System.ComponentModel;
using SwiftExport.View.FW.Mvvm;

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

        private string _description = string.Empty;
        public string Description
        {
            get => _description;
            set => SetProperty(ref _description, value);
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

        private string _features = string.Empty;
        public string Features
        {
            get => _features;
            set => SetProperty(ref _features, value);
        }

        // 价格字段（与 DTO 对应）
        private decimal _rmbPrice;
        /// <summary>人民币单价</summary>
        public decimal RmbPrice
        {
            get => _rmbPrice;
            set => SetProperty(ref _rmbPrice, value);
        }

        private decimal _usdPrice;
        /// <summary>美元单价</summary>
        public decimal UsdPrice
        {
            get => _usdPrice;
            set => SetProperty(ref _usdPrice, value);
        }

        private string _supplyType = string.Empty;
        public string SupplyType
        {
            get => _supplyType;
            set => SetProperty(ref _supplyType, value);
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

        // 当没有错误的时候返回 null，这里如果要求对整个对象做校验的话可以继续扩展。
        public override string Error => null;

        public override string this[string columnName]
        {
            get
            {
                if (columnName == nameof(Sku))
                {
                    if (string.IsNullOrWhiteSpace(Sku))
                        return "产品型号不能为空";
                }
                else if (columnName == nameof(Description))
                {
                    if (string.IsNullOrWhiteSpace(Description))
                        return "描述不能为空";
                }
                return null;
            }
        }

        #endregion
    }
}
