using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Application.Interfaces.Auth.Passwords;
using SwiftExport.Application.Services.Auth.Passwords;
using SwiftExport.Tools.AdminPasswordRecovery;

namespace SwiftExport.Application.Tests.Database;

/// <summary>不连接数据库，验证真实恢复执行器的输入边界、状态映射及失败传播。</summary>
[TestClass]
public sealed class PostgreSqlAdminPasswordRecoveryMigrationTests
{
    /// <summary>无效用户名或密码在哈希和存储之前拒绝，不改变共同密码规则。</summary>
    [TestMethod]
    [DataRow(null, "valid-input", "valid-input")]
    [DataRow("   ", "valid-input", "valid-input")]
    [DataRow("admin", null, null)]
    [DataRow("admin", "   ", "   ")]
    [DataRow("admin", "short", "short")]
    [DataRow("admin", "valid-input", "different")]
    public async Task InvalidInput_NeverHashesOrCallsStore(string? user, string? password, string? confirmation)
    {
        var store = new RecordingStore();
        var hasher = new RecordingHasher();
        var result = await new AdminPasswordRecoveryExecutor(store, hasher).ExecuteAsync(user, password, confirmation);
        Assert.IsFalse(result.Success);
        Assert.AreEqual(0, store.Calls);
        Assert.AreEqual(0, hasher.Calls);
    }

    /// <summary>四种存储结果保持原成功边界；只裁剪用户名，不裁剪密码，并传递同一取消令牌。</summary>
    [TestMethod]
    [DataRow(AdminPasswordRecoveryStatus.Updated, true)]
    [DataRow(AdminPasswordRecoveryStatus.TargetNotFound, false)]
    [DataRow(AdminPasswordRecoveryStatus.TargetNotAdmin, false)]
    [DataRow(AdminPasswordRecoveryStatus.ConcurrencyConflict, false)]
    public async Task StatusAndInput_PreservePolicy(AdminPasswordRecoveryStatus status, bool success)
    {
        using var source = new CancellationTokenSource();
        var store = new RecordingStore { Status = status };
        var hasher = new RecordingHasher();
        var result = await new AdminPasswordRecoveryExecutor(store, hasher)
            .ExecuteAsync("  admin  ", " valid-input ", " valid-input ", source.Token);
        Assert.AreEqual(success, result.Success);
        Assert.AreEqual("admin", store.UserName);
        Assert.IsTrue(hasher.Input == " valid-input ", "密码内容不能裁剪。");
        Assert.IsTrue(store.Hash == hasher.Output, "存储必须接收哈希结果。");
        Assert.AreEqual(1, hasher.Calls);
        Assert.AreEqual(1, store.Calls);
        Assert.AreEqual(source.Token, store.Token);
        Assert.IsFalse(result.Message.Contains(hasher.Output, StringComparison.Ordinal));
    }

    /// <summary>取消和数据库失败原样传播，不将工具失败转成业务成功或自动重试。</summary>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task StoreFailure_IsNotSwallowedOrRetried(bool canceled)
    {
        using var source = new CancellationTokenSource();
        var failure = canceled ? (Exception)new OperationCanceledException(source.Token) : new InvalidOperationException("测试存储失败");
        var store = new RecordingStore { Failure = failure };
        var executor = new AdminPasswordRecoveryExecutor(store, new RecordingHasher());
        Exception? observed = null;
        try { await executor.ExecuteAsync("admin", "valid-input", "valid-input", source.Token); }
        catch (Exception error) { observed = error; }
        Assert.AreSame(failure, observed);
        Assert.AreEqual(1, store.Calls);
    }

    /// <summary>哈希失败发生在存储之前，不能在失败后写入凭据。</summary>
    [TestMethod]
    public async Task HasherFailure_NeverWrites()
    {
        var failure = new InvalidOperationException("测试哈希失败");
        var store = new RecordingStore();
        var hasher = new RecordingHasher { Failure = failure };
        Exception? observed = null;
        try { await new AdminPasswordRecoveryExecutor(store, hasher).ExecuteAsync("admin", "valid-input", "valid-input"); }
        catch (Exception error) { observed = error; }
        Assert.AreSame(failure, observed);
        Assert.AreEqual(0, store.Calls);
    }

    /// <summary>真实 BCrypt 保留密码空格，验证损坏哈希按原认证失败处理，不输出凭据。</summary>
    [TestMethod]
    public void RealHasher_KeepsPasswordAndRejectsCorruption()
    {
        var hasher = new BcryptPasswordHasher();
        var hash = hasher.Hash(" valid-input ");
        Assert.IsTrue(hasher.Verify(" valid-input ", hash));
        Assert.IsFalse(hasher.Verify("valid-input", hash));
        Assert.IsFalse(hasher.Verify("valid-input", "invalid-format"));
    }

    /// <summary>录制真实执行器的窄存储端口调用，不替代 PostgreSQL 并发或 SQL 验收。</summary>
    private sealed class RecordingStore : IAdminPasswordRecoveryStore
    {
        internal int Calls;
        internal string? UserName;
        internal string? Hash;
        internal CancellationToken Token;
        internal AdminPasswordRecoveryStatus Status;
        internal Exception? Failure;

        /// <summary>记录参数并返回指定业务状态，失败实例保持原样。</summary>
        public Task<AdminPasswordRecoveryStatus> ResetExistingAdminAsync(string userName, string passwordHash, CancellationToken cancellationToken = default)
        {
            Calls++; UserName = userName; Hash = passwordHash; Token = cancellationToken;
            return Failure is null ? Task.FromResult(Status) : Task.FromException<AdminPasswordRecoveryStatus>(Failure);
        }
    }

    /// <summary>观察密码进入哈希前的内容与调用次数，不泄露哈希到断言输出。</summary>
    private sealed class RecordingHasher : IPasswordHasher
    {
        internal int Calls;
        internal string? Input;
        internal Exception? Failure;
        internal string Output = Guid.NewGuid().ToString("N");

        /// <summary>记录输入，按测试需要返回不透明结果或抛原异常。</summary>
        public string Hash(string password)
        {
            Calls++; Input = password;
            if (Failure is not null) throw Failure;
            return Output;
        }

        /// <summary>此观察端口不参与密码验证，误用时立即失败。</summary>
        public bool Verify(string password, string passwordHash) => throw new NotSupportedException();
    }
}

