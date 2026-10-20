using System.Text.Json;
using SwiftExportClient.Features.Procurement.PurchaseOrders.Models;

namespace SwiftExportClient.Features.Procurement.PurchaseOrders.Services;

/// <summary>应用内按用户和API环境保留采购创建未决会话；客户端退出后自然丢弃。</summary>
public sealed class PurchaseDraftPendingSession(string environment = "test")
{
    private readonly object _gate = new();
    private readonly Dictionary<(string Environment, string User), Entry> _pending = [];
    private readonly HashSet<(string Environment, string User)> _sending = [];
    private (string, string) Key(string user) => (environment.TrimEnd('/'), user);
    private sealed record Entry(Guid RequestId, string CommandJson, string SnapshotJson);

    /// <summary>取得独立的显示快照，界面改动不能改变应用内冻结记录。</summary>
    public PurchaseDraftRecoverySnapshot? Find(string user)
    {
        string? json;
        lock (_gate) json = _pending.GetValueOrDefault(Key(user))?.SnapshotJson;
        return json is null ? null : JsonSerializer.Deserialize<PurchaseDraftRecoverySnapshot>(json);
    }
    /// <summary>首次冻结或更新原会话反馈；其他请求身份及同ID不同负载不能覆盖。</summary>
    public bool TrySave(string user, PurchaseDraftRecoverySnapshot snapshot)
    {
        var json = JsonSerializer.Serialize(snapshot);
        lock (_gate)
        {
            var key = Key(user); var existing = _pending.GetValueOrDefault(key);
            if (existing is not null && (existing.RequestId != snapshot.ExecutionRequestId || existing.CommandJson != snapshot.CommandJson)) return false;
            _pending[key] = new(snapshot.ExecutionRequestId, snapshot.CommandJson, json);
            return true;
        }
    }
    /// <summary>只清除已取得确定结果的对应会话，不影响其他未决请求。</summary>
    public void Clear(string user, Guid requestId)
    {
        lock (_gate) { var key = Key(user); if (_pending.GetValueOrDefault(key)?.RequestId == requestId) _pending.Remove(key); }
    }
    /// <summary>同一用户环境仅允许一个发送入口，包括多个弹窗的原请求重试。</summary>
    public bool TryBeginSend(string user) { lock (_gate) return _sending.Add(Key(user)); }
    /// <summary>结束在途占用，未知结果的会话仍然保留。</summary>
    public void EndSend(string user) { lock (_gate) _sending.Remove(Key(user)); }
}
