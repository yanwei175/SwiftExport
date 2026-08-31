using System.Threading;
using System.Threading.Tasks;
using Refit;
using SwiftExport.Contracts.DTOs.MasterData.ExecutionEligibility;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.Contracts.Apis.MasterData
{
    /// <summary>仅负责主数据新业务执行资格的启停，当前需服务端有效管理员身份。</summary>
    public interface IExecutionEligibilityApi
    {
        /// <summary>按版本启停指定主数据；相同版本重复提交返回冲突，不重复记录变更。</summary>
        [Post("/api/master-data/execution-eligibility/status")]
        Task<ServiceResult<ExecutionEligibilityStatusResult>> SetStatusAsync(
            [Body] SetExecutionEligibilityStatusCommand command, CancellationToken cancellationToken = default(CancellationToken));
    }
}
