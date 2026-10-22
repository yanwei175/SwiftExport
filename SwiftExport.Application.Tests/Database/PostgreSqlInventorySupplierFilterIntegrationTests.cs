using Dapper;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.MasterData;
using SwiftExport.Core.Interfaces.Auth;
using SwiftExport.Infrastructure.Contexts;
using SwiftExport.Infrastructure.Data;
using SwiftExport.Infrastructure.DatabaseSchema;
using SwiftExport.Infrastructure.Repositories.Base;
using SwiftExport.Infrastructure.Repositories.Inventory.Warehouses;
using SwiftExport.Infrastructure.Services.Auth;

namespace SwiftExport.Application.Tests.Database;

/// <summary>在明确白名单隔离库验证供应商筛选；仅插随机供应商，单事务 finally 回滚，不初始化或清库。</summary>
[TestClass,DoNotParallelize,TestCategory("PostgreSqlInventorySupplierFilterIntegration")]
public sealed class PostgreSqlInventorySupplierFilterIntegrationTests
{
    /// <summary>没有采购或库存记录的活动/停用供应商均可筛选，软删排除；完整分页、字面搜索及 bigint 偏移有效。</summary>
    [TestMethod]
    public async Task IncludeInactiveWithoutBusinessRecordsAndPageSafely()
    {
        using var session=new DbSession(new PostgreSqlConnectionFactory(PostgreSqlIntegrationEnvironment.GetFailureConnectionString()));
        using var transaction=session.Connection.BeginTransaction();session.Transaction=transaction;
        try
        {
            var user=new Identity();var context=new RepositoryBaseContext(session,NullLoggerFactory.Instance,new AuditStampService(user),user);
            var repository=new PostgreSqlWarehouseQueryRepository(context);var prefix="SFLT"+Guid.NewGuid().ToString("N")+"~%_[";
            var active=new Supplier{SupplierCode=prefix+"A",SupplierName="活动供应商",DocRelateNo=prefix,IsActive=true};
            var inactive=new Supplier{SupplierCode=prefix+"B",SupplierName="停用供应商",DocRelateNo=prefix,IsActive=false};
            var deleted=new Supplier{SupplierCode=prefix+"C",SupplierName="已删除供应商",DocRelateNo=prefix,IsActive=true};
            var map=EntityMetadataCache.GetMetadata(typeof(Supplier));
            var columns=string.Join(',',map.Columns.Select(x=>PostgreSqlNames.Quote(x.ColumnName)));
            var values=string.Join(',',map.Columns.Select(x=>"@"+x.ParameterName));
            foreach(var supplier in new[]{active,inactive,deleted})
            {
                context.AuditStamp.StampForInsert(supplier);supplier.IsDeleted=supplier==deleted;
                Assert.AreEqual(1,await session.Connection.ExecuteAsync(new CommandDefinition(
                    $"INSERT INTO public.{PostgreSqlNames.Quote(map.TableName)} ({columns}) VALUES ({values})",
                    PostgreSqlParameters.Entity(supplier,map),transaction)));
            }
            var request=new PagedRequestBase{Keyword="  "+prefix+"  ",PageSize=1};var ids=new List<Guid>();
            for(var page=1;page<=3;page++)
            {
                request.PageIndex=page;var result=await repository.SearchSupplierFilterOptionsAsync(request);
                Assert.AreEqual(2,result.TotalCount);Assert.HasCount(page<=2?1:0,result.Items);ids.AddRange(result.Items.Select(x=>x.Uid));
            }
            CollectionAssert.AreEquivalent(new[]{active.Uid,inactive.Uid},ids);
            request.PageIndex=1;request.Keyword=inactive.SupplierCode;
            Assert.AreEqual(inactive.Uid,(await repository.SearchSupplierFilterOptionsAsync(request)).Items.Single().Uid);
            request.Keyword=prefix;request.PageSize=200;
            Assert.AreEqual(1,(await repository.SearchSupplierLookupAsync(request)).TotalCount,"既有建仓供应商候选仍只允许活动供应商。");
            request.PageIndex=int.MaxValue;
            var beyond=await repository.SearchSupplierFilterOptionsAsync(request);Assert.AreEqual(2,beyond.TotalCount);Assert.IsEmpty(beyond.Items);
            Assert.AreSame(transaction,session.Transaction);
            Assert.AreEqual(1,await session.Connection.ExecuteScalarAsync<int>("SELECT 1",transaction:transaction));
        }
        finally{try{transaction.Rollback();}finally{session.Transaction=null;}}
    }

    /// <summary>仅为测试插入打戳，不表示真实登录或额外权限。</summary>
    private sealed class Identity:ICurrentUser
    {
        public Guid? UserId=>null;
        public string UserName=>"供应商筛选隔离测试";
        public bool IsAuthenticated=>false;
    }
}
