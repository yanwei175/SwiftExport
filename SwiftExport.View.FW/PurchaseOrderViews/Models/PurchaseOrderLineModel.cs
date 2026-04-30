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

        private string _descriptionZh;
        public string DescriptionZh
        {
            get { return _descriptionZh; }
            set { SetProperty(ref _descriptionZh, value); }
        }

        private decimal _qty;
        public decimal QTY
        {
            get { return _qty; }
            set { SetProperty(ref _qty, value); }
        }

        private decimal _recQty;
        public decimal RecQty
        {
            get { return _recQty; }
            set { SetProperty(ref _recQty, value); }
        }

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

        private decimal? _unitWeight;
        public decimal? UnitWeight
        {
            get { return _unitWeight; }
            set { SetProperty(ref _unitWeight, value); }
        }

        private decimal? _unitCBM;
        public decimal? UnitCBM
        {
            get { return _unitCBM; }
            set { SetProperty(ref _unitCBM, value); }
        }

        private string _materialType;
        public string MaterialType
        {
            get { return _materialType; }
            set { SetProperty(ref _materialType, value); }
        }

        private long? _soLineId;
        public long? SoLineId
        {
            get { return _soLineId; }
            set { SetProperty(ref _soLineId, value); }
        }

        private string _supplyType;
        public string SupplyType
        {
            get { return _supplyType; }
            set { SetProperty(ref _supplyType, value); }
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

        /// <summary>
        /// 中文注释：从 DTO 加载，兼容 BaseListModel 的批量装载机制。
        /// </summary>
        public override void LoadFromDto<Tdto>(Tdto dto, bool 抑制清理脏数据 = false)
        {
            var lineDto = dto as PurchaseOrderLineDto;
            if (lineDto == null) return;

            using (程序内部修改Scope())
            {
                Id = lineDto.Id;
                OrderNo = lineDto.OrderNo;
                Sku = lineDto.Sku;
                DescriptionZh = lineDto.DescriptionZh;
                QTY = lineDto.QTY;
                RecQty = lineDto.RecQty;
                ReadyQty = lineDto.ReadyQty;
                FollowExpectedDate = lineDto.FollowExpectedDate;
                SubNote = lineDto.SubNote;
                UnitWeight = lineDto.UnitWeight;
                UnitCBM = lineDto.UnitCBM;
                MaterialType = lineDto.MaterialType;
                SupplyType = lineDto.SupplyType;
                SoLineId = lineDto.SoLineId;
                IsChecked = false;
            }

            if (!抑制清理脏数据)
            {
                CommitChanges();
            }
        }

        /// <summary>
        /// 中文注释：导出 DTO，用于批量保存。
        /// </summary>
        public PurchaseOrderLineDto ToDto()
        {
            return new PurchaseOrderLineDto
            {
                Id = Id,
                OrderNo = OrderNo,
                Sku = Sku,
                DescriptionZh = DescriptionZh,
                QTY = QTY,
                RecQty = RecQty,
                ReadyQty = ReadyQty,
                FollowExpectedDate = FollowExpectedDate,
                SubNote = SubNote,
                UnitWeight = UnitWeight,
                UnitCBM = UnitCBM,
                MaterialType = MaterialType,
                SupplyType = SupplyType,
                SoLineId = SoLineId
            };
        }
    }
}
