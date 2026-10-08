using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections;
using System.ComponentModel;
using Mapster;
using SwiftExport.Contracts.DTOs.QualityControl.FlowManagement;
using SwiftExportClient.Mvvm;

namespace SwiftExportClient.Features.QualityControl.FlowManagement.Models;

/// <summary>测试组头编辑模型；复用公共身份、审计及脏跟踪，不接收前端生成的权威快照。</summary>
public sealed partial class FlowHeaderModel : BaseLineModel<FlowDetailDto>, INotifyDataErrorInfo
{
    private readonly FlowFieldErrors _errors = new();
    /// <summary>字段错误与脏跟踪分别通知，修正字段后移除过期提示。</summary>
    public FlowHeaderModel()
    {
        _errors.ErrorsChanged += (_, args) => ErrorsChanged?.Invoke(this, args);
        PropertyChanged += (_, args) => { if (args.PropertyName is { } field && !IsTrackingIgnoredProperty(field)) _errors.Clear(field); };
    }
    /// <summary>通知视图头字段错误变化。</summary>
    public event EventHandler<DataErrorsChangedEventArgs>? ErrorsChanged;
    /// <summary>当前头字段是否验证失败。</summary>
    public bool HasErrors => _errors.HasErrors;
    /// <summary>供 WPF 验证绑定读取字段错误。</summary>
    public IEnumerable GetErrors(string? propertyName) => _errors.GetErrors(propertyName);
    /// <summary>接纳服务器提供的稳定头字段键，不分析中文消息内容。</summary>
    public void SetFieldError(string field, string message) => _errors.Set(field, message);
    [ObservableProperty] public partial Guid MaterialId { get; set; }
    [ObservableProperty] public partial string SkuSnapshot { get; set; } = string.Empty;
    [ObservableProperty] public partial DateTime? TestDate { get; set; } = DateTime.Today;
    [ObservableProperty] public partial string? MarketDescription { get; set; }
    [ObservableProperty] public partial string? ConfigurationDescription { get; set; }
    [ObservableProperty] public partial string? Remark { get; set; }

    /// <summary>映射服务端头信息；行由页面的公共列表模型单独维护。</summary>
    protected override void LoadFromDto(FlowDetailDto dto) { _errors.ClearAll(); dto.Adapt(this); }

    /// <summary>生成公共脏跟踪基线，行不属于头模型。</summary>
    public override FlowDetailDto ToDto() => this.Adapt<FlowDetailDto>();

    /// <summary>生成服务器允许编辑的头字段，最终行集合由页面补入。</summary>
    public FlowEditDto ToEditDto() => this.Adapt<FlowEditDto>();

    /// <summary>校验物料、业务日期和数据库支持的文本长度，空日期不能回退为旧值。</summary>
    public override string? ValidateRow()
    {
        _errors.ClearAll();
        if (MaterialId == Guid.Empty) return _errors.Set(nameof(MaterialId), "请选择物料。");
        if (!TestDate.HasValue || TestDate.Value == default) return _errors.Set(nameof(TestDate), "请选择测试日期。");
        foreach (var (field, value) in new[] { (nameof(MarketDescription), MarketDescription), (nameof(ConfigurationDescription), ConfigurationDescription), (nameof(Remark), Remark) })
            if (value?.Length > 2000) return _errors.Set(field, "市场、配件配置和备注各自不能超过 2000 字。");
        return null;
    }
}

