namespace SwiftExport.Application.Services.DemandPlanning.Materials;

/// <summary>表示物料建立或回退事务内发现的业务规则失败，保留原模块错误码。</summary>
internal sealed class MaterialDemandException(string code, string message) : Exception(message)
{
    /// <summary>供服务入口转换为既有业务错误响应的代码。</summary>
    public string Code { get; } = code;
}
