using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Mapster;
using SwiftExport.Contracts.Common;
using SwiftExport.Contracts.DTOs.SystemDtos.SystemParameters;

namespace SwiftExportClient.Features.SystemSettings.SystemParameters.Models;

/// <summary>参数表格的单行草稿；以不可编辑的参数键定位，独立保存值、版本与格式反馈。</summary>
public sealed partial class SystemParameterLineModel : ObservableObject, IDataErrorInfo
{
    private string _baseline = string.Empty;
    /// <summary>代码注册的参数键，仅供界面只读展示。</summary>
    [ObservableProperty] public partial string Key { get; set; } = string.Empty;
    /// <summary>参数名称。</summary>
    [ObservableProperty] public partial string DisplayName { get; set; } = string.Empty;
    /// <summary>参数用途及输入说明。</summary>
    [ObservableProperty] public partial string Description { get; set; } = string.Empty;
    /// <summary>当前输入；保存其他行或刷新缓存不会覆盖此草稿。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasUnsavedChanges), nameof(ValidationMessage), nameof(Error))]
    public partial string Value { get; set; } = string.Empty;
    /// <summary>此行读取的权威版本，用于条件保存。</summary>
    [ObservableProperty] public partial int Version { get; set; }
    /// <summary>是否已在服务器单独配置；未配置行可显式保存建议值。</summary>
    [ObservableProperty] public partial bool IsConfigured { get; set; }
    /// <summary>仅属于此行的保存、冲突及刷新反馈。</summary>
    [ObservableProperty] public partial string Status { get; set; } = string.Empty;
    /// <summary>当前输入是否区别于已接纳基线。</summary>
    public bool HasUnsavedChanges => Value != _baseline;
    /// <summary>已知参数的即时格式校验；未来其他参数的最终规则仍由服务端执行。</summary>
    public string ValidationMessage => Key switch
    {
        SystemParameterKeys.SalesDefaultDeliveryDays =>
            int.TryParse(Value?.Trim(), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var days) && days >= 0
                ? string.Empty : "请输入非负整数自然日天数，0 表示订单当天。",
        SystemParameterKeys.ProductImagesBaseUrl => ProductImageAddress.TryNormalizeBaseAddress(Value, out _) ? string.Empty
            : "请输入 HTTP/HTTPS 目录、Windows 本地绝对目录或 UNC 目录，最多 2048 字符。",
        SystemParameterKeys.ProductImagesUploadBaseUrl => ProductImageAddress.TryNormalizeBaseUrl(Value, out _) ? string.Empty
            : "请输入 HTTP/HTTPS 原图目录，不含凭据、查询、片段或 Thumbnails，最多 2048 字符。",
        _ => string.Empty
    };
    /// <inheritdoc />
    public string Error => ValidationMessage;
    /// <inheritdoc />
    public string this[string columnName] => columnName == nameof(Value) ? ValidationMessage : string.Empty;

    /// <summary>接纳该键的权威回执，映射结构字段并清理单行草稿。</summary>
    public void Accept(SystemParameterDto parameter)
    {
        _baseline = parameter.Value;
        parameter.Adapt(this);
        Status = IsConfigured ? $"当前版本 {Version}。" : "尚未单独配置，保存将建立版本 1。";
        OnPropertyChanged(nameof(HasUnsavedChanges));
    }

    /// <summary>缓存暂不可用时仍显式丢弃输入，恢复本行原读取基线。</summary>
    public void RejectToBaseline() => Value = _baseline;
}
