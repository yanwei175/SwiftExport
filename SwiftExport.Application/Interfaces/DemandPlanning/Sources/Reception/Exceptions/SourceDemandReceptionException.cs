namespace SwiftExport.Application.Interfaces.DemandPlanning.Sources.Reception.Exceptions;

/// <summary>来源需求接收失败；Code 是调用方可稳定识别的业务错误码。</summary>
public sealed class SourceDemandReceptionException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}
