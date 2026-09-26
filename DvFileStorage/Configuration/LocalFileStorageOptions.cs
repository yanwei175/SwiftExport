namespace DvFileStorage.Configuration;

/// <summary>提供本地文件存储实例启动时使用的固定配置。</summary>
public sealed class LocalFileStorageOptions
{
    /// <summary>
    /// 获取或设置已存在的绝对根目录；存储实例创建后不会跟随此值变化。
    /// </summary>
    public string RootPath { get; set; } = string.Empty;
}
