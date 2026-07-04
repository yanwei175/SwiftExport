namespace SwiftExportClient.Authentication;

public interface ISessionService
{
    event EventHandler<SessionEndedEventArgs>? SessionEnded;

    void ClearSession();

    void EndSession(SessionEndReason reason);
}
