using System.ComponentModel;
using Mapster;
using CommunityToolkit.Mvvm.ComponentModel;
using SwiftExport.Contracts.DTOs.Products.ProductMasters.ImportExport;
using SwiftExportClient.Features.Products.ProductMasterImportExport.Services;
namespace SwiftExportClient.Features.Products.ProductMasterImportExport.Models;
/// <summary>保存原文件行号、可编辑原始文本及独立预览；非法输入不会变成零或丢失。</summary>
public sealed partial class ProductMasterImportLineModel : ObservableObject
{
    private readonly Dictionary<string,string> _fileErrors = new();
    private ProductMasterImportPreviewRowDto? _preview;
    /// <summary>原文件中的行号，移除其它行不会改变。</summary>
    public int SourceRowNumber { get; set; }
    /// <summary>主型号输入文本。</summary>
    [ObservableProperty] public partial string MasterSku { get; set; } = string.Empty;
    /// <summary>中文描述输入文本。</summary>
    [ObservableProperty] public partial string DefaultDescriptionZh { get; set; } = string.Empty;
    /// <summary>英文描述输入文本。</summary>
    [ObservableProperty] public partial string DefaultDescriptionEn { get; set; } = string.Empty;
    /// <summary>中文配置输入文本。</summary>
    [ObservableProperty] public partial string DefaultFeaturesZh { get; set; } = string.Empty;
    /// <summary>英文配置输入文本。</summary>
    [ObservableProperty] public partial string DefaultFeaturesEn { get; set; } = string.Empty;
    /// <summary>每箱装率输入文本。</summary>
    [ObservableProperty] public partial string DefaultPcsPerPackage { get; set; } = string.Empty;
    /// <summary>每箱净重(KG)输入文本。</summary>
    [ObservableProperty] public partial string DefaultNetWeight { get; set; } = string.Empty;
    /// <summary>每箱毛重(KG)输入文本。</summary>
    [ObservableProperty] public partial string DefaultGrossWeight { get; set; } = string.Empty;
    /// <summary>每箱体积(CBM)输入文本。</summary>
    [ObservableProperty] public partial string DefaultCbm { get; set; } = string.Empty;
    /// <summary>分类路径输入文本。</summary>
    [ObservableProperty] public partial string CategoryPath { get; set; } = string.Empty;
    /// <summary>默认供应商编码输入文本。</summary>
    [ObservableProperty] public partial string DefaultSupplierCode { get; set; } = string.Empty;
    /// <summary>状态供用户区分待校验、新增、更新和错误。</summary>
    public string Status => _fileErrors.Count > 0 ? "文件错误" : _preview?.Operation switch { "Create" => "新增", "Update" => "更新", "Unchanged" => "无变化", "Invalid" => "错误", _ => "待校验" };
    /// <summary>可理解的错误说明，保留文件错误直到对应字段被修正。</summary>
    public string ErrorSummary => string.Join("；", _fileErrors.Values.Concat(_preview?.Issues.Select(issue => issue.Message) ?? []));
    /// <summary>本行当前可提交的服务器预览，编辑会使其失效。</summary>
    public ProductMasterImportPreviewRowDto? Preview => _preview;
    /// <summary>是否仍包含无法读取的单元格。</summary>
    public bool HasFileErrors => _fileErrors.Count > 0;
    /// <summary>列出当前值与拟保存值的差异，不把空白输入误称为清空。</summary>
    public string DifferenceSummary => _preview?.CurrentData is not { } current || _preview.ProposedData is not { } proposed ? string.Empty :
        string.Join("\n", typeof(ProductMasterTransferData).GetProperties().Where(property => !Equals(property.GetValue(current), property.GetValue(proposed)))
            .Select(property => $"{ProductMasterWorkbookService.FieldTitle(property.Name)}：{DifferenceValue(property.Name, current, false)} → {DifferenceValue(property.Name, proposed, true)}"));
    /// <summary>引用差异展示业务路径和编码，避免把内部标识当成用户可理解的值。</summary>
    private object DifferenceValue(string field, ProductMasterTransferData data, bool proposed) => field switch
    {
        nameof(data.CategoryId) => (proposed ? _preview!.ProposedCategoryPath : _preview!.CurrentCategoryPath) ?? "空",
        nameof(data.DefaultSupplierId) => (proposed ? _preview!.ProposedSupplierCode : _preview!.CurrentSupplierCode) ?? "空",
        _ => typeof(ProductMasterTransferData).GetProperty(field)!.GetValue(data) ?? "空"
    };
    /// <summary>生成独立请求快照，结构复制交由 Mapster。</summary>
    public ProductMasterImportRowInput ToInput() => this.Adapt<ProductMasterImportRowInput>();
    /// <summary>设置由工作簿定位的初始错误。</summary>
    public void AddFileError(string field, string message) { _fileErrors[field] = message; NotifyResult(); }
    /// <summary>只在对应请求仍有效时由页面接纳预览。</summary>
    public void ApplyPreview(ProductMasterImportPreviewRowDto? preview) { _preview = preview; NotifyResult(); }
    /// <summary>字段编辑使匹配失效，只清除该字段的原始文件错误。</summary>
    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        if (ProductMasterWorkbookService.InputKeys.Contains(e.PropertyName ?? string.Empty))
        { _preview = null; _fileErrors.Remove(e.PropertyName!); NotifyResult(); }
        base.OnPropertyChanged(e);
    }
    private void NotifyResult()
    { OnPropertyChanged(nameof(Status)); OnPropertyChanged(nameof(ErrorSummary)); OnPropertyChanged(nameof(Preview)); OnPropertyChanged(nameof(HasFileErrors)); OnPropertyChanged(nameof(DifferenceSummary)); }
}
