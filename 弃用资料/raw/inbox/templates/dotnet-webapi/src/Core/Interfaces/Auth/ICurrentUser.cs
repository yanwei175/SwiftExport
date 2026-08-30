namespace {{ProjectName}}.Core.Interfaces.Auth;

public interface ICurrentUser
{
    string UserName { get; }
    Guid? UserId { get; }
    bool IsAuthenticated { get; }
}
