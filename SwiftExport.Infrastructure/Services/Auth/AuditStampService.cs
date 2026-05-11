using SwiftExport.Core.Common;
using SwiftExport.Core.Interfaces.Auth;

namespace SwiftExport.Infrastructure.Services.Auth;

/// <summary>
/// 审计字段自动打戳服务实现。
/// 在 DapperRepository 的 Add/Update 前调用，业务代码无需手动赋值 CreatedBy/UpdatedBy。
/// </summary>
public class AuditStampService(ICurrentUser currentUser) : IAuditStampService
{
    private readonly ICurrentUser _currentUser = currentUser;

    /// <summary>
    /// 新增前打戳：填充 CreatedDate、CreatedBy，同时也填充 UpdatedDate、UpdatedBy。
    /// </summary>
    public void StampForInsert(BaseEntity entity)
    {
        var now = DateTime.Now;
        var user = _currentUser.UserName;

        // 只在未赋值时才覆盖，避免业务层显式指定时被覆盖
        entity.CreatedDate ??= now;
        entity.CreatedBy ??= user;
        entity.UpdatedDate ??= now;
        entity.UpdatedBy ??= user;
    }

    /// <summary>
    /// 更新前打戳：强制刷新 UpdatedDate、UpdatedBy（始终覆盖，确保记录最新操作人）。
    /// </summary>
    public void StampForUpdate(BaseEntity entity)
    {
        entity.UpdatedDate = DateTime.Now;
        entity.UpdatedBy = _currentUser.UserName;
    }
}
