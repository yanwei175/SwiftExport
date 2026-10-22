using Dapper;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Contracts.DTOs.Shipping.PackingLists;
using SwiftExport.Contracts.SysEnums.Shipping;
using SwiftExport.Core.Entities.Shipping;
using SwiftExport.Core.Interfaces.Auth;
using SwiftExport.Infrastructure.Contexts;
using SwiftExport.Infrastructure.Data;
using SwiftExport.Infrastructure.DatabaseSchema;
using SwiftExport.Infrastructure.Repositories.Base;
using SwiftExport.Infrastructure.Repositories.Shipping;
using SwiftExport.Infrastructure.Services.Auth;

namespace SwiftExport.Application.Tests.Database;

/// <summary>仅在明确隔离库中验证 Manager 集合筛选，插入随机头后单事务回滚，不执行箱单业务命令。</summary>
[TestClass,DoNotParallelize,TestCategory("PostgreSqlPackingListManagerFilterIntegration")]
public sealed class PostgreSqlPackingListManagerFilterIntegrationTests
{
    /// <summary>客户和状态分别取并集、跨维取交集，计数分页一致，空集合/旧单值及混用保持兼容。</summary>
    [TestMethod]
    public async Task ManagerCollectionsPreserveUnionFallbackAndPaging()
    {
        using var session=new DbSession(new PostgreSqlConnectionFactory(PostgreSqlIntegrationEnvironment.GetFailureConnectionString()));
        using var transaction=session.Connection.BeginTransaction();session.Transaction=transaction;
        try
        {
            var user=new Identity();var context=new RepositoryBaseContext(session,NullLoggerFactory.Instance,new AuditStampService(user),user);
            var repository=new PostgreSqlPackingListRepository(context);var prefix="PLMF"+Guid.NewGuid().ToString("N");
            var a=Guid.NewGuid();var b=Guid.NewGuid();var c=Guid.NewGuid();
            var states=new[]{(a,PackingListPostingStatusEnum.Unposted),(a,PackingListPostingStatusEnum.Posted),(a,PackingListPostingStatusEnum.Reversed),
                (b,PackingListPostingStatusEnum.Unposted),(b,PackingListPostingStatusEnum.Reversed),(c,PackingListPostingStatusEnum.Posted)};
            var expected=new List<Guid>();var index=0;var map=EntityMetadataCache.GetMetadata(typeof(PackingListHead));
            var columns=string.Join(',',map.Columns.Select(x=>PostgreSqlNames.Quote(x.ColumnName)));var values=string.Join(',',map.Columns.Select(x=>"@"+x.ParameterName));
            foreach(var (customer,status) in states)
            {
                var head=new PackingListHead{PackingListNo=prefix+index++,CustomerId=customer,CustomerNameSnapshot="筛选客户",
                    InvoiceDate=new(2026,10,21),PostingStatus=status,CreateCommandId=Guid.NewGuid()};
                context.AuditStamp.StampForInsert(head);
                Assert.AreEqual(1,await session.Connection.ExecuteAsync(new CommandDefinition(
                    $"INSERT INTO public.{PostgreSqlNames.Quote(map.TableName)} ({columns}) VALUES ({values})",PostgreSqlParameters.Entity(head,map),transaction)));
                if((customer==a||customer==b)&&status is PackingListPostingStatusEnum.Unposted or PackingListPostingStatusEnum.Reversed)expected.Add(head.Uid);
            }
            var request=new PackingListManagerSearchRequest{Keyword=prefix,PageSize=2,CustomerId=c,PostingStatus=PackingListPostingStatusEnum.Posted,
                CustomerIds=[a,b],PostingStatuses=[PackingListPostingStatusEnum.Unposted,PackingListPostingStatusEnum.Reversed]};
            var ids=new List<Guid>();
            for(var page=1;page<=3;page++)
            {
                request.PageIndex=page;var result=await repository.SearchAsync(request);
                Assert.AreEqual(4,result.TotalCount);Assert.HasCount(page<=2?2:0,result.Items);ids.AddRange(result.Items.Select(x=>x.Uid));
            }
            CollectionAssert.AreEquivalent(expected,ids);
            request.PageIndex=1;request.PageSize=200;request.CustomerIds=[];request.PostingStatuses=[];
            Assert.AreEqual(6,(await repository.SearchAsync(request)).TotalCount);
            request.CustomerIds=null!;request.PostingStatuses=null!;
            var legacy=await repository.SearchAsync(request);Assert.AreEqual(1,legacy.TotalCount);Assert.AreEqual(c,legacy.Items.Single().CustomerId);
            request.CustomerIds=[b];request.PostingStatus=PackingListPostingStatusEnum.Unposted;Assert.AreEqual(1,(await repository.SearchAsync(request)).TotalCount);
            request.CustomerIds=null!;request.CustomerId=a;request.PostingStatuses=[PackingListPostingStatusEnum.Reversed];Assert.AreEqual(1,(await repository.SearchAsync(request)).TotalCount);
            request.CustomerIds=[a,a];request.PostingStatuses=[PackingListPostingStatusEnum.Unposted,PackingListPostingStatusEnum.Unposted];Assert.AreEqual(1,(await repository.SearchAsync(request)).TotalCount);
            Assert.AreSame(transaction,session.Transaction);Assert.AreEqual(1,await session.Connection.ExecuteScalarAsync<int>("SELECT 1",transaction:transaction));
        }
        finally{try{transaction.Rollback();}finally{session.Transaction=null;}}
    }

    /// <summary>仅用于新测试头审计打戳，不表示实际登录或业务授权。</summary>
    private sealed class Identity:ICurrentUser
    {public Guid? UserId=>null;public string UserName=>"Manager筛选隔离测试";public bool IsAuthenticated=>false;}
}
