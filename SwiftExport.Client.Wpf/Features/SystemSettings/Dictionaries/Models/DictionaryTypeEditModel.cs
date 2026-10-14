using CommunityToolkit.Mvvm.ComponentModel;
using Mapster;
using SwiftExport.Contracts.DTOs.SystemDtos;
using SwiftExportClient.Mvvm;
using System.ComponentModel;

namespace SwiftExportClient.Features.SystemSettings.Dictionaries.Models;

/// <summary>编辑自定义字典类型，按服务端内置或旧系统身份决定只读，不自行推断内置目录。</summary>
public sealed partial class DictionaryTypeEditModel : BaseLineModel<SysDictTypeDto>, IDataErrorInfo
{
    private bool _isProtected;

    [ObservableProperty]
    public partial string DictCode { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string DictName { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsSystem { get; set; }

    /// <summary>服务端内置事实，随快照装载并供只读门禁使用。</summary>
    [ObservableProperty]
    public partial bool IsBuiltIn { get; set; }

    [ObservableProperty]
    public partial string Remark { get; set; } = string.Empty;

    [ObservableProperty]
    public partial Guid? ParentId { get; set; }

    [ObservableProperty]
    public partial int SortOrder { get; set; }

    [ObservableProperty]
    public partial string GroupCode { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string GroupName { get; set; } = string.Empty;

    /// <summary>记录装载时的保护身份，避免修改客户端标记绕过只读门禁。</summary>
    public bool IsProtected => _isProtected;

    public string Error => string.Empty;

    public string this[string columnName] => columnName switch
    {
        nameof(DictCode) when string.IsNullOrWhiteSpace(DictCode) => "字典编码不能为空。",
        nameof(DictName) when string.IsNullOrWhiteSpace(DictName) => "字典名称不能为空。",
        _ => string.Empty
    };

    /// <summary>检查自定义类型编码和名称必填，业务唯一性与树关系由服务端验证。</summary>
    public override string? ValidateRow()
    {
        if (string.IsNullOrWhiteSpace(DictCode))
        {
            return "字典编码不能为空。";
        }

        if (string.IsNullOrWhiteSpace(DictName))
        {
            return "字典名称不能为空。";
        }

        return null;
    }

    /// <summary>系统保护事实属于服务器快照，不作为需要保存的用户变更。</summary>
    protected override bool IsTrackingIgnoredProperty(string? propertyName)
    {
        return base.IsTrackingIgnoredProperty(propertyName)
            || propertyName is nameof(IsProtected) or nameof(IsBuiltIn);
    }

    /// <summary>装载类型快照与服务端保护事实；零版本内置类型无需初始化后才能维护明细。</summary>
    protected override void LoadFromDto(SysDictTypeDto dto)
    {
        dto.Adapt(this);
        _isProtected = dto.IsBuiltIn || dto.IsSystem;
    }

    /// <summary>输出自定义类型的当前字段，保留基类管理的身份和版本。</summary>
    public override SysDictTypeDto ToDto() => this.Adapt<SysDictTypeDto>();
}
