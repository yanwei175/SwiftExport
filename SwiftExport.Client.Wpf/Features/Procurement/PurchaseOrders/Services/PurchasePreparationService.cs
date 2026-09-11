using SwiftExport.Contracts.Apis.CustomerApis;
using SwiftExport.Contracts.Apis.MasterData;
using SwiftExport.Contracts.Apis.Procurement;
using SwiftExport.Contracts.DTOs.Customers;
using SwiftExport.Contracts.DTOs.MasterData.ProcurementLookups;
using SwiftExport.Contracts.DTOs.Procurement.PurchaseDrafts;
using SwiftExport.Contracts.Wrappers;
using SwiftExportClient.Services.Api;

namespace SwiftExportClient.Features.Procurement.PurchaseOrders.Services;

/// <summary>协调采购预览、供应商查询和客户采购默认配置；不持有任何页面状态。</summary>
public sealed class PurchasePreparationService(
    IPurchaseDraftApi purchaseDraftApi,
    ICustomerPoConfigApi customerPoConfigApi,
    IProcurementMasterDataQueryApi masterDataApi,
    IApiCallRunner apiCallRunner)
{
    private const string NoConfigMessage = "未找到该客户与供应商的采购默认配置。";
    private readonly Dictionary<(Guid CustomerId, Guid SupplierId), CustomerPoConfigDto?> _sessionConfigs = [];

    /// <summary>开始新的采购会话并清空同会话默认配置缓存。</summary>
    public void BeginSession() => _sessionConfigs.Clear();

    /// <summary>查询可用供应商；页面自行决定 latest-request-wins 与展示状态。</summary>
    public Task<ServiceResult<PagedResponseBase<ProcurementSupplierCandidateDto>>> SearchSuppliersAsync(
        PagedRequestBase request,
        CancellationToken cancellationToken) => apiCallRunner.RunAsync(
            () => masterDataApi.SearchSuppliersAsync(request, cancellationToken),
            cancellationToken,
            "搜索供应商失败");

    /// <summary>生成权威采购预览，并按新单客户与实际供应商组合加载会话内去重的默认配置。</summary>
    public async Task<ServiceResult<PurchasePreparationResult>> PrepareAsync(
        PurchaseDraftPreviewRequest request,
        CancellationToken cancellationToken)
    {
        var previewResult = await apiCallRunner.RunAsync(
            () => purchaseDraftApi.PreviewAsync(request, cancellationToken),
            cancellationToken,
            "生成采购预览失败");
        if (!previewResult.Success || previewResult.Data is null)
            return ServiceResult<PurchasePreparationResult>.Fail(previewResult.Message);

        var configs = new Dictionary<(Guid CustomerId, Guid SupplierId), CustomerPoConfigDto?>();
        foreach (var document in previewResult.Data.SourceGroups.SelectMany(group => group.Documents)
                     .Where(document => document.TargetMode == SwiftExport.Contracts.SysEnums.PurchaseDraftTargetModeEnum.CreateNew))
        {
            if (!document.CustomerId.HasValue || document.CustomerId.Value == Guid.Empty)
                return ServiceResult<PurchasePreparationResult>.Fail(
                    $"来源 {document.SourceNo} 缺少可确认的客户身份，不能初始化采购单头。");

            var key = (CustomerId: document.CustomerId.Value, SupplierId: document.SupplierId);
            if (_sessionConfigs.TryGetValue(key, out var cached))
            {
                configs[key] = cached;
                continue;
            }

            var configResult = await apiCallRunner.RunAsync(
                () => customerPoConfigApi.GetByPartiesAsync(key.CustomerId, key.SupplierId, cancellationToken),
                cancellationToken,
                "加载客户采购默认配置失败");
            if (configResult.Success && configResult.Data is not null)
            {
                _sessionConfigs[key] = configResult.Data;
                configs[key] = configResult.Data;
                continue;
            }

            if (configResult.Success)
                return ServiceResult<PurchasePreparationResult>.Fail("客户采购默认配置接口返回了空结果，请刷新后重试。");

            if (string.Equals(configResult.Message, NoConfigMessage, StringComparison.Ordinal))
            {
                _sessionConfigs[key] = null;
                configs[key] = null;
                continue;
            }

            return ServiceResult<PurchasePreparationResult>.Fail(configResult.Message);
        }

        return ServiceResult<PurchasePreparationResult>.Ok(new PurchasePreparationResult
        {
            Preview = previewResult.Data,
            Configurations = configs,
        });
    }
}

/// <summary>采购准备结果，包含权威预览及新单默认配置快照。</summary>
public sealed class PurchasePreparationResult
{
    public required PurchaseDraftPreviewResponse Preview { get; init; }
    public IReadOnlyDictionary<(Guid CustomerId, Guid SupplierId), CustomerPoConfigDto?> Configurations { get; init; }
        = new Dictionary<(Guid CustomerId, Guid SupplierId), CustomerPoConfigDto?>();
}
