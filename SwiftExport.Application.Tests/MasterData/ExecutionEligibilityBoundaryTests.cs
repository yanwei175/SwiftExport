using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Application.Services.MasterData;
using SwiftExport.Contracts.DTOs.MasterData.ExecutionEligibility;
using SwiftExport.Contracts.DTOs.Products;
using SwiftExport.Contracts.DTOs.Customers;
using SwiftExport.Contracts.DTOs.Suppliers;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.Core.Interfaces.Auth;
using SwiftExport.Infrastructure.Repositories.Products;
using SwiftExport.Infrastructure.Repositories.Customers;
using SwiftExport.Infrastructure.Repositories.Suppliers;

namespace SwiftExport.Application.Tests.MasterData;

/// <summary>保护旧资料保存不能越过资格命令，以及未经认证请求不能进入事务。</summary>
[TestClass]
public sealed class ExecutionEligibilityBoundaryTests
{
    /// <summary>验证实际仓储生成的普通 UPDATE 列集排除 IsActive，同时仍保留资料字段和新增默认值。</summary>
    [TestMethod]
    public void OrdinaryRepositoryUpdatesCannotChangeEligibility()
    {
        AssertUpdateBoundary(typeof(ProductRepository), "Sku");
        AssertUpdateBoundary(typeof(CustomerRepository), "CustomerCode");
        AssertUpdateBoundary(typeof(SupplierRepository), "SupplierCode");
    }

    /// <summary>旧 DTO 不新增可绑定的资格状态，新增命令也不能接收用户或角色字段。</summary>
    [TestMethod]
    public void LegacyDtosAndDedicatedCommandKeepSeparateWriteSurfaces()
    {
        foreach (var type in new[] { typeof(ProductDto), typeof(CustomerDto), typeof(SupplierDto) })
            Assert.IsNull(type.GetProperty("IsActive"), type.FullName);
        var properties = typeof(SetExecutionEligibilityStatusCommand).GetProperties().Select(p => p.Name).ToArray();
        CollectionAssert.AreEquivalent(new[] { "Kind", "Uid", "ExpectedVersion", "IsActive", "Reason" }, properties);
    }

    /// <summary>缺少真实用户标识时即使认证标志存在，也应在访问数据库前拒绝。</summary>
    [TestMethod]
    public async Task AuthenticatedWithoutUserIdIsRejectedBeforeDatabaseAccess()
    {
        var service = CreateWithoutDatabase(new TestUser(true, null));
        var result = await service.SetStatusAsync(ValidCommand());
        Assert.IsFalse(result.Success);
        Assert.AreEqual("UNAUTHENTICATED", result.Error.Code);
    }

    /// <summary>未认证请求不能凭伪造的用户编号执行管理员动作。</summary>
    [TestMethod]
    public async Task UnauthenticatedUserIdIsRejectedBeforeDatabaseAccess()
    {
        var result = await CreateWithoutDatabase(new TestUser(false, Guid.NewGuid())).SetStatusAsync(ValidCommand());
        Assert.IsFalse(result.Success);
        Assert.AreEqual("UNAUTHENTICATED", result.Error.Code);
    }

    /// <summary>未定义对象、无效版本和无原因请求均不能进入数据库或枚举动态 SQL 分支。</summary>
    [TestMethod]
    public async Task InvalidCommandIsRejectedBeforeDatabaseAccess()
    {
        var service = CreateWithoutDatabase(new TestUser(true, Guid.NewGuid()));
        var commands = new[] { ValidCommand(), ValidCommand(), ValidCommand(), ValidCommand() };
        commands[0].Kind = (ExecutionEligibilityKind)99;
        commands[1].ExpectedVersion = 0;
        commands[2].ExpectedVersion = int.MaxValue;
        commands[3].Reason = " ";
        foreach (var command in commands)
        {
            var result = await service.SetStatusAsync(command);
            Assert.IsFalse(result.Success);
            Assert.AreEqual("VALIDATION", result.Error.Code);
        }
    }

    /// <summary>不初始化数据库上下文，使任何越过早期守卫的数据库访问直接导致测试失败。</summary>
    private static ExecutionEligibilityService CreateWithoutDatabase(ICurrentUser user)
        => new(null!, null!, null!, user, null!);

    private static SetExecutionEligibilityStatusCommand ValidCommand() => new()
    {
        Kind = ExecutionEligibilityKind.Product, Uid = Guid.NewGuid(), ExpectedVersion = 1,
        IsActive = false, Reason = "暂停新增业务"
    };

    /// <summary>只读取真实仓储列元数据，不调用构造函数，也不创建或模拟数据库连接。</summary>
    private static void AssertUpdateBoundary(Type repositoryType, string businessProperty)
    {
        var repository = RuntimeHelpers.GetUninitializedObject(repositoryType);
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var update = (IReadOnlyList<string>)repositoryType.GetProperty("UpdateProperties", flags)!.GetValue(repository)!;
        var insert = (IReadOnlyList<string>)repositoryType.GetProperty("InsertProperties", flags)!.GetValue(repository)!;
        Assert.IsFalse(update.Contains("IsActive"));
        Assert.IsTrue(update.Contains(businessProperty));
        Assert.IsTrue(insert.Contains("IsActive"));
    }

    /// <summary>提供明确认证和用户编号组合，避免测试依赖 Web 身份实现。</summary>
    private sealed class TestUser(bool authenticated, Guid? userId) : ICurrentUser
    {
        public string UserName => "资格测试用户";
        public Guid? UserId => userId;
        public bool IsAuthenticated => authenticated;
    }
}
