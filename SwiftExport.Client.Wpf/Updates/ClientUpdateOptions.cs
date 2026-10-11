namespace SwiftExportClient.Updates;

/// <summary>定义静态更新源；安装包发布时写入固定 HTTPS 地址。</summary>
public sealed class ClientUpdateOptions
{
    /// <summary>更新配置节名称。</summary>
    public const string SectionName = "Updates";
    public string FeedUrl { get; set; } = string.Empty;
}
