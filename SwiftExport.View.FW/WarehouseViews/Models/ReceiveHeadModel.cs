using Mapster;
using SwiftExport.Contracts.DTOs.Warehouses;
using SwiftExport.View.FW.Mvvm;
using System;

namespace SwiftExport.View.FW.WarehouseViews.Models
{
    /// <summary>
    /// 入库单头 UI 模型。
    /// 继承 BaseLineModel，统一支持状态机、脏数据跟踪与回滚提交。
    /// </summary>
    public class ReceiveHeadModel : BaseLineModel
    {

        private string _receiveNo;
        public string ReceiveNo
        {
            get => _receiveNo;
            set => SetProperty(ref _receiveNo, value);
        }

        private string _supplierCode;
        public string SupplierCode
        {
            get => _supplierCode;
            set => SetProperty(ref _supplierCode, value);
        }

        private DateTime? _receiveDate;
        public DateTime? ReceiveDate
        {
            get => _receiveDate;
            set => SetProperty(ref _receiveDate, value);
        }

        private string _handler;
        public string Handler
        {
            get => _handler;
            set => SetProperty(ref _handler, value);
        }

        private string _remark;
        public string Remark
        {
            get => _remark;
            set => SetProperty(ref _remark, value);
        }
        private bool _isPosted;
        public bool IsPosted
        {
            get => _isPosted;
            set => SetProperty(ref _isPosted, value);
        }

        private string _relatedPoNos;
        public string RelatedPoNos
        {
            get => _relatedPoNos;
            set => SetProperty(ref _relatedPoNos, value);
        }



        public override string ValidateRow()
        {
            if (string.IsNullOrWhiteSpace(ReceiveNo)) return "入库单号不能为空";
            if (string.IsNullOrWhiteSpace(SupplierCode)) return "供应商不能为空";
            return null;
        }


    }
}
