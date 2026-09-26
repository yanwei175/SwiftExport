namespace SwiftExport.Application.Services.Files.ProductImages;

/// <summary>为当前进程内同名目标提供大小写不敏感互斥，并在最后一个使用者离开后移除锁条目。</summary>
public sealed class ProductImageTargetLockManager
{
    private readonly object _gate = new();
    private readonly Dictionary<string, LockEntry> _entries = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>异步取得目标锁；等待取消时不会遗留永久条目。</summary>
    public async ValueTask<IDisposable> AcquireAsync(
        string targetFileName,
        CancellationToken cancellationToken)
    {
        LockEntry entry;
        lock (_gate)
        {
            if (!_entries.TryGetValue(targetFileName, out entry!))
            {
                entry = new LockEntry();
                _entries.Add(targetFileName, entry);
            }

            entry.References++;
        }

        try
        {
            await entry.Semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
            return new Releaser(this, targetFileName, entry);
        }
        catch
        {
            RemoveReference(targetFileName, entry);
            throw;
        }
    }

    private void Release(string targetFileName, LockEntry entry)
    {
        entry.Semaphore.Release();
        RemoveReference(targetFileName, entry);
    }

    /// <summary>只在字典仍指向同一条目且没有持有者或等待者时清除。</summary>
    private void RemoveReference(string targetFileName, LockEntry entry)
    {
        lock (_gate)
        {
            entry.References--;
            if (entry.References == 0 &&
                _entries.TryGetValue(targetFileName, out var current) &&
                ReferenceEquals(current, entry))
            {
                _entries.Remove(targetFileName);
                entry.Semaphore.Dispose();
            }
        }
    }

    private sealed class LockEntry
    {
        public SemaphoreSlim Semaphore { get; } = new(1, 1);
        public int References { get; set; }
    }

    private sealed class Releaser(
        ProductImageTargetLockManager owner,
        string targetFileName,
        LockEntry entry) : IDisposable
    {
        private int _released;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0)
                owner.Release(targetFileName, entry);
        }
    }
}
