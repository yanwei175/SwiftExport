using CommunityToolkit.Mvvm.ComponentModel;

namespace SwiftExportClient.Features.Products.ProductList.Models;

/// <summary>保留待上传图片的本地路径与逐项展示状态，不修改产品资料。</summary>
public sealed partial class ProductImageUploadItem(string path) : ObservableObject
{
    /// <summary>读取源文件使用的路径；不作为 HTTP 文件名发送。</summary>
    public string Path { get; } = path;
    /// <summary>向用户展示、向服务端提交的文件名。</summary>
    public string FileName => System.IO.Path.GetFileName(Path);
    /// <summary>与服务端转换规则一致的目标名，用于拒绝同次选择中的重复覆盖。</summary>
    public string TargetName => System.IO.Path.GetFileNameWithoutExtension(Path) + ".jpg";
    /// <summary>当前项目的处理结果或等待状态。</summary>
    [ObservableProperty] public partial string Status { get; set; } = "等待上传";
}
