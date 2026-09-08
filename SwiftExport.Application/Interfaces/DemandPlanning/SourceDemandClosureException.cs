namespace SwiftExport.Application.Interfaces.DemandPlanning;
/// <summary>来源结案资格阻断，调用方只能展示或交由下游处置。</summary>
public sealed class SourceDemandClosureException(string code,string message) : Exception(message)
{
    /// <summary>稳定阻断代码。</summary>
    public string Code { get; } = code;
}
