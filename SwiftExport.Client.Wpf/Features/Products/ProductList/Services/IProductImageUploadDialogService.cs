namespace SwiftExportClient.Features.Products.ProductList.Services;

/// <summary>提供管理员图片选择与上传进度弹窗，不改变产品编辑状态。</summary>
public interface IProductImageUploadDialogService
{
    /// <summary>直接打开上传弹窗，用户在其中选择图片、开始上传和查看结果。</summary>
    Task ShowAsync();
}
