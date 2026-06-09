using Mapster;
using SwiftExport.View.FW.Mvvm;
using System;
using System.Threading.Tasks;

namespace SwiftExport.View.FW.PackingListViews.Models
{
    /// <summary>
    /// 装箱单明细前端模型。
    /// 中文注释：属性结构与 PackingListLineDto 对齐，便于前后端字段一一映射。
    /// </summary>
    public class PackingListLineModel : BaseLineModel
    {

        public PackingListLineModel()
        {
            重置计算标识();
        }

        private bool _启动净重小计计算;
        private bool _启动毛重小计计算;
        private bool _启动体积小计计算;
        private bool _启动已发货数量计算;
        /// <summary>
        /// 中文注释：重置所有“手工值”标记。
        /// 用于 DTO 加载完成后恢复自动计算行为。
        /// </summary>
        private void 重置计算标识()
        {
            _启动净重小计计算 = false;
            _启动毛重小计计算 = false;
            _启动体积小计计算 = false;
            _启动已发货数量计算 = false;
        }
        #region 计算逻辑（保留原样 + 自动抑制状态改变）

        private void RecalculateAll()
        {
            // 使用 程序内部修改Scope 控制程序计算不要触发数据修改状态改变，避免不必要的保存提示
            using (DisabledStateScope())
            {
                CalculateCarton();
                CalculateNwSubTotal();
                CalculateGwSubTotal();
                CalculateCbmSubTotal();
                CalculateUnshipped();
            }
        }

        private void CalculateUnshipped()
        {
            if (!_启动已发货数量计算)
            {
                IsFullyShipped = UnshippedQty >= Qty;
            }
        }


        // 计算箱数（Ctns） = 订单数量（QTY） / PcsOfCarton（PcsOfCarton），向上取整
        private void CalculateCarton()
        {
            if (PcsPerPackage <= 0)
            {
                TotalCtns = 0;
                return;
            }

            TotalCtns = Math.Ceiling(Qty / PcsPerPackage);
        }

        // 计算净重小计 = Ctns（Ctns） * NwOfCarton（NwOfCarton），保留两位小数，四舍五入
        private void CalculateNwSubTotal()
        {
            if (!_启动净重小计计算 && TotalCtns.HasValue && Nw_KG.HasValue)
            {
                TotalNw_KG = Math.Round(TotalCtns.Value * Nw_KG.Value, 2, MidpointRounding.AwayFromZero);
            }
            else
            {
                TotalNw_KG = null;
            }
        }

        // 计算毛重小计 = Ctns（Ctns） * GwOfCarton（GwOfCarton），保留两位小数，四舍五入
        private void CalculateGwSubTotal()
        {
            if (!_启动毛重小计计算 && TotalCtns.HasValue && Gw_KG.HasValue)
            {
                TotalGw_KG = Math.Round(TotalCtns.Value * Gw_KG.Value, 2, MidpointRounding.AwayFromZero);
            }
            else
            {
                TotalGw_KG = null;
            }
        }

        // 计算体积小计 = Ctns（Ctns） * CbmOfCarton（CbmOfCarton），保留三位小数，四舍五入
        private void CalculateCbmSubTotal()
        {
            if (!_启动体积小计计算 && TotalCtns.HasValue && Cbm_m3.HasValue)
            {
                TotalCbm_m3 = Math.Round(TotalCtns.Value * Cbm_m3.Value, 3, MidpointRounding.AwayFromZero);
            }
            else
            {
                TotalCbm_m3 = null;
            }
        }

        #endregion

        #region IDataErrorInfo 验证

        public override string Error => null;// 告诉 WPF：行级别没有整体错误，不要动整行的样式,必须返回 null，避免整行标红。如果要跨行验证，请使用 ValidateRow() 方法。

        /// <summary>
        /// 字段级验证（WPF 绑定会自动调用）
        /// 这里写“单字段”的验证逻辑。
        /// </summary>
        public override string this[string columnName]
        {
            get
            {
                if (columnName == nameof(Sku) && string.IsNullOrWhiteSpace(Sku))
                    return "物料号不能为空";

                if (columnName == nameof(Qty) && Qty <= 0)
                    return "出货数量不能为0";

                if ((columnName == nameof(Nw_KG) || columnName == nameof(Gw_KG)) && Nw_KG >= Gw_KG)
                    return "净重必须小于毛重";

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






        #region 属性

        /// <summary>装箱单号</summary>
        private string _packingListNo;
        public string PackingListNo
        {
            get => _packingListNo;
            set => SetProperty(ref _packingListNo, value);
        }

        /// <summary>装箱单表头 Id</summary>
        private int _packingListHeadId;
        public int PackingListHeadId
        {
            get => _packingListHeadId;
            set => SetProperty(ref _packingListHeadId, value);
        }

        /// <summary>SKU</summary>
        private string _sku;
        public string Sku
        {
            get => _sku;
            set => SetProperty(ref _sku, value);
        }

        /// <summary>LinekSku</summary>
        private string _linkSku;
        public string LinkSku
        {
            get => _linkSku;
            set => SetProperty(ref _linkSku, value);
        }

        /// <summary>数量</summary>
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

        private decimal _unShippedQTY;
        public decimal UnshippedQty
        {
            get => _unShippedQTY;
            set => SetProperty(ref _unShippedQTY, value);
        }

        /// <summary>客户 SKU</summary>
        private string _customerSku;
        public string CustomerSku
        {
            get => _customerSku;
            set => SetProperty(ref _customerSku, value);
        }

        /// <summary>封条号</summary>
        private string _sealNo;
        public string SealNo
        {
            get => _sealNo;
            set => SetProperty(ref _sealNo, value);
        }

        /// <summary>发票号</summary>
        private string _invoiceNo;
        public string InvoiceNo
        {
            get => _invoiceNo;
            set => SetProperty(ref _invoiceNo, value);
        }

        /// <summary>销售订单号</summary>
        private string _salesOrderNo;
        public string SalesOrderNo
        {
            get => _salesOrderNo;
            set => SetProperty(ref _salesOrderNo, value);
        }

        /// <summary>销售订单行 Id</summary>
        private Guid? _soLineId;
        public Guid? SoLineId
        {
            get => _soLineId;
            set => SetProperty(ref _soLineId, value);
        }

        /// <summary>英文描述</summary>
        private string _descriptionEn;
        public string DescriptionEn
        {
            get => _descriptionEn;
            set => SetProperty(ref _descriptionEn, value);
        }

        /// <summary>英文饰面</summary>
        private string _finishEn;
        public string FinishEn
        {
            get => _finishEn;
            set => SetProperty(ref _finishEn, value);
        }

        /// <summary>每箱件数</summary>
        private int _pcsPerPackage = 1;
        public int PcsPerPackage
        {
            get => _pcsPerPackage;
            set
            {
                if (SetProperty(ref _pcsPerPackage, value))
                {
                    RecalculateAll();
                }
            }
        }

        /// <summary>净重（KG）</summary>
        private decimal? _nw_KG;
        public decimal? Nw_KG
        {
            get => _nw_KG;
            set
            {
                if (SetProperty(ref _nw_KG, value))
                {
                    RecalculateAll();
                }
            }
        }

        /// <summary>毛重（KG）</summary>
        private decimal? _gw_KG;
        public decimal? Gw_KG
        {
            get => _gw_KG;
            set
            {
                if (SetProperty(ref _gw_KG,value))
                {
                    RecalculateAll();
                }
            }
        }

        /// <summary>体积（m³）</summary>
        private decimal? _cbm_m3;
        public decimal? Cbm_m3
        {
            get => _cbm_m3;
            set
            {
                if (SetProperty(ref _cbm_m3, value))
                    RecalculateAll();
            }
        }

        /// <summary>总箱数</summary>
        private decimal? _totalCtns;
        public decimal? TotalCtns
        {
            get => _totalCtns;
            set => SetProperty(ref _totalCtns, value);
        }

        /// <summary>总净重（KG）</summary>
        private decimal? _totalNw_KG;
        public decimal? TotalNw_KG
        {
            get => _totalNw_KG;
            set
            {
                if (SetProperty(ref _totalNw_KG, value) && !DisabledState)
                    _启动净重小计计算 = true;
            }
        }

        /// <summary>总毛重（KG）</summary>
        private decimal? _totalGw_KG;
        public decimal? TotalGw_KG
        {
            get => _totalGw_KG;
            set
            {
                if (SetProperty(ref _totalGw_KG, value) && !DisabledState)
                    _启动毛重小计计算 = true;
            }
        }

        /// <summary>总体积（m³）</summary>
        private decimal? _totalCbm_m3;
        public decimal? TotalCbm_m3
        {
            get => _totalCbm_m3;
            set
            {
                if (SetProperty(ref _totalCbm_m3, value) && !DisabledState)
                    _启动体积小计计算 = true;
            }
        }

        /// <summary>单位</summary>
        private string _unit;
        public string Unit
        {
            get => _unit;
            set => SetProperty(ref _unit, value);
        }

        /// <summary>备注</summary>
        private string _remark;
        public string Remark
        {
            get => _remark;
            set => SetProperty(ref _remark, value);
        }

        /// <summary>图片地址</summary>
        private string _mainImage;
        public string MainImage
        {
            get => _mainImage;
            set => SetProperty(ref _mainImage, value);
        }

        private bool _isFullyShipped;
        public bool IsFullyShipped
        {
            get => _isFullyShipped;
            set => SetProperty(ref _isFullyShipped, value);
        }



        #endregion

    }
}
