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
    /// <summary>其他注册参数各自保存独立值与版本，防止测试把两个参数误当成同一行。</summary>
    public Dictionary<string, (string Value, int Version)> OtherParameters { get; } = new();

    /// <inheritdoc />
    public Task<SystemParameter?> FindAsync(string key, CancellationToken cancellationToken = default)
    {
        Reads++;
        if (key == SystemParameterKeys.ProductImagesBaseUrl)
            return Task.FromResult(Value is null ? null : new SystemParameter
            { ParameterKey = key, ParameterValue = Value, Version = Version });
        return Task.FromResult(OtherParameters.TryGetValue(key, out var item) ? new SystemParameter
            { ParameterKey = key, ParameterValue = item.Value, Version = item.Version } : null);
    }
    /// <inheritdoc />
    public Task<bool> InsertAsync(SystemParameter parameter, CancellationToken cancellationToken = default)
    {
        lock (_sync)
        {
            Writes++;
            if (FailWrites) throw new InvalidOperationException("模拟存储失败");
            if (parameter.ParameterKey != SystemParameterKeys.ProductImagesBaseUrl)
            {
                if (OtherParameters.ContainsKey(parameter.ParameterKey)) return Task.FromResult(false);
                parameter.Version = 1;
                OtherParameters.Add(parameter.ParameterKey, (parameter.ParameterValue, 1));
                return Task.FromResult(true);
            }
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
            if (parameter.ParameterKey != SystemParameterKeys.ProductImagesBaseUrl)
            {
                if (!OtherParameters.TryGetValue(parameter.ParameterKey, out var item) || item.Version != expectedVersion) return Task.FromResult(false);
                parameter.Version = item.Version + 1;
                OtherParameters[parameter.ParameterKey] = (parameter.ParameterValue, parameter.Version);
                return Task.FromResult(true);
            }
            if (Version != expectedVersion) return Task.FromResult(false);
            Value = parameter.ParameterValue; parameter.Version = ++Version;
            return Task.FromResult(true);
        }
    }
}
