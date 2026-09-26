namespace SwiftExport.Application.Commands;

/// <summary>向命令入口报告业务结果及其是否来自已提交回执，不改变对外响应。</summary>
public sealed record CommandExecutionResult<T>(T Value, bool IsReplay);
