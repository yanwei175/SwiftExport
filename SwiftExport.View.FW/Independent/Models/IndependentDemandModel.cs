using Mapster;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.Contracts.SysEnums.StaticEnums;
using SwiftExport.View.FW.Mvvm;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SwiftExport.View.FW.Independent.Models
{
    public class IndependentDemandModel : BaseLineModel
    {
        private ConfirmationStatusEnum _confirmationStatus;
        public ConfirmationStatusEnum ConfirmationStatus
        {
            get => _confirmationStatus;
            set
            {
                if (SetProperty(ref _confirmationStatus, value))
                {
                    RaisePropertyChanged(nameof(ConfirmationStatusText));
                }
            }
        }
        public string ConfirmationStatusText => ConfirmationStatus.ToString();

        #region 来源信息 (Source)

        private string _sourceType = string.Empty;
        /// <summary>
        /// 来源单据类型（枚举：SalesOrder, SampleOrder, Forecast, StockPlan 等）
        /// </summary>
        public string SourceType
        {
            get => _sourceType;
            set => SetProperty(ref _sourceType, value);
        }

        private string _sourceNo = string.Empty;
        /// <summary>
        /// 来源单号（SO号、样品单号等）
        /// </summary>
        public string SourceNo
        {
            get => _sourceNo;
            set => SetProperty(ref _sourceNo, value);
        }

        private Guid _sourceLineId;
        /// <summary>
        /// 来源单据行 ID
        /// </summary>
        public Guid SourceLineId
        {
            get => _sourceLineId;
            set => SetProperty(ref _sourceLineId, value);
        }
        #endregion

        #region 产品信息

        private string _sku = string.Empty;
        public string Sku
        {
            get => _sku;
            set => SetProperty(ref _sku, value);
        }

        private string _sourceSku;
        /// <summary>
        /// 非标件需要有一个来源的sku,比如客户的型号
        /// </summary>
        public string SourceSku
        {
            get => _sourceSku;
            set => SetProperty(ref _sourceSku, value);
        }
        private bool _isNonStandard;
        public bool IsNonStandard
        {
            get => _isNonStandard;
            set => SetProperty(ref _isNonStandard, value);
        }

        private string _description;
        /// <summary>
        /// 非标件描述或产品名称
        /// </summary>
        public string Description
        {
            get => _description;
            set => SetProperty(ref _description, value);
        }

        private string _colorName;
        /// <summary>
        /// 颜色名称
        /// </summary>
        public string ColorName
        {
            get => _colorName;
            set => SetProperty(ref _colorName, value);
        }

        private string _features;
        /// <summary>
        /// 规格型号（针对非标件尤为重要）
        /// </summary>
        public string Features
        {
            get => _features;
            set => SetProperty(ref _features, value);
        }
        private string _mainImage;
        public string MainImage
        {
            get => _mainImage;
            set => SetProperty(ref _mainImage, value);
        }

        #endregion

        #region 数量和状态

        private decimal _originalQty;
        /// <summary>
        /// 原始订单数量
        /// </summary>
        public decimal OriginalQty
        {
            get => _originalQty;
            set => SetProperty(ref _originalQty, value);
        }

        private decimal _filledQty;
        /// <summary>
        /// 已填充数量（已匹配采购或库存的数量）
        /// </summary>
        public decimal FilledQty
        {
            get => _filledQty;
            set => SetProperty(ref _filledQty, value);
        }

        private decimal _shippedQty;
        /// <summary>
        /// 已发货数量
        /// </summary>
        public decimal ShippedQty
        {
            get => _shippedQty;
            set => SetProperty(ref _shippedQty, value);
        }

        private bool _isKitted;
        /// <summary>
        /// 是否齐套
        /// </summary>
        public bool IsKitted
        {
            get => _isKitted;
            set => SetProperty(ref _isKitted, value);
        }

        private IndependentDemandStatusEnum _orderStatus = IndependentDemandStatusEnum.未确认;
        /// <summary>
        /// 内部执行状态 (0:待处理, 1:采购中, 2:已到货, 3:已完成)
        /// </summary>
        public IndependentDemandStatusEnum OrderStatus
        {
            get => _orderStatus;
            set => SetProperty(ref _orderStatus, value);
        }
        public string OrderStatusText => OrderStatus.ToString();

        #endregion

        #region 日期信息

        private DateTime? _hopefulDate;
        /// <summary>
        /// 期望交期
        /// </summary>
        public DateTime? HopefulDate
        {
            get => _hopefulDate;
            set => SetProperty(ref _hopefulDate, value);
        }

        private DateTime? _lastShippedDate;
        /// <summary>
        /// 最后发货日期
        /// </summary>
        public DateTime? LastShippedDate
        {
            get => _lastShippedDate;
            set => SetProperty(ref _lastShippedDate, value);
        }


        #endregion

        #region 其他属性

        private string _remark;
        /// <summary>
        /// 备注
        /// </summary>
        public string Remark
        {
            get => _remark;
            set => SetProperty(ref _remark, value);
        }

        private int _approvalStatus;
        public int ApprovalStatus
        {
            get => _approvalStatus;
            set => SetProperty(ref _approvalStatus, value);
        }

        private bool _isDecomposed;
        /// <summary>
        /// 是否已执行拆解（防重复拆解标记）。
        /// </summary>
        public bool IsDecomposed
        {
            get => _isDecomposed;
            set => SetProperty(ref _isDecomposed, value);
        }

        private DateTime? _decomposedTime;
        /// <summary>
        /// 最近一次拆解时间。
        /// </summary>
        public DateTime? DecomposedTime
        {
            get => _decomposedTime;
            set => SetProperty(ref _decomposedTime, value);
        }

        #endregion

    }
}
