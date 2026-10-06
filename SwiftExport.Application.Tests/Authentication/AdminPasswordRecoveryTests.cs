using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Application.Interfaces.Auth.Passwords;
using SwiftExport.Tools.AdminPasswordRecovery;

namespace SwiftExport.Application.Tests.Authentication;

/// <summary>验证本机恢复工具只接受数据库确认的既有 Admin 目标。</summary>
[TestClass]
public sealed class AdminPasswordRecoveryTests
{
    /// <summary>非 Admin 目标被拒绝，工具不会把失败误报为成功。</summary>
    [TestMethod]
    public async Task RecoveryRejectsNonAdminTarget()
    {
        var store = new StoreStub { Status = AdminPasswordRecoveryStatus.TargetNotAdmin };
        var result = await new AdminPasswordRecoveryExecutor(store, new HasherStub())
            .ExecuteAsync(" ordinary ", "secret", "secret");
        Assert.IsFalse(result.Success);
        Assert.AreEqual("ordinary", store.UserName);
        Assert.AreEqual("hash:secret", store.PasswordHash);
        StringAssert.Contains(result.Message, "不是活动 Admin");
    }

    /// <summary>密码确认不一致时不生成哈希、不访问数据库。</summary>
    [TestMethod]
    public async Task RecoveryRejectsMismatchedConfirmationBeforeStoreCall()
    {
        var store = new StoreStub();
        var hasher = new HasherStub();
        var result = await new AdminPasswordRecoveryExecutor(store, hasher)
            .ExecuteAsync("admin", "secret", "different");
        Assert.IsFalse(result.Success);
        Assert.IsNull(store.UserName);
        Assert.AreEqual(0, hasher.HashCalls);
    }

    /// <summary>只有存储层确认更新后才报告恢复成功。</summary>
    [TestMethod]
    public async Task RecoveryReportsSuccessOnlyAfterConstrainedUpdate()
    {
        var store = new StoreStub { Status = AdminPasswordRecoveryStatus.Updated };
        var result = await new AdminPasswordRecoveryExecutor(store, new HasherStub())
            .ExecuteAsync("admin", "secret", "secret");
        Assert.IsTrue(result.Success);
    }

    private sealed class StoreStub : IAdminPasswordRecoveryStore
    {
        public AdminPasswordRecoveryStatus Status { get; set; }
        public string? UserName { get; private set; }
        public string? PasswordHash { get; private set; }
        public Task<AdminPasswordRecoveryStatus> ResetExistingAdminAsync(string userName, string passwordHash, CancellationToken cancellationToken = default)
        {
            UserName = userName; PasswordHash = passwordHash;
            return Task.FromResult(Status);
        }
    }

    private sealed class HasherStub : IPasswordHasher
    {
        public int HashCalls { get; private set; }
        public string Hash(string password) { HashCalls++; return "hash:" + password; }
        public bool Verify(string password, string passwordHash) => throw new NotSupportedException();
    }
}
