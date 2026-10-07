using SwiftExport.Contracts.SysEnums;
using System;
using System.Collections.Generic;

namespace SwiftExport.Contracts.DTOs.Procurement.PurchaseDrafts
{
/// <summary>转采购预览和创建使用的稳定警告代码。</summary>
    public static class PurchaseDraftWarningCodes
    {
        /// <summary>采购数量校验的兼容成员；预览警告和创建失败引用同一规则代码。</summary>
        /// <summary>采购数量校验的兼容成员；预览警告和创建失败引用同一规则代码。</summary>
        public const string InvalidPurchaseQuantity = PurchaseDraftErrorCodes.InvalidPurchaseQuantity;
        public const string PurchaseSupplierRequired = "PURCHASE_SUPPLIER_REQUIRED";
        public const string PurchaseSupplierInvalid = "PURCHASE_SUPPLIER_INVALID";
        public const string DeliveryTargetRequired = "DELIVERY_TARGET_REQUIRED";
        public const string CorruptedPlanningState = "CORRUPTED_PLANNING_STATE";
        public const string PurchaseDataIncomplete = "PURCHASE_DATA_INCOMPLETE";
        public const string DifferentPlannedDeliveryDates = "DIFFERENT_PLANNED_DELIVERY_DATES";
        public const string RelatedCustomerSuppliedRemaining = "RELATED_CUSTOMER_SUPPLIED_REMAINING";
        public const string ExecutionTypeNotPurchase = "EXECUTION_TYPE_NOT_PURCHASE";
    }
}

