using Microsoft.Extensions.Logging;
using Refit;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Contracts.DTOs.Errors;
using System.Net.Http;

namespace SwiftExportClient.Services.Api;

/// <summary>统一执行客户端 API 调用，并把 HTTP、网络、超时和调用者取消转换为稳定结果。</summary>
public sealed class ApiCallRunner(ILogger<ApiCallRunner> logger) : IApiCallRunner
{
    /// <summary>执行不带显式调用者取消语义的 API 请求。</summary>
    public async Task<ServiceResult<T>> RunAsync<T>(
        Func<Task<ServiceResult<T>>> action,
        string failureMessage = "请求失败")
    {
        try
        {
            return await action() ?? ServiceResult<T>.Fail(ServiceErrorCodes.OutcomeUnknown, $"{failureMessage}：服务端返回为空。");
        }
        catch (Exception exception) when (TryMapException<T>(exception, failureMessage, out var result))
        {
            return result;
        }
    }

    /// <summary>执行返回裸值的 API 请求，并包装为统一服务结果。</summary>
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

    /// <summary>执行可取消 API 请求；调用者主动取消时返回无提示失败，不把取消暴露为技术异常。</summary>
    public async Task<ServiceResult<T>> RunAsync<T>(
        Func<Task<ServiceResult<T>>> action,
        CancellationToken cancellationToken,
        string failureMessage = "请求失败")
    {
        try
        {
            return await action() ?? ServiceResult<T>.Fail(ServiceErrorCodes.OutcomeUnknown, $"{failureMessage}：服务端返回为空。");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            logger.LogDebug("API request was canceled by the caller.");
            return ServiceResult<T>.Fail(string.Empty);
        }
        catch (ApiRequestException exception) when (
            cancellationToken.IsCancellationRequested && ContainsCancellation(exception))
        {
            // Refit 13 会把 HttpClient 的 TaskCanceledException 包装成 ApiRequestException。
            logger.LogDebug("Refit API request was canceled by the caller.");
            return ServiceResult<T>.Fail(string.Empty);
        }
        catch (ApiException) when (cancellationToken.IsCancellationRequested)
        {
            // Refit 响应缓冲取消后仍可能尝试反序列化，将取消包装成“流已消费”。
            // 以调用者 Token 为准，不依赖消息文本或已被覆盖的内部异常；未取消的响应错误仍正常报告。
            logger.LogDebug("Refit response reading was canceled by the caller.");
            return ServiceResult<T>.Fail(string.Empty);
        }
        catch (Exception exception) when (TryMapException<T>(exception, failureMessage, out var result))
        {
            return result;
        }
    }

    /// <summary>按异常类型识别传输错误；编程错误继续抛出。</summary>
    private bool TryMapException<T>(
        Exception exception,
        string failureMessage,
        out ServiceResult<T> result)
    {
        result = exception switch
        {
            ApiRequestException requestException => MapApiRequestException<T>(requestException, failureMessage),
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

    /// <summary>映射 Refit 在收到响应前包装的传输异常；调用者取消已由带 Token 的入口提前处理。</summary>
    private ServiceResult<T> MapApiRequestException<T>(
        ApiRequestException exception,
        string failureMessage)
    {
        if (ContainsCancellation(exception))
        {
            logger.LogWarning(exception, "API request timed out before receiving a response.");
            return ServiceResult<T>.Fail(ServiceErrorCodes.OutcomeUnknown, $"{failureMessage}：请求超时或已取消。");
        }

        logger.LogWarning(exception, "API request failed before receiving a response.");
        return ServiceResult<T>.Fail(ServiceErrorCodes.OutcomeUnknown, $"{failureMessage}：无法连接到服务器。");
    }

    /// <summary>沿异常链识别 Refit 包装的取消，不依赖易变化的异常消息文本。</summary>
    private static bool ContainsCancellation(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is OperationCanceledException) return true;
        }

        return false;
    }

    /// <summary>只记录状态码，结构化业务错误由统一读取器还原，不记录响应正文。</summary>
    private ServiceResult<T> MapApiException<T>(ApiException exception, string failureMessage)
    {
        logger.LogWarning("API request failed with status code {StatusCode}.", exception.StatusCode);
        return ApiErrorResponseReader.Read<T>(exception, failureMessage);
    }

    /// <summary>网络故障不能证明写入是否提交；结果保持未知。</summary>
    private ServiceResult<T> MapHttpRequestException<T>(HttpRequestException exception, string failureMessage)
    {
        logger.LogWarning(exception, "HTTP request failed before receiving a response.");
        return ServiceResult<T>.Fail(ServiceErrorCodes.OutcomeUnknown, $"{failureMessage}：无法连接到服务器。");
    }

    /// <summary>超时不证明命令未执行，保留未知结果语义。</summary>
    private ServiceResult<T> MapTaskCanceledException<T>(TaskCanceledException exception, string failureMessage)
    {
        logger.LogWarning(exception, "HTTP request timed out or was canceled.");
        return ServiceResult<T>.Fail(ServiceErrorCodes.OutcomeUnknown, $"{failureMessage}：请求超时或已取消。");
    }
}
