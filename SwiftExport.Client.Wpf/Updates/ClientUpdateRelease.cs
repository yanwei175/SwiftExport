namespace SwiftExportClient.Updates;

/// <summary>向界面提供更新说明，包身份和校验信息由更新服务持有。</summary>
public sealed record ClientUpdateRelease(string Version, string ReleaseNotes);
