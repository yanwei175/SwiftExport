using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using SwiftExport.Contracts.Common;
using SwiftExport.Contracts.DTOs.DemandPlanning.NetDemand;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.Auditing;
using SwiftExport.Core.Interfaces.Auditing;
using SwiftExport.Core.Interfaces.Auth;
using SwiftExport.Infrastructure.Data;

namespace SwiftExport.Application.Services.DemandPlanning.Materials;

/// <summary>仅复用建立与回退既有事务清理、审计重放和指纹代码，不决定物料业务规则。</summary>
internal sealed class MaterialDemandCommandSupport(
    DbSession session,
    IBusinessOperationLogRepository operationLogs,
    ICorrelationContext correlationContext,
    ICurrentUser currentUser,
    ILogger logger)
{
    private const string Module = "MaterialDemand";
    private readonly DbSession _session = session;
    private readonly ILogger _logger = logger;

    /// <summary>为单条物料命令保留原事务边界、错误映射及失败清理；拒绝接管已有事务。</summary>
    public async Task<ServiceResult<T>> ExecuteAsync<T>(
        Func<Task<T>> action,
        string failureMessage,
        CancellationToken cancellationToken)
    {
        if (_session.Transaction is not null)
            return ServiceResult<T>.Fail("TRANSACTION_ALREADY_ACTIVE", "当前请求已存在未完成事务。");
        _session.Transaction = _session.Connection.BeginTransaction();
        try
        {
            var result = await action();
            _session.Transaction.Commit();
            return ServiceResult<T>.Ok(result);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _session.Transaction.Rollback();
            throw;
        }
        catch (MaterialDemandException ex)
        {
            _session.Transaction.Rollback();
            return ServiceResult<T>.Fail(ex.Code, ex.Message);
        }
        catch (Exception ex)
        {
            _session.Transaction.Rollback();
            _logger.LogError(ex, "{FailureMessage}", failureMessage);
            return ServiceResult<T>.Fail(failureMessage);
        }
        finally
        {
            _session.Transaction.Dispose();
            _session.Transaction = null;
        }
    }

    /// <summary>在当前命令事务中写入既有审计和可重放结果，写入失败使整批回滚。</summary>
    public async Task WriteLogAsync<T>(
        Guid commandId, string operationType, string operationName, string fingerprint,
        T result, Guid aggregateId, string? reason)
    {
        var log = new BusinessOperationLog
        {
            Uid = GuidSequentialGenerator.NewGuid(), CommandId = commandId,
            CorrelationId = string.IsNullOrWhiteSpace(correlationContext.CorrelationId)
                ? commandId.ToString("N") : correlationContext.CorrelationId[..Math.Min(64, correlationContext.CorrelationId.Length)],
            Module = Module, AggregateType = "GrossDemand", AggregateUid = aggregateId,
            OperationType = operationType, OperationName = operationName, Reason = reason,
            Summary = operationName, ChangedFieldsJson = JsonSerializer.Serialize(result), RelatedObjectsJson = fingerprint,
            UserUid = currentUser.UserId, UserNameSnapshot = currentUser.UserName, OccurredDate = DateTime.Now
        };
        if (await operationLogs.InsertAsync(log) != 1)
            throw new MaterialDemandException("LOG_WRITE_FAILED", "物料需求幂等日志写入失败。");
    }

    /// <summary>核对原命令指纹并读取既有审计结果；本批保持原重放协议。</summary>
    public static T Replay<T>(BusinessOperationLog prior, string fingerprint)
    {
        if (!string.Equals(prior.RelatedObjectsJson, fingerprint, StringComparison.Ordinal))
            throw new MaterialDemandException("COMMAND_REUSED", "同一 CommandId 已用于其他物料需求内容。");
        return JsonSerializer.Deserialize<T>(prior.ChangedFieldsJson ?? string.Empty)
            ?? throw new MaterialDemandException("IDEMPOTENCY_RESULT_INVALID", "历史幂等结果无法读取。");
    }

    /// <summary>按来源身份排序并规范化回退原因，生成建立或回退命令的既有指纹。</summary>
    public static string CreateFingerprint(object command)
    {
        // 幂等指纹按来源身份排序，并规范化原因；客户端选择顺序变化不应构成不同业务内容。
        var json = command switch
        {
            EstablishMaterialDemandsCommand establish => JsonSerializer.Serialize(new
            {
                Items = establish.Items.OrderBy(item => item.GrossDemandId).Select(item => new
                {
                    item.GrossDemandId, item.SourceDemandVersion, item.SourceVersion
                })
            }),
            RollbackMaterialDemandsCommand rollback => JsonSerializer.Serialize(new
            {
                Reason = rollback.Reason.Trim(),
                Items = rollback.Items.OrderBy(item => item.GrossDemandId).Select(item => new
                {
                    item.GrossDemandId, item.DecompositionRevision
                })
            }),
            _ => JsonSerializer.Serialize(command)
        };
        return JsonSerializer.Serialize(new
        {
            Fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json)))
        });
    }

}

/// <summary>表示物料建立与回退现有错误码对应的业务失败。</summary>
internal sealed class MaterialDemandException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}
