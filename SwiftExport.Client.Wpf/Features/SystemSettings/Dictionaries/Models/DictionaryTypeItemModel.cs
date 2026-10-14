using CommunityToolkit.Mvvm.ComponentModel;
using SwiftExport.Contracts.DTOs.SystemDtos;

namespace SwiftExportClient.Features.SystemSettings.Dictionaries.Models;

/// <summary>展示服务端有效字典类型清单及选择状态，不在客户端自行补内置定义。</summary>
public sealed partial class DictionaryTypeItemModel : ObservableObject
{
    /// <summary>从服务端类型建立导航项，内置类型即使未落库也具有完整可读快照。</summary>
    public DictionaryTypeItemModel(SysDictTypeDto data)
    {
        Data = data;
    }

    public string Code => Data.DictCode;

    public string Name => Data.DictName;

    public SysDictTypeDto Data { get; }

    /// <summary>内置类型只读；旧库系统标记延续原有界面保护。</summary>
    public bool IsProtected => Data.IsBuiltIn || Data.IsSystem;

    [ObservableProperty]
    public partial bool IsSelected { get; set; }
}
