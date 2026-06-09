using SwiftExport.View.FW.Mvvm;
using System;

namespace SwiftExport.View.FW.PurchaseOrderViews.Models
{
    /// <summary>
    /// 虚拟入库记录模型。
    /// 中文注释：当前阶段用于 Tab 展示占位，后续可替换为真实入库 API。
    /// </summary>
    public class PurchaseReceiptRecordModel : BaseLineModel
    {
        private string _receiptNo;
        public string ReceiptNo
        {
            get { return _receiptNo; }
            set { SetProperty(ref _receiptNo, value); }
        }

        private DateTime? _receiptDate;
        public DateTime? ReceiptDate
        {
            get { return _receiptDate; }
            set { SetProperty(ref _receiptDate, value); }
        }

        private string _sku;
        public string Sku
        {
            get { return _sku; }
            set { SetProperty(ref _sku, value); }
        }

        private decimal _qty;
        public decimal Qty
        {
            get { return _qty; }
            set { SetProperty(ref _qty, value); }
        }

        private string _remark;
        public string Remark
        {
            get { return _remark; }
            set { SetProperty(ref _remark, value); }
        }
    }
}
