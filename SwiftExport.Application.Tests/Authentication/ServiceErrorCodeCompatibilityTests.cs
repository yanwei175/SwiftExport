using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;
using SwiftExport.Contracts.DTOs.Errors;
using SwiftExport.Contracts.DTOs.ForExcel.PurchaseOrders;
using SwiftExport.Contracts.DTOs.Shipping.PackingLists;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Application.Services.Procurement.PurchaseOrders.Draft;
using SwiftExport.Application.Services.Procurement.PurchaseOrders.Support;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.WebApi.ErrorHandling;

namespace SwiftExport.Application.Tests.Authentication;

/// <summary>锁定共享错误码的对外字符串和 HTTP 分类，覆盖已证实的遗漏出口。</summary>
[TestClass]
public sealed class ServiceErrorCodeCompatibilityTests
{
    /// <summary>只有实际进入服务失败出口的业务拒绝补分类，正常响应中的问题集合不据此改写状态。</summary>
    [TestMethod]
    [DataRow("EXCEL_READ_DUPLICATE_NAME", 409)]
    [DataRow("SelectionInvalid", 400)]
    [DataRow("ProductNotMatched", 422)]
    [DataRow("ProductAmbiguous", 422)]
    [DataRow("ProductUnavailable", 422)]
    [DataRow("SOURCE_NOT_ASSOCIATED", 422)]
    public void ConfirmedServiceFailures_HaveExpectedHttpStatus(string code, int expected)
    {
        Assert.AreEqual(expected, ServiceErrorHttpMapper.GetStatusCode(code), code);
    }

    /// <summary>保留原始字面量快照，防止实现和断言同时改用常量而掩盖对外值变化。</summary>
    [TestMethod]
    [DataRow("SwiftExport.Contracts.DTOs.DemandPlanning.DemandPlanningErrorCodes", "BomChildNotFound", "BOM_CHILD_NOT_FOUND")]
    [DataRow("SwiftExport.Contracts.DTOs.DemandPlanning.DemandPlanningErrorCodes", "BomNotFound", "BOM_NOT_FOUND")]
    [DataRow("SwiftExport.Contracts.DTOs.DemandPlanning.DemandPlanningErrorCodes", "CustomerSuppliedRootNotAllowed", "CUSTOMER_SUPPLIED_ROOT_NOT_ALLOWED")]
    [DataRow("SwiftExport.Contracts.DTOs.DemandPlanning.DemandPlanningErrorCodes", "DownstreamExecutionExists", "DOWNSTREAM_EXECUTION_EXISTS")]
    [DataRow("SwiftExport.Contracts.DTOs.DemandPlanning.DemandPlanningErrorCodes", "DuplicateActiveSource", "DUPLICATE_ACTIVE_SOURCE")]
    [DataRow("SwiftExport.Contracts.DTOs.DemandPlanning.DemandPlanningErrorCodes", "DuplicateSourceLine", "DUPLICATE_SOURCE_LINE")]
    [DataRow("SwiftExport.Contracts.DTOs.DemandPlanning.DemandPlanningErrorCodes", "InvalidBomLine", "INVALID_BOM_LINE")]
    [DataRow("SwiftExport.Contracts.DTOs.DemandPlanning.DemandPlanningErrorCodes", "InvalidCustomerSuppliedProduct", "INVALID_CUSTOMER_SUPPLIED_PRODUCT")]
    [DataRow("SwiftExport.Contracts.DTOs.DemandPlanning.DemandPlanningErrorCodes", "InvalidProductBom", "INVALID_PRODUCT_BOM")]
    [DataRow("SwiftExport.Contracts.DTOs.DemandPlanning.DemandPlanningErrorCodes", "InvalidQuantity", "INVALID_QUANTITY")]
    [DataRow("SwiftExport.Contracts.DTOs.DemandPlanning.DemandPlanningErrorCodes", "InvalidRevision", "INVALID_REVISION")]
    [DataRow("SwiftExport.Contracts.DTOs.DemandPlanning.DemandPlanningErrorCodes", "InvalidSourceLifecycle", "INVALID_SOURCE_LIFECYCLE")]
    [DataRow("SwiftExport.Contracts.DTOs.DemandPlanning.DemandPlanningErrorCodes", "InvalidSupplier", "INVALID_SUPPLIER")]
    [DataRow("SwiftExport.Contracts.DTOs.DemandPlanning.DemandPlanningErrorCodes", "MaterialDemandNotActive", "MATERIAL_DEMAND_NOT_ACTIVE")]
    [DataRow("SwiftExport.Contracts.DTOs.DemandPlanning.DemandPlanningErrorCodes", "NonStandardProductForbidden", "NON_STANDARD_PRODUCT_FORBIDDEN")]
    [DataRow("SwiftExport.Contracts.DTOs.DemandPlanning.DemandPlanningErrorCodes", "ProductNotFound", "PRODUCT_NOT_FOUND")]
    [DataRow("SwiftExport.Contracts.DTOs.DemandPlanning.DemandPlanningErrorCodes", "ProductUnitRequired", "PRODUCT_UNIT_REQUIRED")]
    [DataRow("SwiftExport.Contracts.DTOs.DemandPlanning.DemandPlanningErrorCodes", "ProductionCustomerSuppliedNotSupported", "PRODUCTION_CUSTOMER_SUPPLIED_NOT_SUPPORTED")]
    [DataRow("SwiftExport.Contracts.DTOs.DemandPlanning.DemandPlanningErrorCodes", "RevisionConflict", "REVISION_CONFLICT")]
    [DataRow("SwiftExport.Contracts.DTOs.DemandPlanning.DemandPlanningErrorCodes", "SourceChangeRequiresDisposition", "SOURCE_CHANGE_REQUIRES_DISPOSITION")]
    [DataRow("SwiftExport.Contracts.DTOs.DemandPlanning.DemandPlanningErrorCodes", "SourceChanged", "SOURCE_CHANGED")]
    [DataRow("SwiftExport.Contracts.DTOs.DemandPlanning.DemandPlanningErrorCodes", "SourceDemandMismatch", "SOURCE_DEMAND_MISMATCH")]
    [DataRow("SwiftExport.Contracts.DTOs.DemandPlanning.DemandPlanningErrorCodes", "SourceExitBlocked", "SOURCE_EXIT_BLOCKED")]
    [DataRow("SwiftExport.Contracts.DTOs.DemandPlanning.DemandPlanningErrorCodes", "SourceHistoryInvalid", "SOURCE_HISTORY_INVALID")]
    [DataRow("SwiftExport.Contracts.DTOs.DemandPlanning.DemandPlanningErrorCodes", "SourceNotFound", "SOURCE_NOT_FOUND")]
    [DataRow("SwiftExport.Contracts.DTOs.DemandPlanning.DemandPlanningErrorCodes", "SourceTypeNotSupported", "SOURCE_TYPE_NOT_SUPPORTED")]
    [DataRow("SwiftExport.Contracts.DTOs.DemandPlanning.DemandPlanningErrorCodes", "SourceVersionConflict", "SOURCE_VERSION_CONFLICT")]
    [DataRow("SwiftExport.Contracts.DTOs.DemandPlanning.DemandPlanningErrorCodes", "SourceVersionMissing", "SOURCE_VERSION_MISSING")]
    [DataRow("SwiftExport.Contracts.DTOs.DemandPlanning.DemandPlanningErrorCodes", "StaleSourceVersion", "STALE_SOURCE_VERSION")]
    [DataRow("SwiftExport.Contracts.DTOs.DemandPlanning.DemandPlanningErrorCodes", "UnsubmittedActiveSource", "UNSUBMITTED_ACTIVE_SOURCE")]
    [DataRow("SwiftExport.Contracts.DTOs.DemandPlanning.DemandPlanningErrorCodes", "UnsupportedSupplyType", "UNSUPPORTED_SUPPLY_TYPE")]
    [DataRow("SwiftExport.Contracts.DTOs.DemandPlanning.SourceDemandDelivery.SourceDeliveryErrorCodes", "ClaimInvalid", "SOURCE_DELIVERY_CLAIM_INVALID")]
    [DataRow("SwiftExport.Contracts.DTOs.DemandPlanning.SourceDemandDelivery.SourceDeliveryErrorCodes", "Exceeded", "SOURCE_DELIVERY_EXCEEDED")]
    [DataRow("SwiftExport.Contracts.DTOs.DemandPlanning.SourceDemandDelivery.SourceDeliveryErrorCodes", "Inactive", "SOURCE_DELIVERY_INACTIVE")]
    [DataRow("SwiftExport.Contracts.DTOs.DemandPlanning.SourceDemandDelivery.SourceDeliveryErrorCodes", "NotFound", "SOURCE_DELIVERY_NOT_FOUND")]
    [DataRow("SwiftExport.Contracts.DTOs.DemandPlanning.SourceDemandDelivery.SourceDeliveryErrorCodes", "QueryUnavailable", "DELIVERY_QUERY_UNAVAILABLE")]
    [DataRow("SwiftExport.Contracts.DTOs.DemandPlanning.SourceDemandDelivery.SourceDeliveryErrorCodes", "SourceDeliveryExists", "SOURCE_DELIVERY_EXISTS")]
    [DataRow("SwiftExport.Contracts.DTOs.DemandPlanning.SourceDemandDelivery.SourceDeliveryErrorCodes", "SourceDeliveryHistoryInvalid", "SOURCE_DELIVERY_HISTORY_INVALID")]
    [DataRow("SwiftExport.Contracts.DTOs.DemandPlanning.SourceDemandDelivery.SourceDeliveryErrorCodes", "SourceDeliveryQueryInvalid", "SOURCE_DELIVERY_QUERY_INVALID")]
    [DataRow("SwiftExport.Contracts.DTOs.DemandPlanning.SourceDemandDelivery.SourceDeliveryErrorCodes", "VersionConflict", "SOURCE_DELIVERY_VERSION_CONFLICT")]
    [DataRow("SwiftExport.Contracts.DTOs.Errors.ServiceErrorCodes", "BusinessBlocked", "BUSINESS_BLOCKED")]
    [DataRow("SwiftExport.Contracts.DTOs.Errors.ServiceErrorCodes", "CommandBusy", "COMMAND_BUSY")]
    [DataRow("SwiftExport.Contracts.DTOs.Errors.ServiceErrorCodes", "CommandConflict", "COMMAND_CONFLICT")]
    [DataRow("SwiftExport.Contracts.DTOs.Errors.ServiceErrorCodes", "CommandReused", "COMMAND_REUSED")]
    [DataRow("SwiftExport.Contracts.DTOs.Errors.ServiceErrorCodes", "ConcurrencyConflict", "CONCURRENCY_CONFLICT")]
    [DataRow("SwiftExport.Contracts.DTOs.Errors.ServiceErrorCodes", "Forbidden", "FORBIDDEN")]
    [DataRow("SwiftExport.Contracts.DTOs.Errors.ServiceErrorCodes", "InsertFailed", "INSERT_FAILED")]
    [DataRow("SwiftExport.Contracts.DTOs.Errors.ServiceErrorCodes", "InvalidCommand", "INVALID_COMMAND")]
    [DataRow("SwiftExport.Contracts.DTOs.Errors.ServiceErrorCodes", "InvalidReason", "INVALID_REASON")]
    [DataRow("SwiftExport.Contracts.DTOs.Errors.ServiceErrorCodes", "LogWriteFailed", "LOG_WRITE_FAILED")]
    [DataRow("SwiftExport.Contracts.DTOs.Errors.ServiceErrorCodes", "NotFound", "NOT_FOUND")]
    [DataRow("SwiftExport.Contracts.DTOs.Errors.ServiceErrorCodes", "OutcomeUnknown", "OUTCOME_UNKNOWN")]
    [DataRow("SwiftExport.Contracts.DTOs.Errors.ServiceErrorCodes", "RateLimited", "RATE_LIMITED")]
    [DataRow("SwiftExport.Contracts.DTOs.Errors.ServiceErrorCodes", "SchemaNotReady", "SCHEMA_NOT_READY")]
    [DataRow("SwiftExport.Contracts.DTOs.Errors.ServiceErrorCodes", "ServiceUnavailable", "SERVICE_UNAVAILABLE")]
    [DataRow("SwiftExport.Contracts.DTOs.Errors.ServiceErrorCodes", "StateBlocked", "STATE_BLOCKED")]
    [DataRow("SwiftExport.Contracts.DTOs.Errors.ServiceErrorCodes", "TransactionRequired", "TRANSACTION_REQUIRED")]
    [DataRow("SwiftExport.Contracts.DTOs.Errors.ServiceErrorCodes", "Unauthenticated", "UNAUTHENTICATED")]
    [DataRow("SwiftExport.Contracts.DTOs.Errors.ServiceErrorCodes", "Unexpected", "UNEXPECTED")]
    [DataRow("SwiftExport.Contracts.DTOs.Errors.ServiceErrorCodes", "Validation", "VALIDATION")]
    [DataRow("SwiftExport.Contracts.DTOs.Errors.ServiceErrorCodes", "VersionConflict", "VERSION_CONFLICT")]
    [DataRow("SwiftExport.Contracts.DTOs.Errors.ServiceErrorCodes", "WriteFailed", "WRITE_FAILED")]
    [DataRow("SwiftExport.Contracts.DTOs.Files.FileErrorCodes", "Conflict", "FILE_CONFLICT")]
    [DataRow("SwiftExport.Contracts.DTOs.Files.FileErrorCodes", "UnsupportedMediaType", "FILE_UNSUPPORTED_MEDIA_TYPE")]
    [DataRow("SwiftExport.Contracts.DTOs.Files.FileErrorCodes", "UploadTooLarge", "FILE_UPLOAD_TOO_LARGE")]
    [DataRow("SwiftExport.Contracts.DTOs.Files.ProductImages.ProductImageErrorCodes", "DuplicateTargetInBatch", "DUPLICATE_TARGET_IN_BATCH")]
    [DataRow("SwiftExport.Contracts.DTOs.Files.ProductImages.ProductImageErrorCodes", "FileTooLarge", "PRODUCT_IMAGE_FILE_TOO_LARGE")]
    [DataRow("SwiftExport.Contracts.DTOs.Files.ProductImages.ProductImageErrorCodes", "InvalidFileName", "PRODUCT_IMAGE_INVALID_FILE_NAME")]
    [DataRow("SwiftExport.Contracts.DTOs.Files.ProductImages.ProductImageErrorCodes", "MultipleFrames", "PRODUCT_IMAGE_MULTIPLE_FRAMES")]
    [DataRow("SwiftExport.Contracts.DTOs.Files.ProductImages.ProductImageErrorCodes", "PixelLimitExceeded", "PRODUCT_IMAGE_PIXEL_LIMIT_EXCEEDED")]
    [DataRow("SwiftExport.Contracts.DTOs.Files.ProductImages.ProductImageErrorCodes", "ProcessingBusy", "PRODUCT_IMAGE_PROCESSING_BUSY")]
    [DataRow("SwiftExport.Contracts.DTOs.Files.ProductImages.ProductImageErrorCodes", "ProcessingFailed", "PRODUCT_IMAGE_PROCESSING_FAILED")]
    [DataRow("SwiftExport.Contracts.DTOs.Files.ProductImages.ProductImageErrorCodes", "PublishFailed", "PRODUCT_IMAGE_PUBLISH_FAILED")]
    [DataRow("SwiftExport.Contracts.DTOs.Files.ProductImages.ProductImageErrorCodes", "TypeMismatch", "PRODUCT_IMAGE_TYPE_MISMATCH")]
    [DataRow("SwiftExport.Contracts.DTOs.Files.ProductImages.ProductImageErrorCodes", "UnsupportedType", "PRODUCT_IMAGE_UNSUPPORTED_TYPE")]
    [DataRow("SwiftExport.Contracts.DTOs.ForExcel.PackingLists.PackingListImportIssueCodes", "CustomerUnavailable", "CustomerUnavailable")]
    [DataRow("SwiftExport.Contracts.DTOs.ForExcel.PackingLists.PackingListImportIssueCodes", "DateRequired", "DateRequired")]
    [DataRow("SwiftExport.Contracts.DTOs.ForExcel.PackingLists.PackingListImportIssueCodes", "DescriptionRequired", "DescriptionRequired")]
    [DataRow("SwiftExport.Contracts.DTOs.ForExcel.PackingLists.PackingListImportIssueCodes", "LinkSkuMismatch", "LinkSkuMismatch")]
    [DataRow("SwiftExport.Contracts.DTOs.ForExcel.PackingLists.PackingListImportIssueCodes", "NonStandardProduct", "NonStandardProduct")]
    [DataRow("SwiftExport.Contracts.DTOs.ForExcel.PackingLists.PackingListImportIssueCodes", "PackingInvalid", "PackingInvalid")]
    [DataRow("SwiftExport.Contracts.DTOs.ForExcel.PackingLists.PackingListImportIssueCodes", "ProductNotMatched", "ProductNotMatched")]
    [DataRow("SwiftExport.Contracts.DTOs.ForExcel.PackingLists.PackingListImportIssueCodes", "ProductUnavailable", "ProductUnavailable")]
    [DataRow("SwiftExport.Contracts.DTOs.ForExcel.PackingLists.PackingListImportIssueCodes", "QuantityInvalid", "QuantityInvalid")]
    [DataRow("SwiftExport.Contracts.DTOs.ForExcel.PackingLists.PackingListImportIssueCodes", "TextTooLong", "TextTooLong")]
    [DataRow("SwiftExport.Contracts.DTOs.ForExcel.PackingLists.PackingListImportIssueCodes", "UnitRequired", "UnitRequired")]
    [DataRow("SwiftExport.Contracts.DTOs.ForExcel.PurchaseOrders.PurchaseOrderImportIssueCodes", "CustomerRequired", "CustomerRequired")]
    [DataRow("SwiftExport.Contracts.DTOs.ForExcel.PurchaseOrders.PurchaseOrderImportIssueCodes", "CustomerUnavailable", "CustomerUnavailable")]
    [DataRow("SwiftExport.Contracts.DTOs.ForExcel.PurchaseOrders.PurchaseOrderImportIssueCodes", "DeliverySupplierRequired", "DeliverySupplierRequired")]
    [DataRow("SwiftExport.Contracts.DTOs.ForExcel.PurchaseOrders.PurchaseOrderImportIssueCodes", "LinkSkuMismatch", "LinkSkuMismatch")]
    [DataRow("SwiftExport.Contracts.DTOs.ForExcel.PurchaseOrders.PurchaseOrderImportIssueCodes", "SupplierUnavailable", "SupplierUnavailable")]
    [DataRow("SwiftExport.Contracts.DTOs.ForExcel.PurchaseOrders.PurchaseOrderImportIssueCodes", "UnitUnavailable", "UnitUnavailable")]
    [DataRow("SwiftExport.Contracts.DTOs.ForExcel.SalesOrders.SalesOrderImportIssueCodes", "AmountOverflow", "AmountOverflow")]
    [DataRow("SwiftExport.Contracts.DTOs.ForExcel.SalesOrders.SalesOrderImportIssueCodes", "CurrencyRequired", "CurrencyRequired")]
    [DataRow("SwiftExport.Contracts.DTOs.ForExcel.SalesOrders.SalesOrderImportIssueCodes", "CustomerRequired", "CustomerRequired")]
    [DataRow("SwiftExport.Contracts.DTOs.ForExcel.SalesOrders.SalesOrderImportIssueCodes", "CustomerUnavailable", "CustomerUnavailable")]
    [DataRow("SwiftExport.Contracts.DTOs.ForExcel.SalesOrders.SalesOrderImportIssueCodes", "DepositRateInvalid", "DepositRateInvalid")]
    [DataRow("SwiftExport.Contracts.DTOs.ForExcel.SalesOrders.SalesOrderImportIssueCodes", "DescriptionRequired", "DescriptionRequired")]
    [DataRow("SwiftExport.Contracts.DTOs.ForExcel.SalesOrders.SalesOrderImportIssueCodes", "ExchangeRateInvalid", "ExchangeRateInvalid")]
    [DataRow("SwiftExport.Contracts.DTOs.ForExcel.SalesOrders.SalesOrderImportIssueCodes", "ExistingOrders", "ExistingOrders")]
    [DataRow("SwiftExport.Contracts.DTOs.ForExcel.SalesOrders.SalesOrderImportIssueCodes", "LinkSkuMismatch", "LinkSkuMismatch")]
    [DataRow("SwiftExport.Contracts.DTOs.ForExcel.SalesOrders.SalesOrderImportIssueCodes", "NumberNegative", "NumberNegative")]
    [DataRow("SwiftExport.Contracts.DTOs.ForExcel.SalesOrders.SalesOrderImportIssueCodes", "OrderDateRequired", "OrderDateRequired")]
    [DataRow("SwiftExport.Contracts.DTOs.ForExcel.SalesOrders.SalesOrderImportIssueCodes", "PackingInvalid", "PackingInvalid")]
    [DataRow("SwiftExport.Contracts.DTOs.ForExcel.SalesOrders.SalesOrderImportIssueCodes", "ProductAmbiguous", "ProductAmbiguous")]
    [DataRow("SwiftExport.Contracts.DTOs.ForExcel.SalesOrders.SalesOrderImportIssueCodes", "ProductNotMatched", "ProductNotMatched")]
    [DataRow("SwiftExport.Contracts.DTOs.ForExcel.SalesOrders.SalesOrderImportIssueCodes", "ProductUnavailable", "ProductUnavailable")]
    [DataRow("SwiftExport.Contracts.DTOs.ForExcel.SalesOrders.SalesOrderImportIssueCodes", "QuantityInvalid", "QuantityInvalid")]
    [DataRow("SwiftExport.Contracts.DTOs.ForExcel.SalesOrders.SalesOrderImportIssueCodes", "SelectionInvalid", "SelectionInvalid")]
    [DataRow("SwiftExport.Contracts.DTOs.ForExcel.SalesOrders.SalesOrderImportIssueCodes", "UnitRequired", "UnitRequired")]
    [DataRow("SwiftExport.Contracts.DTOs.Inventory.InventoryErrorCodes", "DocumentStateInvalid", "DOCUMENT_STATE_INVALID")]
    [DataRow("SwiftExport.Contracts.DTOs.Inventory.InventoryErrorCodes", "InventoryBlocked", "INVENTORY_BLOCKED")]
    [DataRow("SwiftExport.Contracts.DTOs.Inventory.InventoryErrorCodes", "LedgerIntegrity", "LEDGER_INTEGRITY")]
    [DataRow("SwiftExport.Contracts.DTOs.Inventory.InventoryErrorCodes", "NumberFailed", "NUMBER_FAILED")]
    [DataRow("SwiftExport.Contracts.DTOs.Inventory.InventoryErrorCodes", "OutcomeInvalid", "OUTCOME_INVALID")]
    [DataRow("SwiftExport.Contracts.DTOs.Inventory.InventoryErrorCodes", "ProductUnitInvalid", "PRODUCT_UNIT_INVALID")]
    [DataRow("SwiftExport.Contracts.DTOs.Inventory.InventoryErrorCodes", "PurchaseCapacityExceeded", "PURCHASE_CAPACITY_EXCEEDED")]
    [DataRow("SwiftExport.Contracts.DTOs.Inventory.InventoryErrorCodes", "PurchaseNotReceivable", "PURCHASE_NOT_RECEIVABLE")]
    [DataRow("SwiftExport.Contracts.DTOs.Inventory.InventoryErrorCodes", "ReceiptHistoryInvalid", "RECEIPT_HISTORY_INVALID")]
    [DataRow("SwiftExport.Contracts.DTOs.Inventory.InventoryErrorCodes", "ResourcesAlreadyPrepared", "RESOURCES_ALREADY_PREPARED")]
    [DataRow("SwiftExport.Contracts.DTOs.Inventory.InventoryErrorCodes", "ResourcesNotPrepared", "RESOURCES_NOT_PREPARED")]
    [DataRow("SwiftExport.Contracts.DTOs.Inventory.InventoryErrorCodes", "SupplierWarehouseInvalid", "SUPPLIER_WAREHOUSE_INVALID")]
    [DataRow("SwiftExport.Contracts.DTOs.Inventory.InventoryErrorCodes", "UndeclaredResource", "UNDECLARED_RESOURCE")]
    [DataRow("SwiftExport.Contracts.DTOs.Inventory.InventoryErrorCodes", "UnsupportedType", "UNSUPPORTED_TYPE")]
    [DataRow("SwiftExport.Contracts.DTOs.Inventory.InventoryErrorCodes", "WarehouseInvalid", "WAREHOUSE_INVALID")]
    [DataRow("SwiftExport.Contracts.DTOs.Inventory.Outbounds.OutboundErrorCodes", "CommandBusy", "COMMAND_BUSY")]
    [DataRow("SwiftExport.Contracts.DTOs.Inventory.Outbounds.OutboundErrorCodes", "OutcomeUnknown", "OUTCOME_UNKNOWN")]
    [DataRow("SwiftExport.Contracts.DTOs.Procurement.OrderManagement.ProcurementErrorCodes", "BomBasisChanged", "PROC_BOM_BASIS_CHANGED")]
    [DataRow("SwiftExport.Contracts.DTOs.Procurement.OrderManagement.ProcurementErrorCodes", "ProductAmbiguous", "ProductAmbiguous")]
    [DataRow("SwiftExport.Contracts.DTOs.Procurement.OrderManagement.ProcurementErrorCodes", "ProductNotMatched", "ProductNotMatched")]
    [DataRow("SwiftExport.Contracts.DTOs.Procurement.OrderManagement.ProcurementErrorCodes", "ProductUnavailable", "ProductUnavailable")]
    [DataRow("SwiftExport.Contracts.DTOs.Procurement.OrderManagement.ProcurementErrorCodes", "SelectionInvalid", "SelectionInvalid")]
    [DataRow("SwiftExport.Contracts.DTOs.Procurement.PurchaseDrafts.PurchaseDraftErrorCodes", "AppendConcurrencyConflict", "APPEND_CONCURRENCY_CONFLICT")]
    [DataRow("SwiftExport.Contracts.DTOs.Procurement.PurchaseDrafts.PurchaseDraftErrorCodes", "AppendTargetMismatch", "APPEND_TARGET_MISMATCH")]
    [DataRow("SwiftExport.Contracts.DTOs.Procurement.PurchaseDrafts.PurchaseDraftErrorCodes", "AppendTargetRequired", "APPEND_TARGET_REQUIRED")]
    [DataRow("SwiftExport.Contracts.DTOs.Procurement.PurchaseDrafts.PurchaseDraftErrorCodes", "AppendTargetUnsafe", "APPEND_TARGET_UNSAFE")]
    [DataRow("SwiftExport.Contracts.DTOs.Procurement.PurchaseDrafts.PurchaseDraftErrorCodes", "CorruptedState", "CORRUPTED_STATE")]
    [DataRow("SwiftExport.Contracts.DTOs.Procurement.PurchaseDrafts.PurchaseDraftErrorCodes", "CustomerRequired", "CUSTOMER_REQUIRED")]
    [DataRow("SwiftExport.Contracts.DTOs.Procurement.PurchaseDrafts.PurchaseDraftErrorCodes", "DocumentNoFailed", "DOCUMENT_NO_FAILED")]
    [DataRow("SwiftExport.Contracts.DTOs.Procurement.PurchaseDrafts.PurchaseDraftErrorCodes", "DuplicateItem", "DUPLICATE_ITEM")]
    [DataRow("SwiftExport.Contracts.DTOs.Procurement.PurchaseDrafts.PurchaseDraftErrorCodes", "DuplicateSource", "DUPLICATE_SOURCE")]
    [DataRow("SwiftExport.Contracts.DTOs.Procurement.PurchaseDrafts.PurchaseDraftErrorCodes", "DuplicateTarget", "DUPLICATE_TARGET")]
    [DataRow("SwiftExport.Contracts.DTOs.Procurement.PurchaseDrafts.PurchaseDraftErrorCodes", "ExecutionRequestRequired", "EXECUTION_REQUEST_REQUIRED")]
    [DataRow("SwiftExport.Contracts.DTOs.Procurement.PurchaseDrafts.PurchaseDraftErrorCodes", "IdempotencyKeyReused", "IDEMPOTENCY_KEY_REUSED")]
    [DataRow("SwiftExport.Contracts.DTOs.Procurement.PurchaseDrafts.PurchaseDraftErrorCodes", "IdempotencyPartialState", "IDEMPOTENCY_PARTIAL_STATE")]
    [DataRow("SwiftExport.Contracts.DTOs.Procurement.PurchaseDrafts.PurchaseDraftErrorCodes", "InvalidEntryMode", "INVALID_ENTRY_MODE")]
    [DataRow("SwiftExport.Contracts.DTOs.Procurement.PurchaseDrafts.PurchaseDraftErrorCodes", "InvalidItem", "INVALID_ITEM")]
    [DataRow("SwiftExport.Contracts.DTOs.Procurement.PurchaseDrafts.PurchaseDraftErrorCodes", "InvalidPreviewMode", "INVALID_PREVIEW_MODE")]
    [DataRow("SwiftExport.Contracts.DTOs.Procurement.PurchaseDrafts.PurchaseDraftErrorCodes", "InvalidPurchaseQuantity", "INVALID_PURCHASE_QUANTITY")]
    [DataRow("SwiftExport.Contracts.DTOs.Procurement.PurchaseDrafts.PurchaseDraftErrorCodes", "InvalidSource", "INVALID_SOURCE")]
    [DataRow("SwiftExport.Contracts.DTOs.Procurement.PurchaseDrafts.PurchaseDraftErrorCodes", "InvalidTarget", "INVALID_TARGET")]
    [DataRow("SwiftExport.Contracts.DTOs.Procurement.PurchaseDrafts.PurchaseDraftErrorCodes", "ItemRequired", "ITEM_REQUIRED")]
    [DataRow("SwiftExport.Contracts.DTOs.Procurement.PurchaseDrafts.PurchaseDraftErrorCodes", "MasterDataNotFound", "MASTER_DATA_NOT_FOUND")]
    [DataRow("SwiftExport.Contracts.DTOs.Procurement.PurchaseDrafts.PurchaseDraftErrorCodes", "NetDemandNotFound", "NET_DEMAND_NOT_FOUND")]
    [DataRow("SwiftExport.Contracts.DTOs.Procurement.PurchaseDrafts.PurchaseDraftErrorCodes", "NothingToCreate", "NOTHING_TO_CREATE")]
    [DataRow("SwiftExport.Contracts.DTOs.Procurement.PurchaseDrafts.PurchaseDraftErrorCodes", "OneClickMustCreateNew", "ONE_CLICK_MUST_CREATE_NEW")]
    [DataRow("SwiftExport.Contracts.DTOs.Procurement.PurchaseDrafts.PurchaseDraftErrorCodes", "OneClickRequiresAllRemaining", "ONE_CLICK_REQUIRES_ALL_REMAINING")]
    [DataRow("SwiftExport.Contracts.DTOs.Procurement.PurchaseDrafts.PurchaseDraftErrorCodes", "OneClickSingleSource", "ONE_CLICK_SINGLE_SOURCE")]
    [DataRow("SwiftExport.Contracts.DTOs.Procurement.PurchaseDrafts.PurchaseDraftErrorCodes", "PreviewBlocked", "PREVIEW_BLOCKED")]
    [DataRow("SwiftExport.Contracts.DTOs.Procurement.PurchaseDrafts.PurchaseDraftErrorCodes", "PreviewExpired", "PREVIEW_EXPIRED")]
    [DataRow("SwiftExport.Contracts.DTOs.Procurement.PurchaseDrafts.PurchaseDraftErrorCodes", "PreviewFactChanged", "PREVIEW_FACT_CHANGED")]
    [DataRow("SwiftExport.Contracts.DTOs.Procurement.PurchaseDrafts.PurchaseDraftErrorCodes", "PreviewTokenMismatch", "PREVIEW_TOKEN_MISMATCH")]
    [DataRow("SwiftExport.Contracts.DTOs.Procurement.PurchaseDrafts.PurchaseDraftErrorCodes", "PurchaseHeadInsertFailed", "PURCHASE_HEAD_INSERT_FAILED")]
    [DataRow("SwiftExport.Contracts.DTOs.Procurement.PurchaseDrafts.PurchaseDraftErrorCodes", "PurchaseLineInsertFailed", "PURCHASE_LINE_INSERT_FAILED")]
    [DataRow("SwiftExport.Contracts.DTOs.Procurement.PurchaseDrafts.PurchaseDraftErrorCodes", "PurchaseLinkInsertFailed", "PURCHASE_LINK_INSERT_FAILED")]
    [DataRow("SwiftExport.Contracts.DTOs.Procurement.PurchaseDrafts.PurchaseDraftErrorCodes", "SourceHeaderNotFound", "SOURCE_HEADER_NOT_FOUND")]
    [DataRow("SwiftExport.Contracts.DTOs.Procurement.PurchaseDrafts.PurchaseDraftErrorCodes", "SourceNotFound", "SOURCE_NOT_FOUND")]
    [DataRow("SwiftExport.Contracts.DTOs.Procurement.PurchaseDrafts.PurchaseDraftErrorCodes", "SourceRequired", "SOURCE_REQUIRED")]
    [DataRow("SwiftExport.Contracts.DTOs.Procurement.PurchaseDrafts.PurchaseDraftErrorCodes", "SupplierProductDuplicate", "SUPPLIER_PRODUCT_DUPLICATE")]
    [DataRow("SwiftExport.Contracts.DTOs.Procurement.PurchaseDrafts.PurchaseDraftErrorCodes", "TransactionAlreadyActive", "TRANSACTION_ALREADY_ACTIVE")]
    [DataRow("SwiftExport.Contracts.DTOs.Procurement.PurchaseDrafts.PurchaseDraftErrorCodes", "UnexpectedError", "UNEXPECTED_ERROR")]
    [DataRow("SwiftExport.Contracts.DTOs.Procurement.PurchaseDrafts.PurchaseDraftErrorCodes", "WarningConfirmationRequired", "WARNING_CONFIRMATION_REQUIRED")]
    [DataRow("SwiftExport.Contracts.DTOs.Procurement.PurchaseDrafts.PurchaseDraftWarningCodes", "CorruptedPlanningState", "CORRUPTED_PLANNING_STATE")]
    [DataRow("SwiftExport.Contracts.DTOs.Procurement.PurchaseDrafts.PurchaseDraftWarningCodes", "DeliveryTargetRequired", "DELIVERY_TARGET_REQUIRED")]
    [DataRow("SwiftExport.Contracts.DTOs.Procurement.PurchaseDrafts.PurchaseDraftWarningCodes", "DifferentPlannedDeliveryDates", "DIFFERENT_PLANNED_DELIVERY_DATES")]
    [DataRow("SwiftExport.Contracts.DTOs.Procurement.PurchaseDrafts.PurchaseDraftWarningCodes", "ExecutionTypeNotPurchase", "EXECUTION_TYPE_NOT_PURCHASE")]
    [DataRow("SwiftExport.Contracts.DTOs.Procurement.PurchaseDrafts.PurchaseDraftWarningCodes", "InvalidPurchaseQuantity", "INVALID_PURCHASE_QUANTITY")]
    [DataRow("SwiftExport.Contracts.DTOs.Procurement.PurchaseDrafts.PurchaseDraftWarningCodes", "PurchaseDataIncomplete", "PURCHASE_DATA_INCOMPLETE")]
    [DataRow("SwiftExport.Contracts.DTOs.Procurement.PurchaseDrafts.PurchaseDraftWarningCodes", "PurchaseSupplierInvalid", "PURCHASE_SUPPLIER_INVALID")]
    [DataRow("SwiftExport.Contracts.DTOs.Procurement.PurchaseDrafts.PurchaseDraftWarningCodes", "PurchaseSupplierRequired", "PURCHASE_SUPPLIER_REQUIRED")]
    [DataRow("SwiftExport.Contracts.DTOs.Procurement.PurchaseDrafts.PurchaseDraftWarningCodes", "RelatedCustomerSuppliedRemaining", "RELATED_CUSTOMER_SUPPLIED_REMAINING")]
    [DataRow("SwiftExport.Contracts.DTOs.Products.ProductErrorCodes", "ProductMasterSkuOccupied", "PRODUCT_MASTER_SKU_OCCUPIED")]
    [DataRow("SwiftExport.Contracts.DTOs.Products.ProductErrorCodes", "ProductNeutralCodeOccupied", "PRODUCT_NEUTRAL_CODE_OCCUPIED")]
    [DataRow("SwiftExport.Contracts.DTOs.QualityControl.AfterSalesErrorCodes", "AftersalesDeliveryQueryUnavailable", "AFTERSALES_DELIVERY_QUERY_UNAVAILABLE")]
    [DataRow("SwiftExport.Contracts.DTOs.Shipping.PackingLists.PackingListErrorCodes", "AmbiguousSource", "AMBIGUOUS_SOURCE")]
    [DataRow("SwiftExport.Contracts.DTOs.Shipping.PackingLists.PackingListErrorCodes", "BalanceInvalid", "BALANCE_INVALID")]
    [DataRow("SwiftExport.Contracts.DTOs.Shipping.PackingLists.PackingListErrorCodes", "CommandBusy", "COMMAND_BUSY")]
    [DataRow("SwiftExport.Contracts.DTOs.Shipping.PackingLists.PackingListErrorCodes", "CommandConflict", "COMMAND_CONFLICT")]
    [DataRow("SwiftExport.Contracts.DTOs.Shipping.PackingLists.PackingListErrorCodes", "ComponentInvalid", "COMPONENT_INVALID")]
    [DataRow("SwiftExport.Contracts.DTOs.Shipping.PackingLists.PackingListErrorCodes", "ComponentShortfall", "COMPONENT_SHORTFALL")]
    [DataRow("SwiftExport.Contracts.DTOs.Shipping.PackingLists.PackingListErrorCodes", "CustomerInvalid", "CUSTOMER_INVALID")]
    [DataRow("SwiftExport.Contracts.DTOs.Shipping.PackingLists.PackingListErrorCodes", "CustomerMismatch", "CUSTOMER_MISMATCH")]
    [DataRow("SwiftExport.Contracts.DTOs.Shipping.PackingLists.PackingListErrorCodes", "DeliveryExceeded", "DELIVERY_EXCEEDED")]
    [DataRow("SwiftExport.Contracts.DTOs.Shipping.PackingLists.PackingListErrorCodes", "DeliveryUnavailable", "DELIVERY_QUERY_UNAVAILABLE")]
    [DataRow("SwiftExport.Contracts.DTOs.Shipping.PackingLists.PackingListErrorCodes", "DemandAmbiguous", "DEMAND_AMBIGUOUS")]
    [DataRow("SwiftExport.Contracts.DTOs.Shipping.PackingLists.PackingListErrorCodes", "DemandInvalid", "DEMAND_INVALID")]
    [DataRow("SwiftExport.Contracts.DTOs.Shipping.PackingLists.PackingListErrorCodes", "DemandStale", "DEMAND_STALE")]
    [DataRow("SwiftExport.Contracts.DTOs.Shipping.PackingLists.PackingListErrorCodes", "DocumentIntegrity", "DOCUMENT_INTEGRITY")]
    [DataRow("SwiftExport.Contracts.DTOs.Shipping.PackingLists.PackingListErrorCodes", "DuplicateDemand", "DUPLICATE_DEMAND")]
    [DataRow("SwiftExport.Contracts.DTOs.Shipping.PackingLists.PackingListErrorCodes", "ExecutionInvalid", "EXECUTION_INVALID")]
    [DataRow("SwiftExport.Contracts.DTOs.Shipping.PackingLists.PackingListErrorCodes", "ExecutionLinkInvalid", "EXECUTION_LINK_INVALID")]
    [DataRow("SwiftExport.Contracts.DTOs.Shipping.PackingLists.PackingListErrorCodes", "InvalidDraft", "INVALID_DRAFT")]
    [DataRow("SwiftExport.Contracts.DTOs.Shipping.PackingLists.PackingListErrorCodes", "InvalidLines", "INVALID_LINES")]
    [DataRow("SwiftExport.Contracts.DTOs.Shipping.PackingLists.PackingListErrorCodes", "InvalidState", "INVALID_STATE")]
    [DataRow("SwiftExport.Contracts.DTOs.Shipping.PackingLists.PackingListErrorCodes", "LineSetMismatch", "LINE_SET_MISMATCH")]
    [DataRow("SwiftExport.Contracts.DTOs.Shipping.PackingLists.PackingListErrorCodes", "LogFailed", "LOG_FAILED")]
    [DataRow("SwiftExport.Contracts.DTOs.Shipping.PackingLists.PackingListErrorCodes", "NegativeStock", "NEGATIVE_STOCK")]
    [DataRow("SwiftExport.Contracts.DTOs.Shipping.PackingLists.PackingListErrorCodes", "NotFound", "NOT_FOUND")]
    [DataRow("SwiftExport.Contracts.DTOs.Shipping.PackingLists.PackingListErrorCodes", "OutcomeUnknown", "OUTCOME_UNKNOWN")]
    [DataRow("SwiftExport.Contracts.DTOs.Shipping.PackingLists.PackingListErrorCodes", "PreviewStale", "PREVIEW_STALE")]
    [DataRow("SwiftExport.Contracts.DTOs.Shipping.PackingLists.PackingListErrorCodes", "ProductInvalid", "PRODUCT_INVALID")]
    [DataRow("SwiftExport.Contracts.DTOs.Shipping.PackingLists.PackingListErrorCodes", "ResultMissing", "RESULT_MISSING")]
    [DataRow("SwiftExport.Contracts.DTOs.Shipping.PackingLists.PackingListErrorCodes", "SourceChanged", "SOURCE_CHANGED")]
    [DataRow("SwiftExport.Contracts.DTOs.Shipping.PackingLists.PackingListErrorCodes", "SourceInvalid", "SOURCE_INVALID")]
    [DataRow("SwiftExport.Contracts.DTOs.Shipping.PackingLists.PackingListErrorCodes", "SourceNotAssociated", "SOURCE_NOT_ASSOCIATED")]
    [DataRow("SwiftExport.Contracts.DTOs.Shipping.PackingLists.PackingListErrorCodes", "StateBlocked", "STATE_BLOCKED")]
    [DataRow("SwiftExport.Contracts.DTOs.Shipping.PackingLists.PackingListErrorCodes", "SupplyShortfall", "SUPPLY_SHORTFALL")]
    [DataRow("SwiftExport.Contracts.DTOs.Shipping.PackingLists.PackingListErrorCodes", "TransactionAborted", "TRANSACTION_ABORTED")]
    [DataRow("SwiftExport.Contracts.DTOs.Shipping.PackingLists.PackingListErrorCodes", "TransactionExists", "TRANSACTION_EXISTS")]
    [DataRow("SwiftExport.Contracts.DTOs.Shipping.PackingLists.PackingListErrorCodes", "VersionConflict", "VERSION_CONFLICT")]
    [DataRow("SwiftExport.Contracts.DTOs.SystemDtos.ExcelReadProfiles.ExcelReadProfileErrorCodes", "ExcelReadColumnRequired", "EXCEL_READ_COLUMN_REQUIRED")]
    [DataRow("SwiftExport.Contracts.DTOs.SystemDtos.ExcelReadProfiles.ExcelReadProfileErrorCodes", "ExcelReadDuplicateName", "EXCEL_READ_DUPLICATE_NAME")]
    [DataRow("SwiftExport.Contracts.DTOs.SystemDtos.ExcelReadProfiles.ExcelReadProfileErrorCodes", "ExcelReadEndRequired", "EXCEL_READ_END_REQUIRED")]
    [DataRow("SwiftExport.Contracts.DTOs.SystemDtos.ExcelReadProfiles.ExcelReadProfileErrorCodes", "ExcelReadFieldsRequired", "EXCEL_READ_FIELDS_REQUIRED")]
    [DataRow("SwiftExport.Contracts.DTOs.SystemDtos.ExcelReadProfiles.ExcelReadProfileErrorCodes", "ExcelReadStartRowMismatch", "EXCEL_READ_START_ROW_MISMATCH")]
    [DataRow("SwiftExport.Contracts.DTOs.SystemDtos.ExcelReadProfiles.ExcelReadProfileErrorCodes", "ExcelReadSummaryBeforeStart", "EXCEL_READ_SUMMARY_BEFORE_START")]
    [DataRow("SwiftExport.Contracts.DTOs.SystemDtos.ExcelReadProfiles.ExcelReadProfileErrorCodes", "ExcelReadSummaryRequired", "EXCEL_READ_SUMMARY_REQUIRED")]
    public void PublicConstants_RetainWireValues(string typeName, string member, string expected)
    {
        var type = typeof(ServiceErrorCodes).Assembly.GetType(typeName);
        Assert.IsNotNull(type, typeName);
        var field = type.GetField(member);
        Assert.IsNotNull(field, member);
        Assert.IsTrue(field.IsLiteral && !field.IsInitOnly, member);
        Assert.AreEqual(expected, field.GetRawConstantValue(), typeName + "." + member);
    }

    /// <summary>逐码验证整理前已登记的分类，并通过实际失败出口检查结构化传输。</summary>
    [TestMethod]
    [DataRow("AFTERSALES_DELIVERY_QUERY_UNAVAILABLE", 503)]
    [DataRow("AMBIGUOUS_SOURCE", 422)]
    [DataRow("APPEND_CONCURRENCY_CONFLICT", 409)]
    [DataRow("APPEND_TARGET_MISMATCH", 422)]
    [DataRow("APPEND_TARGET_REQUIRED", 400)]
    [DataRow("APPEND_TARGET_UNSAFE", 422)]
    [DataRow("BALANCE_INVALID", 422)]
    [DataRow("BOM_CHILD_NOT_FOUND", 404)]
    [DataRow("BOM_NOT_FOUND", 404)]
    [DataRow("BUSINESS_BLOCKED", 422)]
    [DataRow("COMMAND_BUSY", 409)]
    [DataRow("COMMAND_CONFLICT", 409)]
    [DataRow("COMMAND_REUSED", 409)]
    [DataRow("COMPONENT_INVALID", 422)]
    [DataRow("COMPONENT_SHORTFALL", 422)]
    [DataRow("CONCURRENCY_CONFLICT", 409)]
    [DataRow("CUSTOMER_INVALID", 422)]
    [DataRow("CUSTOMER_MISMATCH", 422)]
    [DataRow("CUSTOMER_REQUIRED", 400)]
    [DataRow("CUSTOMER_SUPPLIED_ROOT_NOT_ALLOWED", 422)]
    [DataRow("DELIVERY_EXCEEDED", 422)]
    [DataRow("DELIVERY_QUERY_UNAVAILABLE", 503)]
    [DataRow("DELIVERY_TARGET_REQUIRED", 400)]
    [DataRow("DEMAND_AMBIGUOUS", 422)]
    [DataRow("DEMAND_INVALID", 422)]
    [DataRow("DEMAND_STALE", 409)]
    [DataRow("DIFFERENT_PLANNED_DELIVERY_DATES", 422)]
    [DataRow("DOCUMENT_STATE_INVALID", 409)]
    [DataRow("DOWNSTREAM_EXECUTION_EXISTS", 422)]
    [DataRow("DUPLICATE_ACTIVE_SOURCE", 409)]
    [DataRow("DUPLICATE_DEMAND", 422)]
    [DataRow("DUPLICATE_ITEM", 400)]
    [DataRow("DUPLICATE_SOURCE", 400)]
    [DataRow("DUPLICATE_SOURCE_LINE", 400)]
    [DataRow("DUPLICATE_TARGET", 400)]
    [DataRow("EXECUTION_LINK_INVALID", 422)]
    [DataRow("EXECUTION_REQUEST_REQUIRED", 400)]
    [DataRow("EXECUTION_TYPE_NOT_PURCHASE", 422)]
    [DataRow("FILE_CONFLICT", 409)]
    [DataRow("FILE_UNSUPPORTED_MEDIA_TYPE", 415)]
    [DataRow("FILE_UPLOAD_TOO_LARGE", 413)]
    [DataRow("FORBIDDEN", 403)]
    [DataRow("IDEMPOTENCY_KEY_REUSED", 409)]
    [DataRow("INVALID_BOM_LINE", 422)]
    [DataRow("INVALID_COMMAND", 400)]
    [DataRow("INVALID_CUSTOMER_SUPPLIED_PRODUCT", 422)]
    [DataRow("INVALID_DRAFT", 400)]
    [DataRow("INVALID_ENTRY_MODE", 400)]
    [DataRow("INVALID_ITEM", 400)]
    [DataRow("INVALID_LINES", 400)]
    [DataRow("INVALID_PREVIEW_MODE", 400)]
    [DataRow("INVALID_PRODUCT_BOM", 422)]
    [DataRow("INVALID_PURCHASE_QUANTITY", 400)]
    [DataRow("INVALID_QUANTITY", 400)]
    [DataRow("INVALID_REASON", 400)]
    [DataRow("INVALID_REVISION", 400)]
    [DataRow("INVALID_SOURCE", 400)]
    [DataRow("INVALID_SOURCE_LIFECYCLE", 409)]
    [DataRow("INVALID_STATE", 409)]
    [DataRow("INVALID_SUPPLIER", 400)]
    [DataRow("INVALID_TARGET", 400)]
    [DataRow("INVENTORY_BLOCKED", 422)]
    [DataRow("ITEM_REQUIRED", 400)]
    [DataRow("LINE_SET_MISMATCH", 409)]
    [DataRow("MASTER_DATA_NOT_FOUND", 404)]
    [DataRow("MATERIAL_DEMAND_NOT_ACTIVE", 409)]
    [DataRow("NEGATIVE_STOCK", 422)]
    [DataRow("NET_DEMAND_NOT_FOUND", 404)]
    [DataRow("NON_STANDARD_PRODUCT_FORBIDDEN", 422)]
    [DataRow("NOTHING_TO_CREATE", 422)]
    [DataRow("NOT_FOUND", 404)]
    [DataRow("ONE_CLICK_MUST_CREATE_NEW", 422)]
    [DataRow("ONE_CLICK_REQUIRES_ALL_REMAINING", 422)]
    [DataRow("ONE_CLICK_SINGLE_SOURCE", 422)]
    [DataRow("OUTCOME_UNKNOWN", 503)]
    [DataRow("PREVIEW_BLOCKED", 422)]
    [DataRow("PREVIEW_EXPIRED", 409)]
    [DataRow("PREVIEW_FACT_CHANGED", 409)]
    [DataRow("PREVIEW_STALE", 409)]
    [DataRow("PREVIEW_TOKEN_MISMATCH", 409)]
    [DataRow("PROC_BOM_BASIS_CHANGED", 409)]
    [DataRow("PRODUCTION_CUSTOMER_SUPPLIED_NOT_SUPPORTED", 422)]
    [DataRow("PRODUCT_INVALID", 422)]
    [DataRow("PRODUCT_NOT_FOUND", 404)]
    [DataRow("PRODUCT_UNIT_INVALID", 422)]
    [DataRow("PRODUCT_UNIT_REQUIRED", 422)]
    [DataRow("PURCHASE_CAPACITY_EXCEEDED", 422)]
    [DataRow("PURCHASE_DATA_INCOMPLETE", 422)]
    [DataRow("PURCHASE_NOT_RECEIVABLE", 422)]
    [DataRow("PURCHASE_SUPPLIER_INVALID", 422)]
    [DataRow("PURCHASE_SUPPLIER_REQUIRED", 422)]
    [DataRow("RATE_LIMITED", 429)]
    [DataRow("RELATED_CUSTOMER_SUPPLIED_REMAINING", 422)]
    [DataRow("REVISION_CONFLICT", 409)]
    [DataRow("SCHEMA_NOT_READY", 503)]
    [DataRow("SERVICE_UNAVAILABLE", 503)]
    [DataRow("SOURCE_CHANGED", 409)]
    [DataRow("SOURCE_CHANGE_REQUIRES_DISPOSITION", 422)]
    [DataRow("SOURCE_DELIVERY_CLAIM_INVALID", 400)]
    [DataRow("SOURCE_DELIVERY_EXCEEDED", 422)]
    [DataRow("SOURCE_DELIVERY_EXISTS", 422)]
    [DataRow("SOURCE_DELIVERY_INACTIVE", 409)]
    [DataRow("SOURCE_DELIVERY_NOT_FOUND", 404)]
    [DataRow("SOURCE_DELIVERY_QUERY_INVALID", 400)]
    [DataRow("SOURCE_DELIVERY_VERSION_CONFLICT", 409)]
    [DataRow("SOURCE_DEMAND_MISMATCH", 422)]
    [DataRow("SOURCE_EXIT_BLOCKED", 422)]
    [DataRow("SOURCE_HEADER_NOT_FOUND", 404)]
    [DataRow("SOURCE_INVALID", 422)]
    [DataRow("SOURCE_NOT_FOUND", 404)]
    [DataRow("SOURCE_REQUIRED", 400)]
    [DataRow("SOURCE_TYPE_NOT_SUPPORTED", 422)]
    [DataRow("SOURCE_VERSION_CONFLICT", 409)]
    [DataRow("STALE_SOURCE_VERSION", 409)]
    [DataRow("STATE_BLOCKED", 409)]
    [DataRow("SUPPLIER_PRODUCT_DUPLICATE", 422)]
    [DataRow("SUPPLIER_WAREHOUSE_INVALID", 422)]
    [DataRow("SUPPLY_SHORTFALL", 422)]
    [DataRow("UNAUTHENTICATED", 401)]
    [DataRow("UNSUBMITTED_ACTIVE_SOURCE", 422)]
    [DataRow("UNSUPPORTED_SUPPLY_TYPE", 422)]
    [DataRow("UNSUPPORTED_TYPE", 422)]
    [DataRow("VALIDATION", 400)]
    [DataRow("VERSION_CONFLICT", 409)]
    [DataRow("WAREHOUSE_INVALID", 422)]
    [DataRow("WARNING_CONFIRMATION_REQUIRED", 422)]
    public void RegisteredErrors_RetainHttpStatus(string code, int expected)
    {
        var response = ServiceErrorResponse.Create(code, "业务提示", "Lines[0].Qty");
        Assert.AreEqual(expected, response.StatusCode, code);
        var body = (ServiceResult<object>)response.Value!;
        Assert.AreEqual(code, body.Error.Code);
        Assert.AreEqual("业务提示", body.Error.Message);
        Assert.AreEqual("Lines[0].Qty", body.Error.Field);
        Assert.IsNull(body.Data);
    }

    /// <summary>技术故障和未判定的历史完整性继续走 500，不能将内部详情暴露为业务提示。</summary>
    [TestMethod]
    [DataRow("UNREGISTERED_CODE")]
    [DataRow("WRITE_FAILED")]
    [DataRow("LOG_WRITE_FAILED")]
    [DataRow("EXECUTION_INVALID")]
    [DataRow("RESULT_MISSING")]
    [DataRow("DOCUMENT_INTEGRITY")]
    [DataRow("LEDGER_INTEGRITY")]
    [DataRow("SOURCE_HISTORY_INVALID")]
    [DataRow("SOURCE_VERSION_MISSING")]
    [DataRow("SOURCE_DELIVERY_HISTORY_INVALID")]
    public void UnclassifiedAndTechnicalErrors_RetainSafe500(string code)
    {
        var response = ServiceErrorResponse.Create(code, "secret-sql", "Internal.Path");
        Assert.AreEqual(500, response.StatusCode);
        var body = (ServiceResult<object>)response.Value!;
        Assert.AreEqual("UNEXPECTED", body.Error.Code);
        Assert.IsFalse(body.Message.Contains("secret-sql", StringComparison.Ordinal));
        Assert.AreEqual(body.Message, body.Error.Message);
        Assert.IsNull(body.Error.Field);
        Assert.IsNull(body.Data);
    }

    /// <summary>通用和模块错误均使用原来的三个字段，序列化不增加专用 DTO 或扩展详情。</summary>
    [TestMethod]
    [DataRow("VALIDATION")]
    [DataRow("PREVIEW_EXPIRED")]
    [DataRow("ProductNotMatched")]
    [DataRow("FILE_CONFLICT")]
    public void ModuleErrors_UseExistingServiceErrorDto(string code)
    {
        var settings = new JsonSerializerSettings { ContractResolver = new CamelCasePropertyNamesContractResolver() };
        var original = ServiceResult<object>.Fail(code, "业务提示", "Lines[0].Qty");
        var json = JsonConvert.SerializeObject(original, settings);
        var error = JObject.Parse(json)["error"]!;
        CollectionAssert.AreEquivalent(new[] { "code", "message", "field" }, ((JObject)error).Properties().Select(x => x.Name).ToArray());
        var restored = JsonConvert.DeserializeObject<ServiceResult<object>>(json)!;
        Assert.AreEqual(typeof(ServiceErrorDto), restored.Error.GetType());
        Assert.AreEqual(code, restored.Error.Code);
        Assert.AreEqual("业务提示", restored.Error.Message);
        Assert.AreEqual("Lines[0].Qty", restored.Error.Field);
    }

    /// <summary>箱单阻断沿用原专用 DTO，保留来源、物理行和数量缺口。</summary>
    [TestMethod]
    public void PostingBlocker_PreservesBusinessIdentityAndShortfall()
    {
        var source = Guid.NewGuid(); var line = Guid.NewGuid();
        var blocker = new PackingListPostingBlockerDto { Code = PackingListErrorCodes.SupplyShortfall,
            Message = "库存不足", GrossDemandId = source, PackingListLineId = line, ShortfallQty = 1.25m };
        var restored = JsonConvert.DeserializeObject<PackingListPostingBlockerDto>(JsonConvert.SerializeObject(blocker))!;
        Assert.AreEqual("SUPPLY_SHORTFALL", restored.Code);
        Assert.AreEqual(source, restored.GrossDemandId);
        Assert.AreEqual(line, restored.PackingListLineId);
        Assert.AreEqual(1.25m, restored.ShortfallQty);
    }

    /// <summary>产品匹配的真实拒绝条件产生原专用码，错误分类与展示文字无关。</summary>
    [TestMethod]
    [DataRow("ProductNotMatched", 422)]
    [DataRow("SelectionInvalid", 400)]
    [DataRow("ProductAmbiguous", 422)]
    [DataRow("ProductUnavailable", 422)]
    public void ProductMatchingFailures_ReachExpectedServiceExit(string code, int expected)
    {
        var candidate = new PurchaseOrderImportProductCandidateDto { CustomerSku = "CUSTOMER",
            CustomerProductId = Guid.NewGuid(), ProductId = Guid.NewGuid(), Sku = "SKU", Unit = "Pcs", IsUsable = true };
        IReadOnlyList<PurchaseOrderImportProductCandidateDto> candidates = code switch
        {
            "ProductNotMatched" => [],
            "ProductAmbiguous" => [candidate, new() { CustomerSku = "CUSTOMER", CustomerProductId = Guid.NewGuid() }],
            _ => [candidate]
        };
        if (code == "ProductUnavailable") candidate.IsUsable = false;
        var selection = code == "SelectionInvalid" ? Guid.NewGuid() : (Guid?)null;
        var error = Assert.ThrowsExactly<ProcurementOperationException>(() => IndependentPurchaseOrderInputPolicy.Resolve("CUSTOMER", selection, candidates));
        Assert.AreEqual(code, error.Code);
        Assert.AreEqual(expected, ServiceErrorResponse.Create(error.Code, error.Message, error.Field).StatusCode);
    }
}
