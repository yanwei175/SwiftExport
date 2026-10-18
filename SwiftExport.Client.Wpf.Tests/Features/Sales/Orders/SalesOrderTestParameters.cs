using SwiftExport.Client.Wpf.Tests.TestInfrastructure;
using SwiftExport.Contracts.DTOs.SystemDtos.SystemParameters;
using SwiftExportClient.Services.Caching;

namespace SwiftExport.Client.Wpf.Tests.Features.Sales.Orders;

/// <summary>仅替代系统参数传输时序，供销售编辑与原有生命周期测试显式提供参数依赖。</summary>
internal sealed class SalesOrderTestParameters : ISystemParameterCacheService
{
    /// <summary>保留服务器原始值与未配置默认值；不会像 TryGetValue 一样过滤空字符串。</summary>
    public SystemParameterDto Parameter { get; } = new() { Key = SystemParameterKeys.SalesDefaultDeliveryDays, Value = "60" };
    /// <summary>允许延迟刷新以覆盖用户先输入、关闭和保存的竞争条件。</summary>
    public Task<CacheRefreshResult>? Pending { get; set; }
    /// <summary>记录每次新建的显式刷新，不把旧快照当权威默认值。</summary>
    public int Refreshes { get; private set; }
    /// <inheritdoc />
    public event EventHandler? Changed;
    /// <inheritdoc />
    public IReadOnlyList<SystemParameterDto> Parameters => [Parameter];
    /// <inheritdoc />
    public bool IsLoaded => true;
    /// <inheritdoc />
    public bool TryGetValue(string key, out string value) { value = Parameter.Value; return key == Parameter.Key && Parameter.IsConfigured && !string.IsNullOrWhiteSpace(value); }
    /// <inheritdoc />
    public Task<CacheRefreshResult> EnsureLoadedAsync() => new ReferenceDataCacheServiceStub().EnsureLoadedAsync();
    /// <inheritdoc />
    public Task<CacheRefreshResult> RefreshAsync(CancellationToken cancellationToken = default) { Refreshes++; return Pending ?? EnsureLoadedAsync(); }
    /// <inheritdoc />
    public void ApplySaved(SystemParameterDto parameter) { Parameter.Value = parameter.Value; Changed?.Invoke(this, EventArgs.Empty); }
    /// <inheritdoc />
    public void Clear() { Parameter.Value = "60"; Changed?.Invoke(this, EventArgs.Empty); }
}
