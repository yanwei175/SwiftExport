using Mapster;
using SwiftExport.Contracts.DTOs.Orders;
using SwiftExport.View.FW.Mvvm;
using System;

namespace SwiftExport.View.FW.PurchaseOrderViews.Models
{
    /// <summary>
    /// 采购订单行 UI 模型。
    /// 中文注释：支持 ReadyQty / FollowExpectedDate / SubNote 可编辑，其他字段用于只读展示与统计。
    /// </summary>
    public class PurchaseOrderLineModel : BaseLineModel
    {
        /// <summary>
        /// 中文注释：从 DTO 加载，兼容 BaseListModel 的批量装载机制。
        /// </summary>
        public override void LoadFromDto<Tdto>(Tdto dto, bool 抑制清理脏数据 = false)
        {
            if (!(dto is PurchaseOrderLineDto lineDto)) return;

            using (DisabledStateScope())
            {
                lineDto.Adapt(this);
                if (this.Id == 0)
                { this.MarkAsAdded(); }
                if (!抑制清理脏数据)
                {
                    CommitChanges();
                }
            }
        }

        private int _readyStatus;
        public int ReadyStatus
        {
            get { return _readyStatus; }
            set { SetProperty(ref _readyStatus, value); }
        }

        private string _even;
        public string Even
        {
            get { return _even; }
            set { SetProperty(ref _even, value); }
        }

        private string _status;
        public string Status
        {
            get { return _status; }
            set { SetProperty(ref _status, value); }
        }

        private int _lineNumber;
        public int LineNumber
        {
            get { return _lineNumber; }
            set { SetProperty(ref _lineNumber, value); }
        }

        private int? _purchaseOrderId;
        public int? PurchaseOrderId
        {
            get { return _purchaseOrderId; }
            set { SetProperty(ref _purchaseOrderId, value); }
        }

        private string _orderNo;
        public string OrderNo
        {
            get { return _orderNo; }
            set { SetProperty(ref _orderNo, value); }
        }

        private string _sku;
        public string Sku
        {
            get { return _sku; }
            set { SetProperty(ref _sku, value); }
        }

        private string _customerSku;
        public string CustomerSku
        {
            get { return _customerSku; }
            set { SetProperty(ref _customerSku, value); }
        }

        private string _linkSku;
        /// <summary>
        /// 对齐 DTO/实体字段名（LinkSku），兼容旧绑定别名 LinkedSku。
        /// </summary>
        public string LinkSku
        {
            get { return _linkSku; }
            set { SetProperty(ref _linkSku, value); }
        }

        private string _supplierSku;
        public string SupplierSku
        {
            get { return _supplierSku; }
            set { SetProperty(ref _supplierSku, value); }
        }

        private string _descriptionZh;
        public string DescriptionZh
        {
            get { return _descriptionZh; }
            set { SetProperty(ref _descriptionZh, value); }
        }

        private string _finishZh;
        public string FinishZh
        {
            get { return _finishZh; }
            set { SetProperty(ref _finishZh, value); }
        }

        private string _featuresZh;
        public string FeaturesZh
        {
            get { return _featuresZh; }
            set { SetProperty(ref _featuresZh, value); }
        }

        private decimal? _unitPrice;
        public decimal? UnitPrice
        {
            get { return _unitPrice; }
            set { SetProperty(ref _unitPrice, value); }
        }

        private decimal? _taxPrice;
        public decimal? TaxPrice
        {
            get { return _taxPrice; }
            set { SetProperty(ref _taxPrice, value); }
        }

        private decimal _qty;
        public decimal QTY
        {
            get { return _qty; }
            set { SetProperty(ref _qty, value); }
        }

        private decimal? _unitAmount;
        public decimal? UnitAmount
        {
            get { return _unitAmount; }
            set { SetProperty(ref _unitAmount, value); }
        }

        private decimal? _taxAmount;
        public decimal? TaxAmount
        {
            get { return _taxAmount; }
            set { SetProperty(ref _taxAmount, value); }
        }

        private decimal _recQty;
        public decimal RecQty
        {
            get { return _recQty; }
            set { SetProperty(ref _recQty, value); }
        }

        public decimal UnRecQty => QTY - RecQty;

        private decimal _readyQty;
        public decimal ReadyQty
        {
            get { return _readyQty; }
            set
            {
                if (value < 0) value = 0;
                SetProperty(ref _readyQty, value);
            }
        }
        private string _followUpRemarks;
        public string FollowUpRemarks
        { 
            get { return _followUpRemarks; }
            set { SetProperty(ref _followUpRemarks, value); }
        }


        private DateTime? _followExpectedDate;
        public DateTime? FollowExpectedDate
        {
            get { return _followExpectedDate; }
            set { SetProperty(ref _followExpectedDate, value); }
        }

        private string _subNote;
        public string SubNote
        {
            get { return _subNote; }
            set { SetProperty(ref _subNote, value); }
        }

        private string _materialType;
        public string MaterialType
        {
            get { return _materialType; }
            set { SetProperty(ref _materialType, value); }
        }

        private string _deliveryTo;
        public string DeliveryTo
        {
            get { return _deliveryTo; }
            set { SetProperty(ref _deliveryTo, value); }
        }

        private string _supplyType;
        public string SupplyType
        {
            get { return _supplyType; }
            set { SetProperty(ref _supplyType, value); }
        }

        private int? _demandId;
        public int? DemandId
        {
            get { return _demandId; }
            set { SetProperty(ref _demandId, value); }
        }

        private string _sourceType;
        public string SourceType
        {
            get { return _sourceType; }
            set { SetProperty(ref _sourceType, value); }
        }

        private string _sourceNo;
        public string SourceNo
        {
            get { return _sourceNo; }
            set { SetProperty(ref _sourceNo, value); }
        }

        private string _mainImage;
        public string MainImage
        {
            get { return _mainImage; }
            set { SetProperty(ref _mainImage, value); }
        }

        private DateTime? _lastRecDate;
        public DateTime? LastRecDate
        {
            get { return _lastRecDate; }
            set { SetProperty(ref _lastRecDate, value); }
        }

        private DateTime? _followUpTime;
        public DateTime? FollowUpTime
        {
            get { return _followUpTime; }
            set { SetProperty(ref _followUpTime, value); }
        }

        private DateTime? _changeTime;
        public DateTime? ChangeTime
        {
            get { return _changeTime; }
            set { SetProperty(ref _changeTime, value); }
        }
        public override string this[string columnName]
        {
            get
            {
                // 中文注释：ReadyQty 仅做非负校验，允许大于合同数用于业务灵活录入。
                if (columnName == nameof(ReadyQty) && ReadyQty < 0)
                {
                    return "完工数量不能小于 0";
                }
                return null;
            }
        }



    }
}
