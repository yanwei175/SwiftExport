namespace SwiftExportClient.Features.SystemSettings.Services;

/// <summary>设置表单一次确认写入的窄返回值；取消或失败时 Saved 为假。</summary>
public readonly record struct SettingsDialogResult(bool Saved, Guid? TargetId = null, string? CreatedUserName = null);
