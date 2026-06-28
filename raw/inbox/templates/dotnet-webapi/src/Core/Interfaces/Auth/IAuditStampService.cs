using {{ProjectName}}.Core.Common;

namespace {{ProjectName}}.Core.Interfaces.Auth;

public interface IAuditStampService
{
    void StampForInsert(BaseEntity entity);
    void StampForUpdate(BaseEntity entity);
}
