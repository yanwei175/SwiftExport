using SwiftExport.Core.Entities.DemandPlanning;

namespace SwiftExport.Application.Services.DemandPlanning.Materials.Establishment.Models;

/// <summary>保存一次来源分解的有效性、错误和待写入物料行。</summary>
public sealed class GrossDemandDecompositionPlan
{
    private GrossDemandDecompositionPlan(bool isValid, string? errorCode, string? errorMessage, IReadOnlyList<NetDemand> netDemands)
    {
        IsValid = isValid;
        ErrorCode = errorCode;
        ErrorMessage = errorMessage;
        NetDemands = netDemands;
    }

    public bool IsValid { get; }
    public string? ErrorCode { get; }
    public string? ErrorMessage { get; }
    public IReadOnlyList<NetDemand> NetDemands { get; }

    /// <summary>封装可写入的完整分解行。</summary>
    public static GrossDemandDecompositionPlan Ok(IReadOnlyList<NetDemand> rows) => new(true, null, null, rows);
    /// <summary>封装业务校验失败，调用方不写入物料事实。</summary>
    public static GrossDemandDecompositionPlan Fail(string code, string message) => new(false, code, message, []);
}
