using Mapster;
using SwiftExport.View.FW.Mvvm;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SwiftExport.View.FW.PackingListViews.Models
{
    /// <summary>
    /// 装箱单表头前端模型。
    /// 中文注释：属性结构与 PackingListHeadDto 对齐，便于前后端字段一一映射。
    /// </summary>
    public class PackingListHeadModel : BaseLineModel
    {
        /// <summary>
        /// 中文注释：DTO 装载入口，当前类尚未实现映射逻辑。
        /// </summary>
        public override void LoadFromDto<Tdto>(Tdto dto, bool 抑制清理脏数据 = false)
        {
            if (dto == null) return;

            using (DisabledStateScope())
            {
                dto.Adapt(this);//使用 Mapster 将 DTO 的属性值映射到 Model(this) 上
                if (this.Id == 0)
                { this.MarkAsAdded(); }

                // 根据调用方需要决定是否立即提交/清理脏数据
                if (!抑制清理脏数据)
                    CommitChanges();
            }
        }

        #region 属性

        /// <summary>装箱单号</summary>
        private string _packingListNo;
        public string PackingListNo
        {
            get => _packingListNo;
            set => SetProperty(ref _packingListNo, value);
        }

        /// <summary>客户编码</summary>
        private string _customerCode;
        public string CustomerCode
        {
            get => _customerCode;
            set => SetProperty(ref _customerCode, value);
        }

        /// <summary>
        /// 这个发票号是为了应付老板的特殊需求,千万不要用这个发票号来执行业务逻辑
        /// </summary>
        private string _invoiceNo;
        public string InvoiceNo
        {
            get => _invoiceNo;
            set => SetProperty(ref _invoiceNo, value);
        }
        private DateTime? _invoiceDate;
        public DateTime? InvoiceDate
        {
            get => _invoiceDate;
            set => SetProperty(ref _invoiceDate, value);
        }

        /// <summary>收货人名称</summary>
        private string _shipToName;
        public string ShipToName
        {
            get => _shipToName;
            set => SetProperty(ref _shipToName, value);
        }

        /// <summary>收货地址</summary>
        private string _shipToAddress;
        public string ShipToAddress
        {
            get => _shipToAddress;
            set => SetProperty(ref _shipToAddress, value);
        }

        /// <summary>收货电话</summary>
        private string _shipToPhone;
        public string ShipToPhone
        {
            get => _shipToPhone;
            set => SetProperty(ref _shipToPhone, value);
        }

        /// <summary>联系人</summary>
        private string _attention;
        public string Attention
        {
            get => _attention;
            set => SetProperty(ref _attention, value);
        }

        /// <summary>柜号</summary>
        private string _containerNo;
        public string ContainerNo
        {
            get => _containerNo;
            set => SetProperty(ref _containerNo, value);
        }

        /// <summary>封条号</summary>
        private string _sealNo;
        public string SealNo
        {
            get => _sealNo;
            set => SetProperty(ref _sealNo, value);
        }

        /// <summary>预计发货日</summary>
        private DateTime? _etd;
        public DateTime? ETD
        {
            get => _etd;
            set => SetProperty(ref _etd, value);
        }

        /// <summary>预计抵达日</summary>
        private DateTime? _eta;
        public DateTime? ETA
        {
            get => _eta;
            set => SetProperty(ref _eta, value);
        }

        /// <summary>是否过账（触发入库/回写的开关）</summary>
        private bool _isPosted;
        public bool IsPosted
        {
            get => _isPosted;
            set => SetProperty(ref _isPosted, value);
        }

        /// <summary>过账日期</summary>
        private DateTime? _postedDate;
        public DateTime? PostedDate
        {
            get => _postedDate;
            set => SetProperty(ref _postedDate, value);
        }

        /// <summary>过账人</summary>
        private string _postedBy;
        public string PostedBy
        {
            get => _postedBy;
            set => SetProperty(ref _postedBy, value);
        }

        /// <summary>装箱日期</summary>
        private DateTime? _packingDate;
        public DateTime? PackingDate
        {
            get => _packingDate;
            set => SetProperty(ref _packingDate, value);
        }

        /// <summary>备注</summary>
        private string _remark;
        public string Remark
        {
            get => _remark;
            set => SetProperty(ref _remark, value);
        }

  


        #endregion
    }
}
