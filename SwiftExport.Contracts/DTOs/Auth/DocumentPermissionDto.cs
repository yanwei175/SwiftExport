using System;
using System.Collections.Generic;
using System.Text;

namespace SwiftExport.Contracts.DTOs.Auth
{
    /// <summary>
    /// 未接入的旧文档权限预留模型；当前无 API、服务或客户端调用方，本阶段不得作为权限来源扩大使用。
    /// </summary>
    public class DocumentPermissionDto
    {
        // ====== 整单级操作权限（按钮权限） ======
        public bool CanCreate { get; set; }
        public bool CanEditHeader { get; set; }
        public bool CanEditLines { get; set; }
        public bool CanDelete { get; set; }
        public bool CanApprove { get; set; }
        public bool CanUnapprove { get; set; }
        public bool CanClose { get; set; }
        public bool CanReopen { get; set; }
        public bool CanPrint { get; set; }
        public bool CanExport { get; set; }

        // ====== 行级字段权限（适用于所有单据） ======
        public bool CanEditQty { get; set; }
        public bool CanEditPrice { get; set; }
        public bool CanEditAmount { get; set; }
        public bool CanEditRemark { get; set; }
        public bool CanEditSku { get; set; }
        public bool CanEditCustomerSku { get; set; }

        // ====== 发货相关字段（订单页面永远只读） ======
        public bool CanEditShippedQty { get; set; } = false;
        public bool CanEditUnshippedQty { get; set; } = false;

        // ====== 收货相关字段（采购订单页面永远只读） ======
        public bool CanEditReceivedQty { get; set; } = false;
        public bool CanEditUnreceivedQty { get; set; } = false;

        // ====== 发货单页面专用 ======
        public bool CanEditShipmentQty { get; set; }
        public bool CanEditShipmentRemark { get; set; }

        // ====== 收货单页面专用 ======
        public bool CanEditReceiptQty { get; set; }
        public bool CanEditReceiptRemark { get; set; }

        // ====== 盘点单页面专用 ======
        public bool CanEditCountQty { get; set; }

        // ====== 通用扩展字段（未来加字段不用改前端） ======
        public Dictionary<string, bool> ExtraFieldPermissions { get; set; } = new Dictionary<string, bool>();
    }

}
