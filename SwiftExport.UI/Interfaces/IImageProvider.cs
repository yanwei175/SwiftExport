using System.Windows.Media;

namespace SwiftExport.UI.Interfaces
{
    public interface IImageProvider
    {
        // 根据路径或 URL 异步获取图片
        Task<ImageSource> GetImageAsync(string pathOrUrl);
    }
}
