using System.Diagnostics;
using Microsoft.Extensions.Logging;
using SwiftExport.Application.Commands.Validation;
using SwiftExport.Application.Services.QualityControl.AfterSales.Exceptions;
using SwiftExport.Contracts.DTOs.Errors;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Interfaces.Auditing;
using SwiftExport.Core.Interfaces.Auth;

namespace SwiftExport.Application.Commands;

/// <summary>统一编排草稿命令的授权、异步验证与完成事件；业务事务仍由执行器拥有。</summary>
public sealed class CommandPipeline(CommandRequestValidator validator, ICorrelationContext correlation,
    ICurrentUser user, ILogger<CommandPipeline> logger)
{
    /// <summary>先授权再验证并执行命令，保留模块错误响应及执行器的重放语义。</summary>
    public async Task<ServiceResult<T>> ExecuteAsync<TRequest, T>(TRequest? request, Guid? commandId, string commandType,
        Func<CancellationToken, Task<ServiceResult<T>?>> authorize,
        Func<CancellationToken, Task<ServiceResult<CommandExecutionResult<T>>>> execute, CancellationToken token)
    {
        var watch = Stopwatch.StartNew();
        var outcome = "Failed";
        string? errorCode = null;
        string? exceptionType = null;
        using var scope = logger.BeginScope(new Dictionary<string, object?>
        {
            ["CommandType"] = commandType, ["CommandId"] = commandId,
            ["CorrelationId"] = correlation.CorrelationId, ["ActorUid"] = user.UserId
        });
        try
        {
            token.ThrowIfCancellationRequested();
            var refusal = await authorize(token);
            if (refusal is not null)
            {
                errorCode = refusal.Error?.Code;
                outcome = Classify(errorCode);
                return refusal;
            }
            await validator.ValidateAsync(request, token);
            var result = await execute(token);
            if (!result.Success)
            {
                errorCode = result.Error?.Code;
                outcome = Classify(errorCode);
                return ServiceResult<T>.Fail(errorCode ?? ServiceErrorCodes.Unexpected,
                    result.Message, result.Error?.Field);
            }
            outcome = result.Data.IsReplay ? "Replayed" : "Succeeded";
            return ServiceResult<T>.Ok(result.Data.Value);
        }
        catch (CommandValidationException)
        {
            errorCode = ServiceErrorCodes.Validation;
            outcome = "Rejected";
            throw;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            outcome = "Cancelled";
            throw;
        }
        catch (AfterSalesException ex)
        {
            exceptionType = ex.GetType().Name;
            errorCode = ex.Code;
            // 售后仅将现有明确业务拒绝降为 Information；审计失败及未知代码仍保留技术故障级别。
            outcome = ex.Code switch
            {
                ServiceErrorCodes.Validation or ServiceErrorCodes.NotFound or
                    ServiceErrorCodes.VersionConflict or ServiceErrorCodes.BusinessBlocked => "Rejected",
                _ => "Failed"
            };
            throw;
        }
        catch (Exception ex)
        {
            exceptionType = ex.GetType().Name;
            errorCode = ex is BusinessCommandException business ? business.Code : ServiceErrorCodes.Unexpected;
            outcome = Classify(errorCode);
            throw;
        }
        finally
        {
            watch.Stop();
            // 完成事件不附加异常对象，堆栈由异常边界或原转换方记录。
            var level = outcome switch
            {
                "Busy" or "Unknown" => LogLevel.Warning,
                "Cancelled" => LogLevel.Information,
                "Failed" => LogLevel.Error,
                _ => LogLevel.Information
            };
            try
            {
                logger.Log(level, "命令 {CommandType} 执行结束，CommandId={CommandId}，CorrelationId={CorrelationId}，ActorUid={ActorUid}，结果 {Outcome}，耗时 {ElapsedMs} ms，错误码 {ErrorCode}，异常类型 {ExceptionType}",
                    commandType, commandId, correlation.CorrelationId, user.UserId, outcome, watch.ElapsedMilliseconds, errorCode, exceptionType);
            }
            catch
            {
                // 日志提供器故障不能遮蔽原异常，也不能改变已确认提交的业务结果。
            }
        }
    }

    /// <summary>按稳定错误代码区分业务拒绝、锁繁忙、结果不明和技术故障。</summary>
    private static string Classify(string? code) => code switch
    {
        ServiceErrorCodes.CommandBusy => "Busy",
        ServiceErrorCodes.OutcomeUnknown => "Unknown",
        ServiceErrorCodes.Unexpected or null => "Failed",
        _ => "Rejected"
    };
}
