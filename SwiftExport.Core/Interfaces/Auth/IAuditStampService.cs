using SwiftExport.Core.Common;

namespace SwiftExport.Core.Interfaces.Auth;

/// <summary>
/// 审计字段自动打戳服务，在仓储层统一调用，业务层无需手动赋值。
/// </summary>
public interface IAuditStampService
{
    /// <summary>新增前打戳：填充 CreatedDate/CreatedBy/UpdatedDate/UpdatedBy</summary>
    void StampForInsert(BaseEntity entity);

    /// <summary>更新前打戳：填充 UpdatedDate/UpdatedBy</summary>
    void StampForUpdate(BaseEntity entity);
}
