namespace SwiftExportClient.Authentication;

public interface ITokenStore
{
    bool HasAccessToken { get; }

    string? GetAccessToken();

    void SetAccessToken(string accessToken);

    void Clear();
}
