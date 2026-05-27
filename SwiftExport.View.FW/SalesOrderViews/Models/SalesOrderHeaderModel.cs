using Mapster;
using MS.WindowsAPICodePack.Internal;
using SwiftExport.Contracts.DTOs.SalesOrders;
using SwiftExport.View.FW.Mvvm;
using System;
using System.ComponentModel;

namespace SwiftExport.View.FW.SalesOrderViews.Models
{
    /// <summary>
    /// 销售订单头（主表）业务模型
    /// 具备：
    /// 1. 状态机（Added/Modified/Deleted/Unchanged）
    /// 2. 原始值快照（ChangeTracker）
    /// 3. CommitChanges / RollbackChanges
    /// 4. 字段级验证（IDataErrorInfo）
    /// </summary>
    public class SalesOrderHeaderModel : BaseLineModel, IDataErrorInfo
    {
        public bool IsRmbCurrency => Currency == "CNY" || Currency == "RMB"; //提供一个给UI判断是否显示某些字段的属性
        public override void LoadFromDto<SalesOrderHeaderDto>(SalesOrderHeaderDto dto, bool 抑制清理脏数据 = false) //加载数据方法
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

        /// <summary>
        /// 明细行变化时调用，更新汇总字段（TotalQty、总金额等）
        /// 这里使用 程序内部修改Scope 包裹，避免“行变化导致的汇总回写”把 Header 误标成用户修改。
        /// </summary>
        public void UpdateSummaryFromLines(decimal totalQty, int lineCount, decimal totalAmountOriginalCurrency, decimal totalAmountLocalCurrency)
        {
            using (程序内部修改Scope())
            {
                TotalQty = totalQty;
                LineCount = lineCount;
                TotalAmountOriginalCurrency = totalAmountOriginalCurrency;
                TotalAmountLocalCurrency = totalAmountLocalCurrency;
            }
        }
        // ============================================================
        // 基本信息
        // ============================================================
        #region 基本信息

        private int _orderStatus;
        /// <summary>
        /// OrderStatus（未发货/部分发货/已发货）
        /// </summary>
        public int OrderStatus
        {
            get => _orderStatus;
            set => SetProperty(ref _orderStatus, value);
        }

        private string _orderNo;
        /// <summary>
        /// OrderNo（系统生成或手工录入）
        /// </summary>
        public string OrderNo
        {
            get => _orderNo;
            set => SetProperty(ref _orderNo, value);
        }

        private int _customerId;
        /// <summary>
        /// 客户 ID（外键）
        /// </summary>
        public int CustomerId
        {
            get => _customerId;
            set => SetProperty(ref _customerId, value);
        }

        private string _customerCode;
        /// <summary>
        /// CustomerCode（显示用）
        /// </summary>
        public string CustomerCode
        {
            get => _customerCode;
            set => SetProperty(ref _customerCode, value);
        }

        private string _customerPoNo;
        /// <summary>
        /// 客户 PO 号（客户下单编号）
        /// </summary>
        public string CustomerPoNo
        {
            get => _customerPoNo;
            set => SetProperty(ref _customerPoNo, value);
        }

        private string _etdSortRemark;
        /// <summary>
        /// ETD备注,用于不写日期的字符串式的告知客户时间
        /// </summary>
        public string EtdSortRemark
        {
            get => _etdSortRemark;
            set => SetProperty(ref _etdSortRemark, value);
        }


        private string _shippedBy;
        /// <summary>
        /// 承运方式（如 DHL / UPS / 海运）
        /// </summary>
        public string ShippedBy
        {
            get => _shippedBy;
            set => SetProperty(ref _shippedBy, value);
        }


        #endregion


        // ============================================================
        // 金额与汇率
        // ============================================================
        #region 金额与汇率

        private string _currency;
        /// <summary>
        /// 币种（USD / RMB / EUR 等）
        /// </summary>
        public string Currency
        {
            get => _currency;
            set
            {
                if (SetProperty(ref _currency, value))
                {
                    RaisePropertyChanged(nameof(IsRmbCurrency));
                }
            }
        }


        private decimal _exchangeRate;
        /// <summary>
        /// ExchangeRate（用于本币金额计算）
        /// </summary>
        public decimal ExchangeRate
        {
            get => _exchangeRate;
            set => SetProperty(ref _exchangeRate, value);
        }

        private decimal _totalAmountOriginalCurrency;
        /// <summary>
        /// 原币总金额（由行集合自动计算）
        /// </summary>
        public decimal TotalAmountOriginalCurrency
        {
            get => _totalAmountOriginalCurrency;
            set => SetProperty(ref _totalAmountOriginalCurrency, value);
        }

        private decimal _totalAmountLocalCurrency;
        /// <summary>
        /// 本币总金额（原币金额 * ExchangeRate）
        /// </summary>
        public decimal TotalAmountLocalCurrency
        {
            get => _totalAmountLocalCurrency;
            set => SetProperty(ref _totalAmountLocalCurrency, value);
        }

        private string _invoiceNo;
        /// <summary>
        /// 发票号（如有）
        /// </summary>
        public string InvoiceNo
        {
            get => _invoiceNo;
            set => SetProperty(ref _invoiceNo, value);
        }

        #endregion

        // ============================================================
        // 汇总冗余字段（UI 可绑定，自动计算）
        // ============================================================
        #region 汇总冗余字段
        /// <summary>
        /// 订单总数量（由行集合自动计算）
        /// </summary>
        /// <remarks>
        /// 该属性由行集合自动计算得出，无需手动设置。
        /// </remarks>
        private decimal _totalQty;
        public decimal TotalQty
        {
            get => _totalQty;
            set => SetProperty(ref _totalQty, value);
        }
        /// <summary>
        /// LineCount(由行集合自动计算)
        /// </summary>
        private int _lineCount;
        public int LineCount
        {
            get => _lineCount;
            set => SetProperty(ref _lineCount, value);
        }
        /// <summary>
        /// 订单总发货数量（由行集合自动计算）
        /// </summary>
        private decimal _totalShippedQty;
        public decimal TotalShippedQty
        {
            get => _totalShippedQty;
            set => SetProperty(ref _totalShippedQty, value);
        }

        /// <summary>
        /// 订单未发货数量（由行集合自动计算）
        /// </summary>
        private decimal _totalUnshippedQty;
        public decimal TotalUnshippedQty
        {
            get => _totalUnshippedQty;
            set => SetProperty(ref _totalUnshippedQty, value);
        }

        /// <summary>
        /// 订单部分发货数量（由行集合自动计算）
        /// </summary>

        private decimal _totalPartiallyShippedQty;
        public decimal TotalPartiallyShippedQty
        {
            get => _totalPartiallyShippedQty;
            set => SetProperty(ref _totalPartiallyShippedQty, value);
        }

        #endregion



        // ============================================================
        // 收货信息
        // ============================================================
        #region 收货信息

        private string _shipToName;
        /// <summary>
        /// 收货人名称
        /// </summary>
        public string ShipToName
        {
            get => _shipToName;
            set => SetProperty(ref _shipToName, value);
        }

        private string _shipToAddress;
        /// <summary>
        /// 收货地址
        /// </summary>
        public string ShipToAddress
        {
            get => _shipToAddress;
            set => SetProperty(ref _shipToAddress, value);
        }

        private string _shipToPhone;
        /// <summary>
        /// 收货电话
        /// </summary>
        public string ShipToPhone
        {
            get => _shipToPhone;
            set => SetProperty(ref _shipToPhone, value);
        }

        #endregion


        // ============================================================
        // Incoterm（_incotermSortRemark）
        // ============================================================
        #region 贸易条款

        private string _incotermSortRemark;
        /// <summary>
        /// 有些人为了图简单,就会直接写成一行. 如:FOB Jiangmen,30% Deposit, Balance before shipment for orders more than 15,000 USD, otherwise EX Works
        /// 用于定义买卖双方的责任划分。
        /// </summary>
        public string IncotermSortRemark
        {
            get => _incotermSortRemark;
            set => SetProperty(ref _incotermSortRemark, value);
        }

        private string _incoterm;
        /// <summary>
        /// 国际贸易术语（如 EXW / FOB / CIF / DAP / DDP）
        /// 用于定义买卖双方的责任划分。
        /// </summary>
        public string Incoterm
        {
            get => _incoterm;
            set => SetProperty(ref _incoterm, value);
        }

        private string _incotermLocation;
        /// <summary>
        /// 贸易条款地点，例如：
        /// FOB Shenzhen → 地点为 Shenzhen
        /// CIF Los Angeles → 地点为 Los Angeles
        /// </summary>
        public string Incotermlocation
        {
            get => _incotermLocation;
            set => SetProperty(ref _incotermLocation, value);
        }

        private decimal _depositRate;
        /// <summary>
        /// DepositRate（如 30%）
        /// </summary>
        public decimal DepositRate
        {
            get => _depositRate;
            set => SetProperty(ref _depositRate, value);
        }

        private int _leadTimeDays;
        /// <summary>
        /// 生产周期（天数）
        /// </summary>
        public int LeadTimeDays
        {
            get => _leadTimeDays;
            set => SetProperty(ref _leadTimeDays, value);
        }

        private string _paymentMethod;
        /// <summary>
        /// 付款方式（如 TT / LC / OA）
        /// </summary>
        public string PaymentMethod
        {
            get => _paymentMethod;
            set => SetProperty(ref _paymentMethod, value);
        }

        #endregion


        // ============================================================
        // 日期字段
        // ============================================================
        #region 日期字段

        private DateTime? _orderDate;
        /// <summary>
        /// 订单日期
        /// </summary>
        public DateTime? OrderDate
        {
            get => _orderDate;
            set => SetProperty(ref _orderDate, value);
        }

        private DateTime? _deliveryDate;
        /// <summary>
        /// 交期（预计发货日期）
        /// </summary>
        public DateTime? DeliverDate
        {
            get => _deliveryDate;
            set => SetProperty(ref _deliveryDate, value);
        }

        private DateTime? _replyDate;
        /// <summary>
        /// 回复客户的日期（如确认交期）
        /// </summary>
        public DateTime? ReplyDate
        {
            get => _replyDate;
            set => SetProperty(ref _replyDate, value);
        }

        private DateTime? _eta;
        /// <summary>
        /// ETA（预计到达时间）
        /// </summary>
        public DateTime? ETA
        {
            get => _eta;
            set => SetProperty(ref _eta, value);
        }

        private DateTime? _etd;
        /// <summary>
        /// ETD（预计离港时间）
        /// </summary>
        public DateTime? ETD
        {
            get => _etd;
            set => SetProperty(ref _etd, value);
        }

        #endregion


        // ============================================================
        // SubNote
        // ============================================================
        #region 备注

        private string _soFolder;
        /// <summary>
        /// 订单文件夹路径（如存放附件）
        /// </summary>
        public string SoFolder
        {
            get => _soFolder;
            set => SetProperty(ref _soFolder, value);
        }

        private string _remark;
        /// <summary>
        /// SubNote
        /// </summary>
        public string Remark
        {
            get => _remark;
            set => SetProperty(ref _remark, value);
        }

        #endregion


        // ============================================================
        // 系统字段（UI 不显示）
        // ============================================================
        #region 系统字段

        private string _versionNo;
        /// <summary>
        /// VersionNo（用于并发控制）
        /// </summary>
        public string VersionNo
        {
            get => _versionNo;
            set => SetProperty(ref _versionNo, value);
        }

        private int _approvalStatus;
        /// <summary>
        /// ApprovalStatusEnum（未审批/已审批/驳回）
        /// </summary>
        public int ApprovalStatus
        {
            get => _approvalStatus;
            set => SetProperty(ref _approvalStatus, value);
        }

        /// <summary>
        /// 是否删除（软删除）
        /// </summary>
        public bool IsDeleted { get; set; }

        /// <summary>
        /// 是否锁定（防止并发编辑）
        /// </summary>
        public bool IsLocked { get; set; }

        public string CreatedBy { get; set; }
        public string UpdatedBy { get; set; }

        public DateTime? CreatedDate { get; set; }
        public DateTime? UpdatedDate { get; set; }

        #endregion






        // ============================================================
        // 验证（IDataErrorInfo）
        // ============================================================
        #region 验证

        public override string Error => null;// 告诉 WPF：行级别没有整体错误，不要动整行的样式,必须返回 null，避免整行标红。如果要跨行验证，请使用 ValidateRow() 方法。

        public override string this[string columnName]
        {
            get
            {
                switch (columnName)
                {
                    case nameof(OrderNo):
                        if (string.IsNullOrWhiteSpace(OrderNo))
                            return "订单编号不能为空";
                        break;
                    case nameof(CustomerCode):
                        if (string.IsNullOrWhiteSpace(CustomerCode))
                            return "客户不能为空";
                        break;
                    case nameof(InvoiceNo):
                        if (string.IsNullOrWhiteSpace(InvoiceNo))
                            return "发票号不能为空";
                        break;

                }
                return null;
            }
        }

        #endregion


        // ============================================================
        // 状态管理（提交/撤销）
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
