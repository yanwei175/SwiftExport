using System.Data;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Refit;
using SwiftExport.Application.Contexts;
using SwiftExport.Application.Services.Inventory;
using SwiftExport.Contracts.Apis.Inventory;
using SwiftExport.Contracts.DTOs.Inventory;
using SwiftExport.Contracts.SysEnums.Inventory;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Interfaces.Auth;
using SwiftExport.Core.Interfaces.Inventory;
using SwiftExport.Infrastructure.Data;

namespace SwiftExport.Application.Tests.Inventory;

/// <summary>验证仓库候选查询的输入、权限、事务、窄契约和真实 Refit 请求；SQL 资格另作残余风险说明。</summary>
[TestClass]
public sealed class WarehouseCandidateQueryTests
{
    /// <summary>匿名、空用户、非法分页和取消在查询前拒绝，不能以空结果伪装成功。</summary>
    [TestMethod]
    public async Task IdentityInputAndCancellationAreRejectedBeforeQuery()
    {
        foreach (var user in new[] { new TestUser(false, Guid.NewGuid()), new TestUser(true, null), new TestUser(true, Guid.Empty) })
            await Assert.ThrowsExactlyAsync<UnauthorizedAccessException>(() =>
                new WarehouseCandidateQueryService(null!, new RepositoryStub(), user).SearchAsync(new()));
        var service = new WarehouseCandidateQueryService(null!, new RepositoryStub(), new TestUser(true, Guid.NewGuid()));
        foreach (var request in new[] { null!, new PagedRequestBase { PageIndex = 0 }, new PagedRequestBase { PageSize = 101 },
                     new PagedRequestBase { PageIndex = int.MaxValue, PageSize = 100 }, new PagedRequestBase { Keyword = new string('仓', 101) } })
            await Assert.ThrowsExactlyAsync<ArgumentException>(() => service.SearchAsync(request));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => service.SearchAsync(new(), cancellation.Token));
    }

    /// <summary>成功查询规范化输入并提交只读事务；无权限时不进入数据读取且释放事务。</summary>
    [TestMethod]
    public async Task QueryOwnsTransactionAndSeparatesPermissionFromDataRead()
    {
        var repository = new RepositoryStub { Allowed = true };
        var session = Session(new ConnectionStub());
        var service = new WarehouseCandidateQueryService(
            new ServiceBaseContext(session, NullLoggerFactory.Instance), repository, new TestUser(true, Guid.NewGuid()));
        var result = await service.SearchAsync(new() { Keyword = " 仓库 ", PageIndex = 2, PageSize = 5 });
        Assert.AreEqual("仓库", repository.Request!.Keyword);
        Assert.AreEqual(1, result.TotalCount);
        Assert.IsNull(session.Transaction);
        Assert.AreEqual(1, ((ConnectionStub)session.Connection).Transaction.Commits);

        repository = new RepositoryStub { Allowed = false };
        session = Session(new ConnectionStub());
        service = new WarehouseCandidateQueryService(
            new ServiceBaseContext(session, NullLoggerFactory.Instance), repository, new TestUser(true, Guid.NewGuid()));
        await Assert.ThrowsExactlyAsync<UnauthorizedAccessException>(() => service.SearchAsync(new()));
        Assert.IsNull(repository.Request);
        Assert.IsNull(session.Transaction);
        Assert.AreEqual(0, ((ConnectionStub)session.Connection).Transaction.Commits);
    }

    /// <summary>真实 Refit 客户端使用既有仓库 API 根和 POST 路由，正文只有稳定分页字段。</summary>
    [TestMethod]
    public async Task RefitUsesCompatibleWarehouseApiRoute()
    {
        using var handler = new RecordingHandler();
        using var client = new HttpClient(handler) { BaseAddress = new Uri("http://localhost/api/inventory/warehouse-sites") };
        var result = await RestService.For<IWarehouseSiteApi>(client).SearchCandidatesAsync(
            new() { Keyword = "WH", PageIndex = 2, PageSize = 5 });
        Assert.IsTrue(result.Success);
        Assert.AreEqual(HttpMethod.Post, handler.Method);
        Assert.AreEqual("/api/inventory/warehouse-sites/candidates/search", handler.Path);
        using var json = JsonDocument.Parse(handler.Body!);
        CollectionAssert.AreEquivalent(new[] { "keyword", "pageIndex", "pageSize" },
            json.RootElement.EnumerateObject().Select(x => x.Name).ToArray());
    }

    /// <summary>公开结果只含稳定身份、显示、类型和双方当前资格，不泄漏库存、采购或联系方式。</summary>
    [TestMethod]
    public void ContractHasOnlyApprovedSelectionFields()
    {
        CollectionAssert.AreEquivalent(new[] { "Uid", "Version", "Code", "Name", "SiteType", "IsActive",
                "SupplierId", "SupplierCode", "SupplierName", "SupplierIsActive" },
            typeof(WarehouseCandidateDto).GetProperties().Select(x => x.Name).ToArray());
        Assert.AreEqual(typeof(WarehouseSiteTypeEnum), typeof(WarehouseCandidateDto).GetProperty("SiteType")!.PropertyType);
    }

    /// <summary>只为测试注入内存连接，不读取配置或建立外部连接。</summary>
    private static DbSession Session(IDbConnection connection)
    {
        var session = (DbSession)RuntimeHelpers.GetUninitializedObject(typeof(DbSession));
        typeof(DbSession).GetField("<Connection>k__BackingField", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .SetValue(session, connection);
        return session;
    }

    private sealed class TestUser(bool authenticated, Guid? uid) : ICurrentUser
    {
        public bool IsAuthenticated => authenticated;
        public Guid? UserId => uid;
        public string UserName => "仓库候选测试";
    }

    /// <summary>记录服务传给仓储的规范化请求，返回确定候选。</summary>
    private sealed class RepositoryStub : IWarehouseCandidateRepository
    {
        public bool Allowed { get; set; }
        public PagedRequestBase? Request { get; private set; }
        public Task<bool> HasPermissionAsync(Guid userId, CancellationToken cancellationToken) => Task.FromResult(Allowed);
        public Task<PagedResponseBase<WarehouseCandidateDto>> SearchAsync(PagedRequestBase request, CancellationToken cancellationToken)
        {
            Request = request;
            return Task.FromResult(new PagedResponseBase<WarehouseCandidateDto>
            {
                Items = [new() { Uid = Guid.NewGuid(), SupplierId = Guid.NewGuid(), SiteType = WarehouseSiteTypeEnum.SupplierVirtual }],
                TotalCount = 1, PageIndex = request.PageIndex, PageSize = request.PageSize
            });
        }
    }

    /// <summary>仅记录事务提交；服务释放事务时不访问数据库。</summary>
    private sealed class ConnectionStub : IDbConnection
    {
        public TransactionStub Transaction { get; } = new();
        public IDbTransaction BeginTransaction() => Transaction;
        public IDbTransaction BeginTransaction(IsolationLevel il) { Transaction.Level = il; return Transaction; }
        public void Close() { }
        public void Open() { }
        public void ChangeDatabase(string databaseName) => throw new NotSupportedException();
        public IDbCommand CreateCommand() => throw new NotSupportedException();
        [AllowNull]
        public string ConnectionString { get; set; } = string.Empty;
        public int ConnectionTimeout => 0;
        public string Database => "memory";
        public ConnectionState State => ConnectionState.Open;
        public void Dispose() { }
    }

    private sealed class TransactionStub : IDbTransaction
    {
        public int Commits { get; private set; }
        public IsolationLevel Level { get; set; }
        public IDbConnection? Connection => null;
        public IsolationLevel IsolationLevel => Level;
        public void Commit() => Commits++;
        public void Rollback() { }
        public void Dispose() { }
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public HttpMethod? Method { get; private set; }
        public string? Path { get; private set; }
        public string? Body { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Method = request.Method;
            Path = request.RequestUri!.AbsolutePath;
            Body = await request.Content!.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                RequestMessage = request,
                Content = new StringContent("""{"success":true,"data":{"items":[],"totalCount":0,"pageIndex":2,"pageSize":5},"message":"成功"}""", Encoding.UTF8, "application/json")
            };
        }
    }
}
