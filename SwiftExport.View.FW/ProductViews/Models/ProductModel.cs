using System.ComponentModel;
using SwiftExport.View.FW.Mvvm;

namespace SwiftExport.View.FW.ProductViews.Models
{
    public class ProductModel : BaseLineModel, IBaseModel, IDataErrorInfo
    {
        public override void LoadFromDto<ProductDto>(ProductDto dto, bool 抑制清理脏数据 = false)
        {
            if (dto == null) return;
            using (程序内部修改Scope())
            {
                Mapster.TypeAdapter.Adapt(dto, this);
                if (this.Id == 0)
                { this.MarkAsAdded(); }

                if (!抑制清理脏数据)
                    CommitChanges();
            }
        }

        private string _masterSku = string.Empty;
        public string MasterSku
        {
            get { return _masterSku; }
            set { if (SetProperty(ref _masterSku, value)) MarkAsModified(); }
        }

        private string _sku = string.Empty;
        public string Sku
        {
            get { return _sku; }
            set { if (SetProperty(ref _sku, value)) MarkAsModified(); }
        }

        private string _description = string.Empty;
        public string Description
        {
            get { return _description; }
            set { if (SetProperty(ref _description, value)) MarkAsModified(); }
        }
        //这个颜色得修改成持有ProductColor的Id,需要在修改颜色的时候修改掉这个Id. 因为颜色会有中文和英文名称.

        //那么怎么在修改颜色的时候即修改掉ID又修改掉这个finish呢?
        private int _colorId;
        public int ColorId
        {
            get { return _colorId; }
            set { if (SetProperty(ref _colorId, value)) MarkAsModified(); }
        }

        private string _finish = string.Empty;
        public string Finish
        {
            get { return _finish; }
            set { if (SetProperty(ref _finish, value)) MarkAsModified(); }
        }

        private string _features = string.Empty;
        public string Features
        {
            get { return _features; }
            set { if (SetProperty(ref _features, value)) MarkAsModified(); }
        }

        // 价格字段（与 DTO 对应）
        private decimal _rmbPrice;
        /// <summary>
        /// 人民币单价
        /// </summary>
        public decimal RmbPrice
        {
            get => _rmbPrice;
            set { if (SetProperty(ref _rmbPrice, value)) MarkAsModified(); }
        }

        private decimal _usdPrice;
        /// <summary>
        /// 美元单价
        /// </summary>
        public decimal UsdPrice
        {
            get => _usdPrice;
            set { if (SetProperty(ref _usdPrice, value)) MarkAsModified(); }
        }

        private string _supplyType = string.Empty;
        public string SupplyType
        {
            get { return _supplyType; }
            set { if (SetProperty(ref _supplyType, value)) MarkAsModified(); }
        }

        private string _bomNo = string.Empty;
        public string BomNo
        {
            get { return _bomNo; }
            set { if (SetProperty(ref _bomNo, value)) MarkAsModified(); }
        }

        // 这个分类得修改成持有ProductCategory的Id对象,Id是唯一的. 因为分类会有中文和英文名称,如果直接持有名称的话,修改分类名称的时候就麻烦了.
        private int _categoryId;
        public int CategoryId
        {
            get { return _categoryId; }
            set { if (SetProperty(ref _categoryId, value)) MarkAsModified(); }
        }

        private string _categoryName = string.Empty;
        public string CategoryName
        {
            get { return _categoryName; }
            set { if (SetProperty(ref _categoryName, value)) MarkAsModified(); }
        }

        private string _note = string.Empty;
        public string Note
        {
            get { return _note; }
            set { if (SetProperty(ref _note, value)) MarkAsModified(); }
        }

        private string _supplierName = string.Empty;
        /// <summary>
        /// 供应商名称（DTO 中的聚合字段）
        /// </summary>
        public string SupplierName
        {
            get => _supplierName;
            set { if (SetProperty(ref _supplierName, value)) MarkAsModified(); }
        }

        private string _linkSku = string.Empty;
        public string LinkSku
        {
            get { return _linkSku; }
            set { if (SetProperty(ref _linkSku, value)) MarkAsModified(); }
        }

        private string _mainImage = string.Empty;
        public string MainImage
        {
            get { return _mainImage; }
            set { if (SetProperty(ref _mainImage, value)) MarkAsModified(); }
        }

        private string _customerCode = string.Empty;
        public string CustomerCode
        {
            get { return _customerCode; }
            set { if (SetProperty(ref _customerCode, value)) MarkAsModified(); }
        }

        private string _cSku = string.Empty;
        public string CSku
        {
            get { return _cSku; }
            set { if (SetProperty(ref _cSku, value)) MarkAsModified(); }
        }

        private string _supplierCode = string.Empty;
        public string SupplierCode
        {
            get { return _supplierCode; }
            set { if (SetProperty(ref _supplierCode, value)) MarkAsModified(); }
        }

        private string _sSku = string.Empty;
        public string SSku
        {
            get { return _sSku; }
            set { if (SetProperty(ref _sSku, value)) MarkAsModified(); }
        }

        protected override void MarkAsModified()
        {
            if (State == ModelState.Unchanged)
            {
                State = ModelState.Modified;
            }
        }

        public override void RaisePropertyChanged([System.Runtime.CompilerServices.CallerMemberName] string propertyName = null)
        {
            base.RaisePropertyChanged(propertyName);

            // 手动触发索引器 ([{propName}]) 变更通知，使得通过 DataGridAssist 注入的"软校验" DataTrigger
            // 可以随着用户的每一次键盘敲击实时响应更新UI，而不会像 WPF 原生验证那样锁死焦点！
            if (propertyName != "Item[]" && propertyName != nameof(State) && propertyName != nameof(IsChecked))
            {
                base.RaisePropertyChanged("Item[]");
            }
        }

        #region IDataErrorInfo Members

        // 当没有错误的时候返回 null，这里如果要求对整个对象做校验的话可以继续扩展。
        public override string Error
        {
            get
            {
                if (!string.IsNullOrEmpty(this[nameof(Sku)])) return this[nameof(Sku)];
                if (!string.IsNullOrEmpty(this[nameof(Description)])) return this[nameof(Description)];
                return null;
            }
        }

        public override string this[string columnName]
        {
            get
            {
                string result = null;

                if (columnName == nameof(Sku))
                {
                    if (string.IsNullOrWhiteSpace(Sku))
                        result = "产品型号不能为空";
                }
                else if (columnName == nameof(Description))
                {
                    if (string.IsNullOrWhiteSpace(Description))
                        result = "描述不能为空";
                }

                return result;
            }
        }

        #endregion
    }
}
