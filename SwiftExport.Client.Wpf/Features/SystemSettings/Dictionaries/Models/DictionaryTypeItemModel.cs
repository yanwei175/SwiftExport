using CommunityToolkit.Mvvm.ComponentModel;
using SwiftExport.Contracts.DTOs.SystemDtos;

namespace SwiftExportClient.Features.SystemSettings.Dictionaries.Models;

public sealed partial class DictionaryTypeItemModel : ObservableObject
{
    public DictionaryTypeItemModel(string code, string fallbackName, SysDictTypeDto? data)
    {
        Code = code;
        Name = string.IsNullOrWhiteSpace(data?.DictName) ? fallbackName : data.DictName;
        Data = data;
    }

    public string Code { get; }

    public string Name { get; }

    public SysDictTypeDto? Data { get; }

    public bool IsConfigured => Data is not null;

    public bool IsProtected => Data?.IsSystem == true
        || SwiftExport.Contracts.SysEnums.DictCodes.DictCodeList.Contains(
            Code,
            StringComparer.OrdinalIgnoreCase);

    [ObservableProperty]
    public partial bool IsSelected { get; set; }
}
