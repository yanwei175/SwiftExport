
namespace SwiftExport.UI.Interfaces
{
    public interface IImageProvider { Task<byte[]> GetImageBytesAsync(string pathOrUrl); }
}
