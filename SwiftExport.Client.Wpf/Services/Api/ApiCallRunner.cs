using System.Net;
using System.Net.Http;
using Microsoft.Extensions.Logging;
using Refit;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExportClient.Services.Api;

public sealed class ApiCallRunner(ILogger<ApiCallRunner> logger) : IApiCallRunner
{
    public async Task<ServiceResult<T>> RunAsync<T>(
        Func<Task<ServiceResult<T>>> action,
        string failureMessage = "请求失败")
    {
        try
        {
            return await action() ?? ServiceResult<T>.Fail($"{failureMessage}：服务端返回为空。");
        }
        catch (Exception exception) when (TryMapException<T>(exception, failureMessage, out var result))
        {
            return result;
        }
    }

    public async Task<ServiceResult<T>> RunValueAsync<T>(
        Func<Task<T>> action,
        string successMessage = "成功",
        string failureMessage = "请求失败")
    {
        try
        {
            var value = await action();
            return ServiceResult<T>.Ok(value, successMessage);
        }
        catch (Exception exception) when (TryMapException<T>(exception, failureMessage, out var result))
        {
            return result;
        }
    }

    public async Task<ServiceResult<T>> RunAsync<T>(
        Func<Task<ServiceResult<T>>> action,
        CancellationToken cancellationToken,
        string failureMessage = "请求失败")
    {
        try
        {
            return await action() ?? ServiceResult<T>.Fail($"{failureMessage}：服务端返回为空。");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            logger.LogDebug("API request was canceled by the caller.");
            return ServiceResult<T>.Fail(string.Empty);
        }
        catch (Exception exception) when (TryMapException<T>(exception, failureMessage, out var result))
        {
            return result;
        }
    }

    private bool TryMapException<T>(
        Exception exception,
        string failureMessage,
        out ServiceResult<T> result)
    {
        result = exception switch
        {
            ApiException apiException => MapApiException<T>(apiException, failureMessage),
            HttpRequestException httpException => MapHttpRequestException<T>(httpException, failureMessage),
            TaskCanceledException canceledException => MapTaskCanceledException<T>(canceledException, failureMessage),
            _ => null!
        };

        if (result is null)
        {
            return false;
        }

        return true;
    }

    private ServiceResult<T> MapApiException<T>(ApiException exception, string failureMessage)
    {
        logger.LogWarning(
            exception,
            "API request failed with status code {StatusCode}.",
            exception.StatusCode);

        var statusText = exception.StatusCode == 0
            ? "HTTP 请求失败"
            : $"HTTP {(int)exception.StatusCode}";

        var message = exception.StatusCode switch
        {
            HttpStatusCode.Unauthorized => "登录状态无效或已过期。",
            HttpStatusCode.Forbidden => "当前账号没有执行该操作的权限。",
            HttpStatusCode.NotFound => "请求的接口或资源不存在。",
            HttpStatusCode.BadRequest => "请求参数不正确。",
            _ => $"{failureMessage}：{statusText}。"
        };

        return ServiceResult<T>.Fail(message);
    }

    private ServiceResult<T> MapHttpRequestException<T>(HttpRequestException exception, string failureMessage)
    {
        logger.LogWarning(exception, "HTTP request failed before receiving a response.");
        return ServiceResult<T>.Fail($"{failureMessage}：无法连接到服务器。");
    }

    private ServiceResult<T> MapTaskCanceledException<T>(TaskCanceledException exception, string failureMessage)
    {
        logger.LogWarning(exception, "HTTP request timed out or was canceled.");
        return ServiceResult<T>.Fail($"{failureMessage}：请求超时或已取消。");
    }
}
