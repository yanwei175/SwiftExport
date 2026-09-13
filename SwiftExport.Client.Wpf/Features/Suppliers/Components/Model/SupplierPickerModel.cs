using System;
using CommunityToolkit.Mvvm.ComponentModel;

namespace SwiftExportClient.Features.Suppliers.Components.Model
{
    /// <summary>供应商选择弹层的视图行模型，保留展示字段并携带前端勾选态。</summary>
    public sealed partial class SupplierPickerModel : ObservableObject
    {
        /// <summary>供应商主键。</summary>
        public Guid Uid { get; set; }

        /// <summary>并发版本号。</summary>
        public int Version { get; set; }

        /// <summary>是否已删除。</summary>
        public bool IsDeleted { get; set; }

        /// <summary>创建时间。</summary>
        public DateTime? CreatedDate { get; set; }

        /// <summary>创建人。</summary>
        public string CreatedBy { get; set; } = string.Empty;

        /// <summary>更新时间。</summary>
        public DateTime? UpdatedDate { get; set; }

        /// <summary>更新人。</summary>
        public string UpdatedBy { get; set; } = string.Empty;

        /// <summary>供应商代码。</summary>
        public string SupplierCode { get; set; } = string.Empty;

        /// <summary>供应商名称。</summary>
        public string SupplierName { get; set; } = string.Empty;

        /// <summary>折扣。</summary>
        public decimal? ZheKou { get; set; }

        /// <summary>折扣备注。</summary>
        public string ZheKouBeiZhu { get; set; } = string.Empty;

        /// <summary>单据关联号。</summary>
        public string DocRelateNo { get; set; } = string.Empty;

        /// <summary>联系人。</summary>
        public string ContactPerson { get; set; } = string.Empty;

        /// <summary>联系人邮箱。</summary>
        public string ContactEmail { get; set; } = string.Empty;

        /// <summary>联系电话。</summary>
        public string ContactPhone { get; set; } = string.Empty;

        /// <summary>供应商地址。</summary>
        public string Address { get; set; } = string.Empty;

        /// <summary>币种。</summary>
        public string Currency { get; set; } = string.Empty;

        /// <summary>税率。</summary>
        public decimal? TaxRate { get; set; }

        /// <summary>付款条件。</summary>
        public string PaymentTerms { get; set; } = string.Empty;

        /// <summary>默认交期。</summary>
        public int? DefaultLeadTime { get; set; }

        /// <summary>列表勾选态，支持 UI 双向绑定通知。</summary>
        [ObservableProperty]
        public partial bool IsChecked { get; set; }
    }
}
