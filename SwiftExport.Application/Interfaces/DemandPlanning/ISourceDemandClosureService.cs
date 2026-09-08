namespace SwiftExport.Application.Interfaces.DemandPlanning;
/// <summary>来源领域为原始单据提供结案资格复核，不执行终止或库存动作。</summary>
public interface ISourceDemandClosureService
{
    /// <summary>在调用方事务中核验来源身份及正式交付或合法关闭，失败抛稳定业务异常。</summary>
    Task VerifyAsync(string sourceType,Guid sourceUid,IReadOnlyDictionary<Guid,decimal> expectedLines,CancellationToken cancellationToken=default);
}
