using {{ProjectName}}.Core.Common;
using {{ProjectName}}.Core.Interfaces.Auth;

namespace {{ProjectName}}.Infrastructure.Services.Auth;

public class AuditStampService(ICurrentUser currentUser) : IAuditStampService
{
    public void StampForInsert(BaseEntity entity)
    {
        var now = DateTime.Now;
        entity.CreatedDate ??= now;
        entity.CreatedBy ??= currentUser.UserName;
        entity.UpdatedDate ??= now;
        entity.UpdatedBy ??= currentUser.UserName;
    }

    public void StampForUpdate(BaseEntity entity)
    {
        entity.UpdatedDate = DateTime.Now;
        entity.UpdatedBy = currentUser.UserName;
    }
}
