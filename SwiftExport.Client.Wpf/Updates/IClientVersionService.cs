namespace SwiftExportClient.Updates;

public interface IClientVersionService
{
    Task<ClientVersionCheckResult> CheckAsync();
}
