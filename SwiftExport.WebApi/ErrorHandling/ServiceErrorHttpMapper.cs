using SwiftExport.Contracts.DTOs.SystemDtos.ExcelReadProfiles;
using SwiftExport.Contracts.DTOs.Shipping.PackingLists;
using SwiftExport.Contracts.DTOs.QualityControl;
using SwiftExport.Contracts.DTOs.Procurement.PurchaseDrafts;
using SwiftExport.Contracts.DTOs.Procurement.OrderManagement;
using SwiftExport.Contracts.DTOs.Inventory;
using SwiftExport.Contracts.DTOs.DemandPlanning.SourceDemandDelivery;
using SwiftExport.Contracts.DTOs.DemandPlanning;
using SwiftExport.Contracts.DTOs.Errors;
using SwiftExport.Contracts.DTOs.Files;

namespace SwiftExport.WebApi.ErrorHandling;

/// <summary>维护业务错误到 HTTP 状态的唯一映射；不根据提示文字或代码前缀猜测类别。</summary>
public static class ServiceErrorHttpMapper
{
    /// <summary>返回错误的 HTTP 状态；未登记代码按服务端契约错误处理。</summary>
    public static int GetStatusCode(string? code) => code switch
    {
        ServiceErrorCodes.Validation or ProcurementErrorCodes.SelectionInvalid or ServiceErrorCodes.InvalidCommand or PackingListErrorCodes.InvalidDraft or PurchaseDraftErrorCodes.InvalidItem
            or PackingListErrorCodes.InvalidLines or DemandPlanningErrorCodes.InvalidQuantity or DemandPlanningErrorCodes.InvalidRevision or ServiceErrorCodes.InvalidReason
            or DemandPlanningErrorCodes.InvalidSupplier or PurchaseDraftErrorCodes.InvalidTarget or PurchaseDraftErrorCodes.InvalidSource or PurchaseDraftErrorCodes.InvalidPreviewMode
            or PurchaseDraftErrorCodes.InvalidEntryMode or PurchaseDraftErrorCodes.ItemRequired or PurchaseDraftErrorCodes.SourceRequired or PurchaseDraftErrorCodes.CustomerRequired
            or PurchaseDraftErrorCodes.AppendTargetRequired or PurchaseDraftWarningCodes.DeliveryTargetRequired or PurchaseDraftErrorCodes.ExecutionRequestRequired
            or PurchaseDraftErrorCodes.InvalidPurchaseQuantity or PurchaseDraftErrorCodes.DuplicateItem or DemandPlanningErrorCodes.DuplicateSourceLine
            or PurchaseDraftErrorCodes.DuplicateSource or PurchaseDraftErrorCodes.DuplicateTarget or SourceDeliveryErrorCodes.SourceDeliveryQueryInvalid
            or SourceDeliveryErrorCodes.ClaimInvalid => 400,
        ServiceErrorCodes.Unauthenticated => 401,
        ServiceErrorCodes.Forbidden => 403,
        ServiceErrorCodes.NotFound or PurchaseDraftErrorCodes.MasterDataNotFound or PurchaseDraftErrorCodes.NetDemandNotFound
            or DemandPlanningErrorCodes.SourceNotFound or PurchaseDraftErrorCodes.SourceHeaderNotFound or SourceDeliveryErrorCodes.NotFound
            or DemandPlanningErrorCodes.ProductNotFound or DemandPlanningErrorCodes.BomNotFound or DemandPlanningErrorCodes.BomChildNotFound => 404,
        ServiceErrorCodes.VersionConflict or ServiceErrorCodes.CommandConflict or FileErrorCodes.Conflict
            or ExcelReadProfileErrorCodes.ExcelReadDuplicateName or ServiceErrorCodes.StateBlocked or ServiceErrorCodes.CommandBusy
            or PurchaseDraftErrorCodes.AppendConcurrencyConflict or ServiceErrorCodes.ConcurrencyConflict or DemandPlanningErrorCodes.RevisionConflict
            or DemandPlanningErrorCodes.SourceVersionConflict or DemandPlanningErrorCodes.StaleSourceVersion or SourceDeliveryErrorCodes.VersionConflict
            or ServiceErrorCodes.CommandReused or PurchaseDraftErrorCodes.IdempotencyKeyReused or PackingListErrorCodes.SourceChanged or PackingListErrorCodes.LineSetMismatch
            or PackingListErrorCodes.InvalidState or InventoryErrorCodes.DocumentStateInvalid or DemandPlanningErrorCodes.InvalidSourceLifecycle
            or PurchaseDraftErrorCodes.PreviewExpired or PurchaseDraftErrorCodes.PreviewFactChanged or PurchaseDraftErrorCodes.PreviewTokenMismatch or PackingListErrorCodes.PreviewStale
            or ProcurementErrorCodes.BomBasisChanged or PackingListErrorCodes.DemandStale or DemandPlanningErrorCodes.DuplicateActiveSource
            or SourceDeliveryErrorCodes.Inactive or DemandPlanningErrorCodes.MaterialDemandNotActive => 409,
        FileErrorCodes.UploadTooLarge => 413,
        FileErrorCodes.UnsupportedMediaType => 415,
        ServiceErrorCodes.BusinessBlocked or ProcurementErrorCodes.ProductNotMatched
            or ProcurementErrorCodes.ProductAmbiguous or ProcurementErrorCodes.ProductUnavailable
            or PackingListErrorCodes.SourceNotAssociated or PackingListErrorCodes.AmbiguousSource or PurchaseDraftErrorCodes.AppendTargetMismatch
            or PurchaseDraftErrorCodes.AppendTargetUnsafe or PackingListErrorCodes.BalanceInvalid or PackingListErrorCodes.ComponentInvalid or PackingListErrorCodes.ComponentShortfall
            or PackingListErrorCodes.CustomerInvalid or PackingListErrorCodes.CustomerMismatch or DemandPlanningErrorCodes.CustomerSuppliedRootNotAllowed
            or PackingListErrorCodes.DeliveryExceeded or PackingListErrorCodes.DemandAmbiguous or PackingListErrorCodes.DemandInvalid
            or PurchaseDraftWarningCodes.DifferentPlannedDeliveryDates or DemandPlanningErrorCodes.DownstreamExecutionExists or PackingListErrorCodes.DuplicateDemand
            or PackingListErrorCodes.ExecutionLinkInvalid or PurchaseDraftWarningCodes.ExecutionTypeNotPurchase or DemandPlanningErrorCodes.InvalidBomLine
            or DemandPlanningErrorCodes.InvalidCustomerSuppliedProduct or DemandPlanningErrorCodes.InvalidProductBom or InventoryErrorCodes.InventoryBlocked
            or PackingListErrorCodes.NegativeStock or DemandPlanningErrorCodes.NonStandardProductForbidden or PurchaseDraftErrorCodes.NothingToCreate
            or PurchaseDraftErrorCodes.OneClickMustCreateNew or PurchaseDraftErrorCodes.OneClickRequiresAllRemaining or PurchaseDraftErrorCodes.OneClickSingleSource
            or PurchaseDraftErrorCodes.PreviewBlocked or PackingListErrorCodes.ProductInvalid or InventoryErrorCodes.ProductUnitInvalid or DemandPlanningErrorCodes.ProductUnitRequired
            or DemandPlanningErrorCodes.ProductionCustomerSuppliedNotSupported or InventoryErrorCodes.PurchaseCapacityExceeded
            or PurchaseDraftWarningCodes.PurchaseDataIncomplete or InventoryErrorCodes.PurchaseNotReceivable or PurchaseDraftWarningCodes.PurchaseSupplierInvalid
            or PurchaseDraftWarningCodes.PurchaseSupplierRequired or PurchaseDraftWarningCodes.RelatedCustomerSuppliedRemaining
            or DemandPlanningErrorCodes.SourceChangeRequiresDisposition or SourceDeliveryErrorCodes.Exceeded or SourceDeliveryErrorCodes.SourceDeliveryExists
            or DemandPlanningErrorCodes.SourceDemandMismatch or DemandPlanningErrorCodes.SourceExitBlocked or PackingListErrorCodes.SourceInvalid or DemandPlanningErrorCodes.SourceTypeNotSupported
            or PurchaseDraftErrorCodes.SupplierProductDuplicate or InventoryErrorCodes.SupplierWarehouseInvalid or PackingListErrorCodes.SupplyShortfall
            or DemandPlanningErrorCodes.UnsubmittedActiveSource or DemandPlanningErrorCodes.UnsupportedSupplyType or InventoryErrorCodes.UnsupportedType
            or InventoryErrorCodes.WarehouseInvalid or PurchaseDraftErrorCodes.WarningConfirmationRequired => 422,
        ServiceErrorCodes.RateLimited => 429,
        ServiceErrorCodes.ServiceUnavailable or ServiceErrorCodes.OutcomeUnknown
            or ServiceErrorCodes.SchemaNotReady or SourceDeliveryErrorCodes.QueryUnavailable or AfterSalesErrorCodes.AftersalesDeliveryQueryUnavailable => 503,
        // 技术故障和未登记代码均由出口隐藏内部细节，保留日志用于定位。
        _ => 500
    };
}
