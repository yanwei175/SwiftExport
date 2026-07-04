using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using SwiftExportClient.Authentication;

namespace SwiftExportClient.Services.Api;

public sealed class AuthTokenHandler(
    ITokenStore tokenStore,
    ISessionService sessionService) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var token = tokenStore.GetAccessToken();
        if (!string.IsNullOrWhiteSpace(token))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        var response = await base.SendAsync(request, cancellationToken);
        if (response.StatusCode is HttpStatusCode.Unauthorized
            && !IsAnonymousAuthEndpoint(request.RequestUri))
        {
            sessionService.EndSession(SessionEndReason.Unauthorized);
        }

        return response;
    }

    private static bool IsAnonymousAuthEndpoint(Uri? uri)
    {
        var path = uri?.AbsolutePath ?? string.Empty;
        return path.EndsWith("/api/auth/login", StringComparison.OrdinalIgnoreCase)
               || path.EndsWith("/api/auth/reset-password", StringComparison.OrdinalIgnoreCase)
               || path.EndsWith("/api/client-version", StringComparison.OrdinalIgnoreCase);
    }
}
