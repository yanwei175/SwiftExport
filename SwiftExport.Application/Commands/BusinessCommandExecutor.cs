using System.Security.Cryptography;
using System.Data;
using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using SwiftExport.Application.Contexts;
using SwiftExport.Contracts.DTOs.Errors;
using SwiftExport.Core.Entities.Commands;
using SwiftExport.Core.Interfaces.Commands;
using SwiftExport.Core.Interfaces.Auth;

namespace SwiftExport.Application.Commands;

/// <summary>在最外层 Application 用例中协调命令锁、回执、业务写入和事务提交。</summary>
public sealed class BusinessCommandExecutor(ServiceBaseContext context, ICommandReceiptRepository receipts,
    ICurrentUser user)
{
    /// <summary>当前支持的输入指纹规范化版本。</summary>
    public const int FingerprintVersion = 1;
    /// <summary>当前支持的成功结果结构版本。</summary>
    public const int ResultVersion = 1;

    /// <summary>先核对回执；首次执行时让业务委托在同一事务中写入事实与审计。</summary>
    public async Task<CommandExecutionResult<T>> ExecuteAsync<T>(Guid commandId, string commandType, Guid? targetUid, object businessInput,
        Func<CancellationToken, Task<T>> action, CancellationToken token, IsolationLevel? isolationLevel = null)
    {
        token.ThrowIfCancellationRequested();
        if (!user.IsAuthenticated || user.UserId is not { } actorUid)
            throw new BusinessCommandException(ServiceErrorCodes.Unauthenticated, "登录状态无效，请重新登录。");
        if (commandId == Guid.Empty || string.IsNullOrWhiteSpace(commandType) || commandType.Length > 100)
            throw new BusinessCommandException(ServiceErrorCodes.Validation, "命令身份或类型无效。", "CommandId");
        if (context.Session.Transaction is not null)
            throw new BusinessCommandException(ServiceErrorCodes.BusinessBlocked, "当前请求已有业务事务，不能独立执行命令。");

        // 模块明确选择业务输入；调用标识和 CommandId 不进入内容指纹。
        var fingerprint = Convert.ToHexString(SHA256.HashData(CanonicalBytes(
            new { FingerprintVersion, ActorUid = actorUid, CommandType = commandType,
                RequestTargetUid = targetUid, BusinessInput = businessInput })));
        // 需要快照前互斥的实现先取得租约；SQL Server 没有此可选能力，仍在原事务内取得命令锁。
        IAsyncDisposable? lease = null;
        if (receipts is ICommandTransactionPreparation preparation)
        {
            lease = await preparation.AcquireBeforeTransactionAsync(commandId, token);
            if (lease is null)
                throw new BusinessCommandException(ServiceErrorCodes.CommandBusy, "命令正在执行，请使用原 CommandId 重试。", "CommandId");
        }
        // 声明顺序保证业务事务先释放，再释放准备租约；BeginTransaction 失败也不会遗留租约。
        await using var preparedCommand = lease;
        token.ThrowIfCancellationRequested();
        // 单据可声明原有隔离级别；未指定时保留现有命令的连接默认隔离行为。
        var transaction = isolationLevel.HasValue
            ? context.Session.Connection.BeginTransaction(isolationLevel.Value)
            : context.Session.Connection.BeginTransaction();
        context.Session.Transaction = transaction;
        try
        {
            if (await receipts.LockAsync(commandId, token) < 0)
                throw new BusinessCommandException(ServiceErrorCodes.CommandBusy, "命令正在执行，请使用原 CommandId 重试。", "CommandId");
            var prior = await receipts.FindAsync(commandId, token);
            if (prior is not null)
            {
                if (prior.FingerprintVersion != FingerprintVersion)
                    throw new BusinessCommandException(ServiceErrorCodes.OutcomeUnknown,
                        "历史命令指纹格式无法识别，请核实执行结果。");
                if (prior.ActorUid != actorUid || prior.CommandType != commandType || prior.RequestTargetUid != targetUid
                    || prior.RequestFingerprint != fingerprint)
                    throw new BusinessCommandException(ServiceErrorCodes.CommandConflict,
                        "COMMAND_CONFLICT：CommandId 已用于不同用户、操作、目标或内容。", "CommandId");
                if (prior.ResultVersion != ResultVersion)
                    throw new BusinessCommandException(ServiceErrorCodes.OutcomeUnknown,
                        "历史命令结果格式无法识别，请核实执行结果。");
                T replay;
                try { replay = JsonSerializer.Deserialize<T>(prior.ResultJson)!; }
                catch (JsonException)
                {
                    throw new BusinessCommandException(ServiceErrorCodes.OutcomeUnknown,
                        "历史命令结果无法读取，请核实执行结果。");
                }
                if (replay is null)
                    throw new BusinessCommandException(ServiceErrorCodes.OutcomeUnknown,
                        "历史命令结果缺失，请核实执行结果。");
                token.ThrowIfCancellationRequested();
                transaction.Commit();
                return new CommandExecutionResult<T>(replay, true);
            }

            var result = await action(token);
            await receipts.InsertAsync(new CommandReceipt
            {
                CommandId = commandId, ActorUid = actorUid, CommandType = commandType,
                RequestTargetUid = targetUid, FingerprintVersion = FingerprintVersion,
                RequestFingerprint = fingerprint, ResultVersion = ResultVersion,
                ResultJson = JsonSerializer.Serialize(result), RecordedAtUtc = DateTime.UtcNow
            }, token);
            token.ThrowIfCancellationRequested();
            try { transaction.Commit(); }
            catch (Exception ex)
            {
                TryLogFailure(LogLevel.Error, ex, commandType, commandId, "提交结果无法确认");
                throw new BusinessCommandException(ServiceErrorCodes.OutcomeUnknown,
                    "命令提交结果无法确认，请使用原 CommandId 查询或重试。");
            }
            return new CommandExecutionResult<T>(result, false);
        }
        catch
        {
            // SQL Server 可能已终止事务，清理失败不能遮蔽原异常。
            try { if (transaction.Connection is not null) transaction.Rollback(); }
            catch (Exception ex)
            {
                TryLogFailure(LogLevel.Warning, ex, commandType, commandId, "回滚清理失败");
            }
            throw;
        }
        finally
        {
            // Dispose 只是清理，不能覆盖原异常或已确认的提交结果；失败时关闭连接，避免未结束事务继续使用。
            try { transaction.Dispose(); }
            catch (Exception ex)
            {
                TryLogFailure(LogLevel.Error, ex, commandType, commandId, "事务释放失败，关闭连接");
                try { context.Session.Connection.Dispose(); }
                catch (Exception closeError)
                {
                    TryLogFailure(LogLevel.Error, closeError, commandType, commandId, "连接释放失败");
                }
            }
            finally { context.Session.Transaction = null; }
        }
    }

    /// <summary>错误诊断尽力记录；日志提供者失效不得改变命令结果或阻止数据库清理。</summary>
    private void TryLogFailure(LogLevel level, Exception exception, string commandType, Guid commandId, string phase)
    {
        try
        {
            context.LoggerFactory.CreateLogger<BusinessCommandExecutor>().Log(level, exception,
                "命令 {CommandType}/{CommandId} {Phase}。", commandType, commandId, phase);
        }
        catch { /* 不通过同一失效日志管道再次记录，保留命令原始结果。 */ }
    }

    /// <summary>按属性名固定对象顺序，并消除等值 decimal 的尾随零；数组顺序保持业务原义。</summary>
    private static byte[] CanonicalBytes<T>(T value)
    {
        var element = JsonSerializer.SerializeToElement(value);
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream)) WriteCanonical(writer, element);
        return stream.ToArray();
    }

    /// <summary>递归写入稳定 JSON；不合并 null、空串或具有业务顺序的明细数组。</summary>
    private static void WriteCanonical(Utf8JsonWriter writer, JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var property in element.EnumerateObject().OrderBy(x => x.Name, StringComparer.Ordinal))
                {
                    writer.WritePropertyName(property.Name);
                    WriteCanonical(writer, property.Value);
                }
                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in element.EnumerateArray()) WriteCanonical(writer, item);
                writer.WriteEndArray();
                break;
            case JsonValueKind.Number:
                var raw = element.GetRawText();
                writer.WriteRawValue(decimal.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var number)
                    ? number.ToString("G29", CultureInfo.InvariantCulture) : raw);
                break;
            case JsonValueKind.String: writer.WriteStringValue(element.GetString()); break;
            case JsonValueKind.True: writer.WriteBooleanValue(true); break;
            case JsonValueKind.False: writer.WriteBooleanValue(false); break;
            default: writer.WriteNullValue(); break;
        }
    }
}
