using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections;
using System.ComponentModel;
using Mapster;
using SwiftExport.Contracts.DTOs.QualityControl.FlowManagement;
using SwiftExportClient.Mvvm;

namespace SwiftExportClient.Features.QualityControl.FlowManagement.Models;

/// <summary>一个功能的测量行；复用公共行状态，快照只在服务端保存后回填。</summary>
public sealed partial class FlowLineModel : BaseLineModel<FlowLineDto>, INotifyDataErrorInfo
{
    private readonly FlowFieldErrors _errors = new();
    /// <summary>绑定字段错误通知，真实字段修正后清除过期提示。</summary>
    public FlowLineModel()
    {
        _errors.ErrorsChanged += (_, args) => ErrorsChanged?.Invoke(this, args);
        PropertyChanged += (_, args) => { if (args.PropertyName is { } field && !IsTrackingIgnoredProperty(field)) _errors.Clear(field); };
    }
    /// <summary>通知视图行字段错误变化。</summary>
    public event EventHandler<DataErrorsChangedEventArgs>? ErrorsChanged;
    /// <summary>当前行是否验证失败。</summary>
    public bool HasErrors => _errors.HasErrors;
    /// <summary>读取本行的指定字段错误。</summary>
    public IEnumerable GetErrors(string? propertyName) => _errors.GetErrors(propertyName);
    /// <summary>接纳稳定字段键错误。</summary>
    public void SetFieldError(string field, string message) => _errors.Set(field, message);
    /// <summary>当前功能是否沿用本行已经保存的选择。</summary>
    public bool IsOriginalFunction => !IsNew && FunctionId == _savedFunctionId;
    /// <summary>当前限流器是否沿用本行已经保存的选择。</summary>
    public bool IsOriginalLimiter => !IsNew && LimiterSpecificationId == _savedLimiterId;
    private Guid _savedFunctionId;
    private Guid _savedLimiterId;
    /// <summary>供当前行显示新选择；集合由页面提供，模型不查询字典。</summary>
    public IReadOnlyList<FlowDictionaryOption> FunctionOptions { get; private set; } = [];
    /// <summary>供当前行显示新限流器选择。</summary>
    public IReadOnlyList<FlowDictionaryOption> LimiterOptions { get; private set; } = [];
    /// <summary>既有选择显示历史快照，新选择显示本次候选名称。</summary>
    public string FunctionDisplayName => IsOriginalFunction ? FunctionNameSnapshot : FunctionOptions.FirstOrDefault(x => x.Uid == FunctionId)?.Name ?? "请选择功能";
    /// <summary>既有限流器显示历史快照，新选择显示本次候选名称。</summary>
    public string LimiterDisplayName => IsOriginalLimiter ? LimiterSpecificationNameSnapshot : LimiterOptions.FirstOrDefault(x => x.Uid == LimiterSpecificationId)?.Name ?? "请选择限流器";
    [ObservableProperty][NotifyPropertyChangedFor(nameof(FunctionDisplayName))] public partial Guid FunctionId { get; set; }
    [ObservableProperty] public partial string FunctionNameSnapshot { get; set; } = string.Empty;
    [ObservableProperty][NotifyPropertyChangedFor(nameof(LimiterDisplayName))] public partial Guid LimiterSpecificationId { get; set; }
    [ObservableProperty] public partial string LimiterSpecificationNameSnapshot { get; set; } = string.Empty;
    [ObservableProperty] public partial int SortNo { get; set; }
    [ObservableProperty] public partial string? Remark { get; set; }
    /// <summary>1 bar 压力下的实测流量，单位 L/min；空表示未测，零表示实测零值。</summary>
    [ObservableProperty] public partial decimal? Flow1BarLMin { get; set; }
    /// <summary>2 bar 压力下的实测流量，单位 L/min；空表示未测，零表示实测零值。</summary>
    [ObservableProperty] public partial decimal? Flow2BarLMin { get; set; }
    /// <summary>3 bar 压力下的实测流量，单位 L/min；空表示未测，零表示实测零值。</summary>
    [ObservableProperty] public partial decimal? Flow3BarLMin { get; set; }
    /// <summary>4 bar 压力下的实测流量，单位 L/min；空表示未测，零表示实测零值。</summary>
    [ObservableProperty] public partial decimal? Flow4BarLMin { get; set; }
    /// <summary>5 bar 压力下的实测流量，单位 L/min；空表示未测，零表示实测零值。</summary>
    [ObservableProperty] public partial decimal? Flow5BarLMin { get; set; }
    /// <summary>6 bar 压力下的实测流量，单位 L/min；空表示未测，零表示实测零值。</summary>
    [ObservableProperty] public partial decimal? Flow6BarLMin { get; set; }
    /// <summary>7 bar 压力下的实测流量，单位 L/min；空表示未测，零表示实测零值。</summary>
    [ObservableProperty] public partial decimal? Flow7BarLMin { get; set; }
    /// <summary>8 bar 压力下的实测流量，单位 L/min；空表示未测，零表示实测零值。</summary>
    [ObservableProperty] public partial decimal? Flow8BarLMin { get; set; }
    /// <summary>20 PSI 压力下的实测流量，单位美国加仑/分钟；空表示未测，零表示实测零值。</summary>
    [ObservableProperty] public partial decimal? Flow20PsiGpm { get; set; }
    /// <summary>40 PSI 压力下的实测流量，单位美国加仑/分钟；空表示未测，零表示实测零值。</summary>
    [ObservableProperty] public partial decimal? Flow40PsiGpm { get; set; }
    /// <summary>60 PSI 压力下的实测流量，单位美国加仑/分钟；空表示未测，零表示实测零值。</summary>
    [ObservableProperty] public partial decimal? Flow60PsiGpm { get; set; }
    /// <summary>80 PSI 压力下的实测流量，单位美国加仑/分钟；空表示未测，零表示实测零值。</summary>
    [ObservableProperty] public partial decimal? Flow80PsiGpm { get; set; }
    /// <summary>100 PSI 压力下的实测流量，单位美国加仑/分钟；空表示未测，零表示实测零值。</summary>
    [ObservableProperty] public partial decimal? Flow100PsiGpm { get; set; }

    /// <summary>装载服务器快照与版本。</summary>
    protected override void LoadFromDto(FlowLineDto dto)
    {
        _errors.ClearAll();
        _savedFunctionId = dto.FunctionId; _savedLimiterId = dto.LimiterSpecificationId;
        dto.Adapt(this);
        OnPropertyChanged(nameof(FunctionDisplayName)); OnPropertyChanged(nameof(LimiterDisplayName));
    }
    /// <summary>更换页面候选只更新展示，不改变业务选择或脏状态。</summary>
    public void SetOptions(IReadOnlyList<FlowDictionaryOption> functions, IReadOnlyList<FlowDictionaryOption> limiters)
    {
        FunctionOptions = functions; LimiterOptions = limiters;
        OnPropertyChanged(nameof(FunctionOptions)); OnPropertyChanged(nameof(LimiterOptions));
        OnPropertyChanged(nameof(FunctionDisplayName)); OnPropertyChanged(nameof(LimiterDisplayName));
    }
    /// <summary>计算展示和候选不是测量事实，不参与脏跟踪。</summary>
    protected override bool IsTrackingIgnoredProperty(string? propertyName) => base.IsTrackingIgnoredProperty(propertyName)
        || propertyName is nameof(FunctionOptions) or nameof(LimiterOptions) or nameof(FunctionDisplayName) or nameof(LimiterDisplayName);
    /// <summary>生成脏跟踪基线。</summary>
    public override FlowLineDto ToDto() => this.Adapt<FlowLineDto>();
    /// <summary>生成完整集合中的一行；临时行身份统一为空且版本为零。</summary>
    public FlowLineEditDto ToEditDto()
    {
        var dto = this.Adapt<FlowLineEditDto>();
        if (IsNew) { dto.Uid = Guid.Empty; dto.Version = 0; }
        return dto;
    }

    /// <summary>同步检查字典身份、测量点与精度，页面另行校验同组功能重复。</summary>
    public override string? ValidateRow()
    {
        _errors.ClearAll();
        if (FunctionId == Guid.Empty) return _errors.Set(nameof(FunctionId), "请选择功能。");
        if (LimiterSpecificationId == Guid.Empty) return _errors.Set(nameof(LimiterSpecificationId), "请选择限流器规格。");
        if (SortNo < 0) return _errors.Set(nameof(SortNo), "排序号不能为负数。");
        if (Remark?.Length > 2000) return _errors.Set(nameof(Remark), "行备注不能超过 2000 字。");
        (string Field, decimal? Value)[] measurements = [(nameof(Flow1BarLMin), Flow1BarLMin), (nameof(Flow2BarLMin), Flow2BarLMin), (nameof(Flow3BarLMin), Flow3BarLMin), (nameof(Flow4BarLMin), Flow4BarLMin), (nameof(Flow5BarLMin), Flow5BarLMin), (nameof(Flow6BarLMin), Flow6BarLMin), (nameof(Flow7BarLMin), Flow7BarLMin), (nameof(Flow8BarLMin), Flow8BarLMin), (nameof(Flow20PsiGpm), Flow20PsiGpm), (nameof(Flow40PsiGpm), Flow40PsiGpm), (nameof(Flow60PsiGpm), Flow60PsiGpm), (nameof(Flow80PsiGpm), Flow80PsiGpm), (nameof(Flow100PsiGpm), Flow100PsiGpm)];
        if (!measurements.Any(x => x.Value.HasValue)) return _errors.Set(nameof(Flow1BarLMin), "每行至少填写一个实测流量，未测请留空。");
        foreach (var (field, value) in measurements)
        {
            if (value < 0) return _errors.Set(field, "实测流量不能为负数。");
            if (value.HasValue && (decimal.Round(value.Value, 4) != value.Value || value.Value >= 100000000000000m))
                return _errors.Set(field, "流量最多四位小数，且必须小于 100000000000000。");
        }
        return null;
    }
}

