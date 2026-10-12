namespace SwiftExportClient.Services.Images;

/// <summary>选择列表缩略图或按需原图，SourcePath 始终保存原始引用。</summary>
public enum ImageDisplayMode
{
    /// <summary>托管网络图片使用服务器缩略图，本地图片沿用解码缩放。</summary>
    Thumbnail,
    /// <summary>直接显示原图引用，用于按需查看。</summary>
    Original
}
