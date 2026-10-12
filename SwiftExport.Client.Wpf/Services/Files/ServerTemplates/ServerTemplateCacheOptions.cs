namespace SwiftExportClient.Services.Files.ServerTemplates;

/// <summary>提供内部缓存边界及可替换测试环境，不增加用户模板管理或离线模式配置。</summary>
internal sealed record ServerTemplateCacheOptions
{
    /// <summary>默认使用当前系统用户的 LocalApplicationData；测试可指定独立目录。</summary>
    internal string? CacheRootPath { get; init; }

    /// <summary>所有环境及账号的模板缓存总配额，默认 200 MiB。</summary>
    internal long MaxCacheBytes { get; init; } = 200 * 1024 * 1024;

    /// <summary>提供缓存访问时间及下载超时的时间源。</summary>
    internal TimeProvider TimeProvider { get; init; } = TimeProvider.System;

    /// <summary>沿用公共文件输入保护；测试可缩小上限以验证真实流边界。</summary>
    internal FileReadLimits ReadLimits { get; init; } = FileReadLimits.Default;

    /// <summary>共享下载拥有独立超时，不因一个等待调用方取消而取消其他等待者。</summary>
    internal TimeSpan DownloadTimeout { get; init; } = TimeSpan.FromSeconds(60);
}
