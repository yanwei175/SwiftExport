using DvFileStorage.Models;

namespace DvFileStorage.Abstractions;

/// <summary>定义以逻辑相对路径访问文件的通用存储能力，不包含业务、HTTP 或数据库语义。</summary>
public interface IFileStorage
{
    /// <summary>
    /// 从源流当前位置异步写入文件；默认拒绝覆盖，源流始终由调用方负责释放。
    /// </summary>
    /// <param name="path">根目录下使用正斜线分隔的逻辑相对路径。</param>
    /// <param name="source">可读源流，从当前位置开始复制。</param>
    /// <param name="overwrite">是否允许以完整的新文件替换现有文件。</param>
    /// <param name="cancellationToken">发布前可取消写入的令牌。</param>
    Task WriteAsync(
        string path,
        Stream source,
        bool overwrite = false,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 打开只读文件流；文件不存在时抛出 <see cref="FileNotFoundException"/>，返回流由调用方释放。
    /// </summary>
    Task<Stream> OpenReadAsync(string path, CancellationToken cancellationToken = default);

    /// <summary>检查逻辑路径是否存在文件；权限或 I/O 故障不会伪装成不存在。</summary>
    Task<bool> ExistsAsync(string path, CancellationToken cancellationToken = default);

    /// <summary>获取不泄漏物理根目录的文件信息；文件不存在时返回 <see langword="null"/>。</summary>
    Task<StoredFileInfo?> GetInfoAsync(string path, CancellationToken cancellationToken = default);

    /// <summary>删除文件；文件不存在视为成功，不删除目录或父目录。</summary>
    Task DeleteIfExistsAsync(string path, CancellationToken cancellationToken = default);
}
