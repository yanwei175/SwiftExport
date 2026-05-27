using Mapster;
using SwiftExport.View.FW.Mvvm;
using System;
using System.ComponentModel;
using System.Threading.Tasks;

namespace SwiftExport.View.FW.SalesOrderViews.Models
{
    public class SalesOrderLineModel : BaseLineModel, IDataErrorInfo
    {
        #region 加载数据
        /// <summary>
        /// 中文注释：从 DTO 加载到行模型。
        /// 这里使用 程序内部修改Scope 包裹，确保“加载数据库数据”不会被识别成用户修改。
        /// 同时在加载结束后重置手工输入标记，避免后续数量/单价变化时自动计算失效。
        /// </summary>
        public override void LoadFromDto<T>(T dto, bool 抑制清理脏数据 = false)
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

            // DTO 本身没有“手工覆盖金额/小计”的状态字段，
            // 加载完成后统一恢复为自动计算模式，避免打开单据后自动计算被锁死。
            重置计算标识();
        }
        #endregion



        /// <summary>
        /// 中文注释：重置所有“手工值”标记。
        /// 用于 DTO 加载完成后恢复自动计算行为。
        /// </summary>
        private void 重置计算标识()
        {
            _启动净重小计计算 = false;
            _启动毛重小计计算 = false;
            _启动体积小计计算 = false;
            _启动美金小计计算 = false;
            _启动人民币小计计算 = false;
            _启动已发货数量计算 = false;
        }

        #region 基础字段

        private string _mainImage;
        public string MainImage
        {
            get => _mainImage;
            set => SetProperty(ref _mainImage, value);
        }

        private int _salesOrderId;
        public int SalesOrderId
        {
            get => _salesOrderId;
            set => SetProperty(ref _salesOrderId, value);
        }

        private string _orderNo;
        public string OrderNo
        {
            get => _orderNo;
            set => SetProperty(ref _orderNo, value);
        }

        private string _sku = string.Empty;
        public string Sku
        {
            get => _sku;
            set => SetProperty(ref _sku, value);
        }

        private string _customerSku;
        public string CustomerSku
        {
            get => _customerSku;
            set => SetProperty(ref _customerSku, value);
        }

        private string _descriptionEn = string.Empty;
        public string DescriptionEn
        {
            get => _descriptionEn;
            set => SetProperty(ref _descriptionEn, value);
        }

        private string _colorNameEn = string.Empty;
        public string ColorNameEn
        {
            get => _colorNameEn;
            set => SetProperty(ref _colorNameEn, value);
        }

        private string _featuresEn = string.Empty;
        public string FeaturesEn
        {
            get => _featuresEn;
            set => SetProperty(ref _featuresEn, value);
        }

        private string _materialType = string.Empty;
        public string MaterialType
        {
            get => _materialType;
            set => SetProperty(ref _materialType, value);
        }
        #endregion

        #region 计算相关字段

        private bool _启动净重小计计算;
        private bool _启动毛重小计计算;
        private bool _启动体积小计计算;
        private bool _启动美金小计计算;
        private bool _启动人民币小计计算;
        private bool _启动已发货数量计算;

        #region 数量 / 单价 / 金额（保留原样）

        private decimal _qty = 0;
        public decimal Qty
        {
            get => _qty;
            set
            {
                if (SetProperty(ref _qty, value))
                    RecalculateAll();
            }
        }

        private decimal _usdPrice;
        public decimal UsdPrice
        {
            get => _usdPrice;
            set
            {
                if (SetProperty(ref _usdPrice, value))
                    RecalculateAll();
            }
        }

        private decimal _cnyPrice;
        public decimal CnyPrice
        {
            get => _cnyPrice;
            set
            {
                if (SetProperty(ref _cnyPrice, value))
                    RecalculateAll();
            }
        }

        private decimal _usdAmount;
        public decimal UsdAmount
        {
            get => _usdAmount;
            set
            {
                if (SetProperty(ref _usdAmount, value) && !是否程序内部修改)
                    _启动美金小计计算 = true;
            }
        }

        private decimal _cnyAmount;
        public decimal CnyAmount
        {
            get => _cnyAmount;
            set
            {
                if (SetProperty(ref _cnyAmount, value) && !是否程序内部修改)
                    _启动人民币小计计算 = true;
            }
        }

        #endregion

        #region 发货状态（新加字段，用于界面显示和逻辑判断）
        private decimal _shippedQty;
        public decimal ShippedQty
        {
            get => _shippedQty;
            set
            {
                if (SetProperty(ref _shippedQty, value))
                    RecalculateAll();
            }
        }

        private decimal _unShippedQty;
        public decimal UnshippedQty
        {
            get => _unShippedQty;
            set => SetProperty(ref _unShippedQty, value);
        }

        private bool _isPartiallyShipped;
        public bool IsPartiallyShipped
        {
            get => _isPartiallyShipped;
            set => SetProperty(ref _isPartiallyShipped, value);
        }

        private bool _isFullyShipped;
        public bool IsFullyShipped
        {
            get => _isFullyShipped;
            set => SetProperty(ref _isFullyShipped, value);
        }





        #endregion

        #region 包装运输信息（保留原样）

        private int _lineNumber;
        public int LineNumber
        {
            get => _lineNumber;
            set => SetProperty(ref _lineNumber, value);
        }

        private string _subNote = string.Empty;
        public string SubNote
        {
            get => _subNote;
            set => SetProperty(ref _subNote, value);
        }

        private decimal _pcsOfCarton;
        public decimal PcsOfCarton
        {
            get => _pcsOfCarton;
            set
            {
                if (SetProperty(ref _pcsOfCarton, value))
                    RecalculateAll();
            }
        }

        private decimal _nwOfCarton;
        public decimal NwOfCarton
        {
            get => _nwOfCarton;
            set
            {
                if (SetProperty(ref _nwOfCarton, value))
                    RecalculateAll();
            }
        }

        private decimal _gwOfCarton;
        public decimal GwOfCarton
        {
            get => _gwOfCarton;
            set
            {
                if (SetProperty(ref _gwOfCarton, value))
                    RecalculateAll();
            }
        }

        private decimal _cbmOfCarton;
        public decimal CbmOfCarton
        {
            get => _cbmOfCarton;
            set
            {
                if (SetProperty(ref _cbmOfCarton, value))
                    RecalculateAll();
            }
        }

        private decimal _ctns;
        public decimal Ctns
        {
            get => _ctns;
            private set => SetProperty(ref _ctns, value);
        }

        private decimal _nwSubTotal;
        public decimal NwSubTotal
        {
            get => _nwSubTotal;
            set
            {
                if (SetProperty(ref _nwSubTotal, value) && !是否程序内部修改)
                    _启动净重小计计算 = true;
            }
        }

        private decimal _gwSubTotal;
        public decimal GwSubTotal
        {
            get => _gwSubTotal;
            set
            {
                if (SetProperty(ref _gwSubTotal, value) && !是否程序内部修改)
                    _启动毛重小计计算 = true;
            }
        }

        private decimal _cbmSubTotal;
        public decimal CbmSubTotal
        {
            get => _cbmSubTotal;
            set
            {
                if (SetProperty(ref _cbmSubTotal, value) && !是否程序内部修改)
                    _启动体积小计计算 = true;
            }
        }

        #endregion

        #region 计算逻辑（保留原样 + 自动抑制状态改变）

        private void RecalculateAll()
        {
            // 使用 程序内部修改Scope 控制程序计算不要触发数据修改状态改变，避免不必要的保存提示
            using (程序内部修改Scope())
            {
                CalculateCarton();
                CalculateNwSubTotal();
                CalculateGwSubTotal();
                CalculateCbmSubTotal();
                CalculateUsdAmount();
                CalculateRmbAmount();
                CalculateUnshipped();
            }
        }

        private void CalculateUnshipped()
        {
            if (!_启动已发货数量计算)
            {
                UnshippedQty = Qty - ShippedQty;
                IsFullyShipped = ShippedQty >= Qty;
                IsPartiallyShipped = ShippedQty > 0 && ShippedQty < Qty;
            }
        }


        // 计算箱数（Ctns） = 订单数量（QTY） / PcsOfCarton（PcsOfCarton），向上取整
        private void CalculateCarton()
        {
            if (PcsOfCarton <= 0)
            {
                Ctns = 0;
                return;
            }

            Ctns = Math.Ceiling(Qty / PcsOfCarton);
        }

        // 计算净重小计 = Ctns（Ctns） * NwOfCarton（NwOfCarton），保留两位小数，四舍五入
        private void CalculateNwSubTotal()
        {
            if (!_启动净重小计计算)
                NwSubTotal = Math.Round(Ctns * NwOfCarton, 2, MidpointRounding.AwayFromZero);
        }

        // 计算毛重小计 = Ctns（Ctns） * GwOfCarton（GwOfCarton），保留两位小数，四舍五入
        private void CalculateGwSubTotal()
        {
            if (!_启动毛重小计计算)
                GwSubTotal = Math.Round(Ctns * GwOfCarton, 2, MidpointRounding.AwayFromZero);
        }

        // 计算体积小计 = Ctns（Ctns） * CbmOfCarton（CbmOfCarton），保留三位小数，四舍五入
        private void CalculateCbmSubTotal()
        {
            if (!_启动体积小计计算)
                CbmSubTotal = Math.Round(Ctns * CbmOfCarton, 3, MidpointRounding.AwayFromZero);
        }

        // 计算美金总金额 = 订单数量（QTY） * 单位价格（USDPrice），保留两位小数，四舍五入
        private void CalculateUsdAmount()
        {
            if (!_启动美金小计计算)
                UsdAmount = Math.Round(Qty * UsdPrice, 2, MidpointRounding.AwayFromZero);
        }

        // 计算人民币总金额 = 订单数量（QTY） * 单位价格（CNYPrice），保留两位小数，四舍五入
        private void CalculateRmbAmount()
        {
            if (!_启动人民币小计计算)
                CnyAmount = Math.Round(Qty * CnyPrice, 2, MidpointRounding.AwayFromZero);
        }
        #endregion


        #endregion

        #region IDataErrorInfo（保留原样）

        public override string Error => null;// 告诉 WPF：行级别没有整体错误，不要动整行的样式,必须返回 null，避免整行标红。如果要跨行验证，请使用 ValidateRow() 方法。

        /// <summary>
        /// 字段级验证（WPF 绑定会自动调用）
        /// 这里写“单字段”的验证逻辑。
        /// </summary>
        public override string this[string columnName]
        {
            get
            {
                if (columnName == nameof(CustomerSku) && string.IsNullOrWhiteSpace(CustomerSku))
                    return "客户产品编号不能为空";

                if (columnName == nameof(Qty) && Qty <= 0)
                    return "订单数量必须大于 0";

                if (columnName == nameof(UsdPrice) && UsdPrice <= 0)
                    return "单位价格必须大于 0";

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
    }
}
