namespace SwiftExportClient.Authentication;

public sealed class SessionService(
    ITokenStore tokenStore,
    ICurrentUserService currentUserService) : ISessionService
{
    public event EventHandler<SessionEndedEventArgs>? SessionEnded;

    public void ClearSession()
    {
        tokenStore.Clear();
        currentUserService.Clear();
    }

    public void EndSession(SessionEndReason reason)
    {
        ClearSession();
        SessionEnded?.Invoke(this, new SessionEndedEventArgs(reason));
    }
}
