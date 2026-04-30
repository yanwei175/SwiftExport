using Mapster;
using SwiftExport.Contracts.DTOs.Orders;
using SwiftExport.View.FW.Mvvm;
using System;
using System.ComponentModel;
using System.Threading.Tasks;

namespace SwiftExport.View.FW.OrderDecomposeViews.Models
{
    /// <summary>
    /// 需求池行模型。
    /// </summary>
    public class RequirementPoolModel : BaseLineModel, IDataErrorInfo
    {
        /// <summary>
        /// 从 DTO 加载数据。
        /// </summary>
        public override void LoadFromDto<TDto>(TDto dto, bool 抑制清理脏数据 = false)
        {
            if (dto == null) return;

            using (程序内部修改Scope())
            {
                dto.Adapt(this);
                if (Id == 0)
                    MarkAsAdded();

                if (!抑制清理脏数据)
                    CommitChanges();
            }
        }

        private string _sourceNo;
        /// <summary>
        /// 来源单号（通用：SO/样品单等）。
        /// </summary>
        public string SourceNo
        {
            get => _sourceNo;
            set => SetProperty(ref _sourceNo, value);
        }

        private string _sourceType;
        /// <summary>
        /// 来源类型（如 SO/样品）。
        /// </summary>
        public string SourceType
        {
            get => _sourceType;
            set => SetProperty(ref _sourceType, value);
        }

        private long _sourceLineId;
        /// <summary>
        /// 来源行 ID。
        /// </summary>
        public long SourceLineId
        {
            get => _sourceLineId;
            set => SetProperty(ref _sourceLineId, value);
        }

        private string _sku;
        public string Sku
        {
            get => _sku;
            set => SetProperty(ref _sku, value);
        }

        private string _mainImage;
        /// <summary>
        /// 物料主图（用于任务清单图片列）。
        /// </summary>
        public string MainImage
        {
            get => _mainImage;
            set => SetProperty(ref _mainImage, value);
        }

        private string _supplyType;
        public string SupplyType
        {
            get => _supplyType;
            set => SetProperty(ref _supplyType, value);
        }

        private string _parentSku;
        /// <summary>
        /// 父级成品 SKU。
        /// </summary>
        public string ParentSku
        {
            get => _parentSku;
            set => SetProperty(ref _parentSku, value);
        }

        private int _bomLevel;
        /// <summary>
        /// BOM 层级，0 表示成品。
        /// </summary>
        public int BomLevel
        {
            get => _bomLevel;
            set => SetProperty(ref _bomLevel, value);
        }

        private DateTime? _requiredDate;
        public DateTime? RequiredDate
        {
            get => _requiredDate;
            set
            {
                if (SetProperty(ref _requiredDate, value))
                {
                    // 中文注释：交期变化时同步刷新紧急标记。
                    RaisePropertyChanged(nameof(IsRequiredDateUrgent));
                }
            }
        }

        private decimal _grossQty;
        public decimal GrossQty
        {
            get => _grossQty;
            set
            {
                if (SetProperty(ref _grossQty, value))
                {
                    // 中文注释：毛需求变化时同步刷新缺口值。
                    RaisePropertyChanged(nameof(GapQty));
                }
            }
        }

        private decimal _netQty;
        public decimal NetQty
        {
            get => _netQty;
            set => SetProperty(ref _netQty, value);
        }

        private decimal _executedQty;
        /// <summary>
        /// 已下达数量（采购/生产）。
        /// </summary>
        public decimal ExecutedQty
        {
            get => _executedQty;
            set => SetProperty(ref _executedQty, value);
        }

        private decimal _receivedQty;
        public decimal ReceivedQty
        {
            get => _receivedQty;
            set => SetProperty(ref _receivedQty, value);
        }

        private decimal _shippedQty;
        public decimal ShippedQty
        {
            get => _shippedQty;
            set => SetProperty(ref _shippedQty, value);
        }

        private string _targetNo;
        /// <summary>
        /// 目标执行单号（PO/MO）。
        /// </summary>
        public string TargetNo
        {
            get => _targetNo;
            set => SetProperty(ref _targetNo, value);
        }

        private long? _targetLineId;
        /// <summary>
        /// 目标执行行 ID。
        /// </summary>
        public long? TargetLineId
        {
            get => _targetLineId;
            set => SetProperty(ref _targetLineId, value);
        }

        private string _supplierCode;
        /// <summary>
        /// 供应商编码。
        /// </summary>
        public string SupplierCode
        {
            get => _supplierCode;
            set
            {
                if (SetProperty(ref _supplierCode, value))
                {
                    RaisePropertyChanged(nameof(IsSupplierMissing));
                }
            }
        }

        private string _supplierName;
        /// <summary>
        /// 供应商名称。
        /// </summary>
        public string SupplierName
        {
            get => _supplierName;
            set
            {
                if (SetProperty(ref _supplierName, value))
                {
                    // 中文注释：供应商变化时同步刷新“缺省供应商告警”状态。
                    RaisePropertyChanged(nameof(IsSupplierMissing));
                }
            }
        }

        private decimal _availableStockQty;
        /// <summary>
        /// 库存可用数（后端支持时回填）。
        /// </summary>
        public decimal AvailableStockQty
        {
            get => _availableStockQty;
            set
            {
                if (SetProperty(ref _availableStockQty, value))
                {
                    // 中文注释：库存变化时同步刷新缺口值。
                    RaisePropertyChanged(nameof(GapQty));
                }
            }
        }

        /// <summary>
        /// 缺口提醒（毛需求 - 库存可用数）。
        /// </summary>
        public decimal GapQty => GrossQty - AvailableStockQty;

        /// <summary>
        /// 是否缺少供应商（用于黄色高亮和警告图标）。
        /// </summary>
        public bool IsSupplierMissing => string.IsNullOrWhiteSpace(SupplierCode);

        /// <summary>
        /// 交期是否紧急（距离今天不足 3 天）。
        /// </summary>
        public bool IsRequiredDateUrgent
        {
            get
            {
                if (!RequiredDate.HasValue) return false;
                return (RequiredDate.Value.Date - DateTime.Today).TotalDays < 3;
            }
        }

        private int _statusFlag;
        public int StatusFlag
        {
            get => _statusFlag;
            set => SetProperty(ref _statusFlag, value);
        }

        private bool _isKitted;
        public bool IsKitted
        {
            get => _isKitted;
            set => SetProperty(ref _isKitted, value);
        }

        private string _remark;
        public string Remark
        {
            get => _remark;
            set => SetProperty(ref _remark, value);
        }

        private DateTime? _changeTime;
        public DateTime? ChangeTime
        {
            get => _changeTime;
            set => SetProperty(ref _changeTime, value);
        }

        private string _even;
        public string Even
        {
            get => _even;
            set => SetProperty(ref _even, value);
        }

        private string _status;
        public string Status
        {
            get => _status;
            set => SetProperty(ref _status, value);
        }

        public override string Error => null;

        public override string this[string columnName]
        {
            get
            {
                if (columnName == nameof(SourceNo) && string.IsNullOrWhiteSpace(SourceNo))
                    return "来源单号不能为空";

                if (columnName == nameof(Sku) && string.IsNullOrWhiteSpace(Sku))
                    return "SKU 不能为空";

                return null;
            }
        }

        public override Task<string> ValidateAsync() => Task.FromResult<string>(null);
    }
}
