using SwiftExport.Contracts.DTOs.SystemDtos.SystemParameters;
using SwiftExport.Core.Entities.Systems.SystemParameters;
using SwiftExport.Core.Interfaces.Systems.SystemParameters;

namespace SwiftExport.Application.Tests.Files;

/// <summary>隔离业务数据库的参数替身，支持读取次数、版本竞争及上传批次快照测试。</summary>
internal sealed class SystemParameterRepositoryStub(string? value = "https://example.test/erp/files/ProductImages/") : ISystemParameterRepository
{
    private readonly object _sync = new();
    public string? Value { get; set; } = value;
    public int Version { get; set; } = value is null ? 0 : 1;
    public int Reads { get; private set; }
    public int Writes { get; private set; }
    public bool FailWrites { get; set; }

    /// <inheritdoc />
    public Task<SystemParameter?> FindAsync(string key, CancellationToken cancellationToken = default)
    {
        Reads++;
        return Task.FromResult(Value is null ? null : new SystemParameter
        { ParameterKey = key, ParameterValue = Value, Version = Version });
    }
    /// <inheritdoc />
    public Task<bool> InsertAsync(SystemParameter parameter, CancellationToken cancellationToken = default)
    {
        lock (_sync)
        {
            Writes++;
            if (FailWrites) throw new InvalidOperationException("模拟存储失败");
            if (Version != 0) return Task.FromResult(false);
            Value = parameter.ParameterValue; Version = parameter.Version = 1;
            return Task.FromResult(true);
        }
    }
    /// <inheritdoc />
    public Task<bool> UpdateAsync(SystemParameter parameter, int expectedVersion, CancellationToken cancellationToken = default)
    {
        lock (_sync)
        {
            Writes++;
            if (FailWrites) throw new InvalidOperationException("模拟存储失败");
            if (Version != expectedVersion) return Task.FromResult(false);
            Value = parameter.ParameterValue; parameter.Version = ++Version;
            return Task.FromResult(true);
        }
    }
}
