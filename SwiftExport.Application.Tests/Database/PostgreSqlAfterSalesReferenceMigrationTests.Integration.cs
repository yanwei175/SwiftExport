using System.Data;
using System.Data.SqlTypes;
using Dapper;
using Npgsql;
using NpgsqlTypes;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.Core.Common;
using SwiftExport.Core.Entities.Auth;
using SwiftExport.Core.Entities.MasterData;
using SwiftExport.Core.Entities.Relations;
using SwiftExport.Core.Entities.QualityControl;
using SwiftExport.Core.Entities.SalesOrders;
using SwiftExport.Core.Entities.Systems.Dictionaries.Data;
using SwiftExport.Core.Interfaces.Auth;
using SwiftExport.Infrastructure.Contexts;
using SwiftExport.Infrastructure.Data;
using SwiftExport.Infrastructure.DatabaseSchema;
using SwiftExport.Infrastructure.Repositories.Base;
using SwiftExport.Infrastructure.Repositories.QualityControl.AfterSales.Lookup;
using SwiftExport.Infrastructure.Services.Auth;

namespace SwiftExport.Application.Tests.Database;

/// <summary>专用 failure 库的完整引用七分支矩阵；只种本人随机合法实体，父 RC 全回滚，不部署、提交或删旧数据。</summary>
[TestClass]
public sealed class PostgreSqlAfterSalesReferenceMigrationIntegrationTests
{
    /// <summary>停用对象仅新引用排除；颜色和已关闭销售头例外，三列 DTO 其余字段仍默认。</summary>
    [TestMethod] [TestCategory("PostgreSqlAfterSalesReferenceIntegration")]
    [DataRow("customers",false)] [DataRow("customers",true)]
    [DataRow("suppliers",false)] [DataRow("suppliers",true)]
    [DataRow("owners",false)] [DataRow("owners",true)]
    [DataRow("colors",false)] [DataRow("colors",true)]
    [DataRow("quality-categories",false)] [DataRow("quality-categories",true)]
    [DataRow("sales-order-lines",false)] [DataRow("sales-order-lines",true)]
    [DataRow("products",false)] [DataRow("products",true)]
    public Task SevenKindsHistoricalQualification(string kind,bool active)=>InRollback(async f=>
    {
        var item=await SeedCandidate(f,kind,false,false);
        var d=await f.Repository.ReferenceAsync(kind,item.Uid,item.CustomerId,active);
        if(active && kind is not "colors" and not "sales-order-lines") {Assert.IsNull(d);return;}
        Assert.IsNotNull(d);Assert.AreEqual(item.Uid,d.Uid);Assert.AreEqual(item.Code,d.Code);Assert.AreEqual(item.Name,d.Name);
        if(kind!="products")
        {Assert.AreEqual("",d.CustomerSku);Assert.AreEqual("",d.LinkSku);Assert.AreEqual("",d.Unit);Assert.IsNull(d.ColorId);Assert.AreEqual("",d.ColorName);Assert.AreEqual("",d.Specification);}
        Assert.AreSame(f.Transaction,f.Session.Transaction);
    });

    /// <summary>全部七类软删排除，即使历史引用放宽活动过滤。</summary>
    [TestMethod] [TestCategory("PostgreSqlAfterSalesReferenceIntegration")]
    [DataRow("customers")] [DataRow("suppliers")] [DataRow("owners")] [DataRow("colors")]
    [DataRow("quality-categories")] [DataRow("sales-order-lines")] [DataRow("products")]
    public Task DeletedCandidatesExcluded(string kind)=>InRollback(async f=>
    {
        var item=await SeedCandidate(f,kind,true,true);
        Assert.IsNull(await f.Repository.ReferenceAsync(kind,item.Uid,item.CustomerId,false));
        Assert.IsNull(await f.Repository.ReferenceAsync(kind,item.Uid,item.CustomerId,true));
    });

    /// <summary>缺失和 Empty 身份及 nullable 客户由真实 SQL 返回 null，没有提前业务短路。</summary>
    [TestMethod] [TestCategory("PostgreSqlAfterSalesReferenceIntegration")]
    [DataRow("customers")] [DataRow("suppliers")] [DataRow("owners")] [DataRow("colors")]
    [DataRow("quality-categories")] [DataRow("sales-order-lines")] [DataRow("products")]
    public Task MissingAndEmptyIdentity(string kind)=>InRollback(async f=>
    {
        Assert.IsNull(await f.Repository.ReferenceAsync(kind,Guid.NewGuid(),null,true));
        // 不依赖公共库绝无 Empty 行：先按实体身份确认缺失，否则未验证，不能删除已有行造前提。
        var type=kind switch{"customers"=>typeof(Customer),"suppliers"=>typeof(Supplier),"owners"=>typeof(SysUser),"colors"=>typeof(ProductColor),"quality-categories"=>typeof(AfterSalesQualityCategory),"sales-order-lines"=>typeof(SalesOrderLine),_=>typeof(Product)};
        var map=EntityMetadataCache.GetMetadata(type);
        var count=await f.Session.Connection.ExecuteScalarAsync<int>($"SELECT count(*) FROM {Table(map)} WHERE {PostgreSqlNames.Quote(map.KeyName)}=@Uid",new{Uid=Guid.Empty},f.Transaction);
        if(count!=0)Assert.Inconclusive("专用库已有 Empty 身份，不能改旧数据造缺失样本。");
        Assert.IsNull(await f.Repository.ReferenceAsync(kind,Guid.Empty,null,false));
    });

    /// <summary>用户空昵称优先，NULL 昵称才回退用户名；颜色英文空串优先，NULL 才中文。</summary>
    [TestMethod] [TestCategory("PostgreSqlAfterSalesReferenceIntegration")]
    [DataRow(false)] [DataRow(true)]
    public Task NameCoalesce(bool empty)=>InRollback(async f=>
    {
        var user=new SysUser{UserName=f.Prefix+"U",PasswordHash="__pgtest_unused_hash__",NickName=empty?"":null};await f.Seed(user);
        var color=new ProductColor{ColorCode=f.Prefix+"C",ColorCodeUnique=f.Prefix+"CU",ColorNameEn=empty?"":null,ColorNameZh="中文"};await f.Seed(color);
        Assert.AreEqual(empty?"":user.UserName,(await f.Repository.ReferenceAsync("owners",user.Uid,null,true))!.Name);
        Assert.AreEqual(empty?"":"中文",(await f.Repository.ReferenceAsync("colors",color.Uid,null,true))!.Name);
    });

    /// <summary>旧字典保留但不再提供候选；新分类编码、别名、名称依次兜底。</summary>
    [TestMethod] [TestCategory("PostgreSqlAfterSalesReferenceIntegration")]
    [DataRow("CODE", "Alias", "CODE")] [DataRow(null, "Alias", "Alias")] [DataRow(" ", "", "品质")]
    public Task CategoryFallbackAndDictionaryIsolation(string? code, string? alias, string expected) => InRollback(async f =>
    {
        var legacy = new SysDictData { DictCode="AfterSalesQualityCategory", DataNameEn=f.Prefix, IsEnabled=true };
        await f.Seed(legacy);
        Assert.IsNull(await f.Repository.ReferenceAsync("quality-categories", legacy.Uid, null, true));
        var category = new AfterSalesQualityCategory { CategoryName="品质", CategoryCode=code is null?null:f.Prefix+code, FakeName=alias };
        if(code==" ") category.CategoryCode=" ";
        await f.Seed(category);
        var result = await f.Repository.ReferenceAsync("quality-categories", category.Uid, null, true);
        Assert.IsNotNull(result); Assert.AreEqual(code=="CODE"?f.Prefix+expected:expected, result.Code); Assert.AreEqual("品质", result.Name);
    });
    /// <summary>销售行只裁决头行删除及客户身份，已关闭/已确认头仍可引用；NULL/错误客户及删头不能返回。</summary>
    [TestMethod] [TestCategory("PostgreSqlAfterSalesReferenceIntegration")]
    public Task SalesHeaderAndCustomerBoundaries()=>InRollback(async f=>
    {
        var c=NewCustomer(f);await f.Seed(c);
        var h=new SalesOrderHeader{OrderNo=f.Prefix+"SO",CustomerId=c.Uid,DocumentStatus=(SalesOrderDocumentStatusEnum)8,ConfirmationStatus=ConfirmationStatusEnum.已确认};await f.Seed(h);
        var l=new SalesOrderLine{SalesOrderId=h.Uid,LineNumber=1,Sku=f.Prefix+"NS",IsNonStandard=true,Qty=2,UnitSnapshot="SET"};await f.Seed(l);
        Assert.AreEqual(h.OrderNo+"/1",(await f.Repository.ReferenceAsync("sales-order-lines",l.Uid,c.Uid,true))!.Code);
        Assert.IsNull(await f.Repository.ReferenceAsync("sales-order-lines",l.Uid,null,false));
        Assert.IsNull(await f.Repository.ReferenceAsync("sales-order-lines",l.Uid,Guid.Empty,false));
        Assert.IsNull(await f.Repository.ReferenceAsync("sales-order-lines",l.Uid,Guid.NewGuid(),false));
        await f.Session.Connection.ExecuteAsync("UPDATE public.sales_order_header SET is_deleted=TRUE WHERE uid=@Uid",new{Uid=h.Uid},f.Transaction);
        Assert.IsNull(await f.Repository.ReferenceAsync("sales-order-lines",l.Uid,c.Uid,false));
    });

    /// <summary>产品九字段保留空串和历史颜色，活动/删除关系始终排除；两合法关系按 SqlGuid 首条而非原生 uuid。</summary>
    [TestMethod] [TestCategory("PostgreSqlAfterSalesReferenceIntegration")]
    [DataRow(false)] [DataRow(true)]
    public Task ProductRelationSqlGuidAndNineFields(bool active)=>InRollback(async f=>
    {
        var customer=NewCustomer(f);customer.IsActive=false;await f.Seed(customer);
        var color=new ProductColor{ColorCode=f.Prefix+"C",ColorCodeUnique=f.Prefix+"CU",ColorNameEn="历史颜色",ColorNameZh="中文",IsDeleted=true};await f.Seed(color);
        var p=new Product{Sku=f.Prefix+"P",SupplyType="采购",Unit="SET",ColorId=color.Uid,DescriptionZh="",DescriptionEn="英文",FeaturesZh=null,FeaturesEn="规格",LinkSku=" AQ "};await f.Seed(p);
        var tail=Guid.NewGuid().ToString("N")[..10];
        var preferred=Guid.Parse("ffffffff-ffff-ffff-ffff-"+tail+"02");var other=Guid.Parse("00000000-0000-0000-0000-"+tail+"ff");
        Assert.IsTrue(new SqlGuid(preferred).CompareTo(new SqlGuid(other))<0);Assert.IsTrue(preferred.CompareTo(other)>0);
        foreach(var row in new[]{
            Relation(customer,p,f.Prefix+"Stop",Guid.Parse("ffffffff-ffff-ffff-ffff-"+tail+"00"),false,false),
            Relation(customer,p,f.Prefix+"Deleted",Guid.Parse("ffffffff-ffff-ffff-ffff-"+tail+"01"),true,true),
            Relation(customer,p,f.Prefix+"First",preferred,true,false),Relation(customer,p,f.Prefix+"Second",other,true,false)}) await f.Seed(row);
        var d=await f.Repository.ReferenceAsync("products",p.Uid,customer.Uid,active);
        Assert.IsNotNull(d);Assert.AreEqual(p.Uid,d.Uid);Assert.AreEqual(p.Sku,d.Code);Assert.AreEqual("",d.Name);
        Assert.AreEqual(f.Prefix+"First",d.CustomerSku);Assert.AreEqual(" AQ ",d.LinkSku);Assert.AreEqual("SET",d.Unit);
        Assert.AreEqual(color.Uid,d.ColorId);Assert.AreEqual("历史颜色",d.ColorName);Assert.AreEqual("规格",d.Specification);
        Assert.AreEqual(NullText(),(await f.Repository.ReferenceAsync("products",p.Uid,null,active))!.CustomerSku);
        Assert.AreEqual(NullText(),(await f.Repository.ReferenceAsync("products",p.Uid,Guid.Empty,active))!.CustomerSku);
        Assert.AreEqual(NullText(),(await f.Repository.ReferenceAsync("products",p.Uid,Guid.NewGuid(),active))!.CustomerSku);
    });

    /// <summary>产品没有关系且名称/特征/颜色 NULL 时，SQL COALESCE 与原 Dapper 默认处理各自保留。</summary>
    [TestMethod] [TestCategory("PostgreSqlAfterSalesReferenceIntegration")]
    public Task ProductNullableSnapshots()=>InRollback(async f=>
    {
        var color=new ProductColor{ColorCode=f.Prefix+"C",ColorCodeUnique=f.Prefix+"CU"};await f.Seed(color);
        var p=new Product{Sku=f.Prefix+"P",SupplyType="采购",ColorId=color.Uid,Unit="SET"};await f.Seed(p);
        var d=await f.Repository.ReferenceAsync("products",p.Uid,null,true);
        Assert.IsNotNull(d);Assert.AreEqual("",d.Name);Assert.AreEqual(NullText(),d.CustomerSku);Assert.AreEqual(NullText(),d.LinkSku);
        Assert.AreEqual(NullText(),d.ColorName);Assert.AreEqual(NullText(),d.Specification);Assert.AreEqual(color.Uid,d.ColorId);
    });

    /// <summary>非法 kind 沿原 ArgumentException，真实会话不改变父事务或发业务 SQL。</summary>
    [TestMethod] [TestCategory("PostgreSqlAfterSalesReferenceIntegration")]
    public Task ExactKindRejected()=>InRollback(async f=>
    {
        foreach(var kind in new string?[]{null,"","Customers"," customers","customers ","unknown"})
        {var ex=await Assert.ThrowsExactlyAsync<ArgumentException>(()=>f.Repository.ReferenceAsync(kind!,Guid.Empty,null,true));Assert.AreEqual("kind",ex.ParamName);}
        Assert.AreSame(f.Transaction,f.Session.Transaction);
    });

    /// <summary>根据实体种合法历史候选，既有引用资格不附加客户、产品或单据状态。</summary>
    private static async Task<(Guid Uid,Guid? CustomerId,string Code,string? Name)> SeedCandidate(Fixture f,string kind,bool active,bool deleted)
    {
        switch(kind)
        {
            case "customers":var c=NewCustomer(f);c.IsActive=active;c.IsDeleted=deleted;await f.Seed(c);return(c.Uid,null,c.CustomerCode,c.CustomerName);
            case "suppliers":var s=new Supplier{SupplierCode=f.Prefix+"S",SupplierName="供应商",DocRelateNo=f.Prefix,IsActive=active,IsDeleted=deleted};await f.Seed(s);return(s.Uid,null,s.SupplierCode,s.SupplierName);
            case "owners":var u=new SysUser{UserName=f.Prefix+"U",NickName="",PasswordHash="__pgtest_unused_hash__",IsActive=active,IsDeleted=deleted};await f.Seed(u);return(u.Uid,null,u.UserName,"");
            case "colors":var color=new ProductColor{ColorCode=f.Prefix+"C",ColorCodeUnique=f.Prefix+"CU",ColorNameEn="",ColorNameZh="中文",IsDeleted=deleted};await f.Seed(color);return(color.Uid,null,color.ColorCode,"");
            case "quality-categories":var d=new AfterSalesQualityCategory{FakeName=f.Prefix+"D",CategoryName="品质",IsEnabled=active,IsDeleted=deleted};await f.Seed(d);return(d.Uid,null,d.FakeName,d.CategoryName);
            case "sales-order-lines":var customer=NewCustomer(f);await f.Seed(customer);var h=new SalesOrderHeader{OrderNo=f.Prefix+"SO",CustomerId=customer.Uid,DocumentStatus=(SalesOrderDocumentStatusEnum)8,ConfirmationStatus=ConfirmationStatusEnum.已确认};await f.Seed(h);var l=new SalesOrderLine{SalesOrderId=h.Uid,LineNumber=1,Sku=f.Prefix+"NS",IsNonStandard=true,Qty=1,UnitSnapshot="SET",IsDeleted=deleted};await f.Seed(l);return(l.Uid,customer.Uid,h.OrderNo+"/1",l.Sku);
            case "products":var pc=new ProductColor{ColorCode=f.Prefix+"C",ColorCodeUnique=f.Prefix+"CU",ColorNameEn="颜色"};await f.Seed(pc);var p=new Product{Sku=f.Prefix+"P",SupplyType="采购",Unit="SET",ColorId=pc.Uid,DescriptionZh="产品",IsActive=active,IsDeleted=deleted};await f.Seed(p);return(p.Uid,null,p.Sku,p.DescriptionZh);
            default:throw new ArgumentException("夹具只支持已冻结白名单。",nameof(kind));
        }
    }
    /// <summary>每次创建不同编码的合法客户，不使用系统既有客户。</summary>
    private static Customer NewCustomer(Fixture f)=>new(){CustomerCode=f.Prefix+"C",CustomerName="客户",DocRelateNo=f.Prefix};
    /// <summary>同产品关系使用不同客户料号满足唯一约束，身份指定用于首条排序对照。</summary>
    private static CustomerProduct Relation(Customer c,Product p,string sku,Guid uid,bool active,bool deleted)=>new(){Uid=uid,CustomerId=c.Uid,CustomerCode=c.CustomerCode,SkuId=p.Uid,Sku=p.Sku,CustomerSku=sku,IsActive=active,IsDeleted=deleted};
    /// <summary>沿当前全局 Dapper 设置解释数据库 NULL，不修改该设置或 DTO 默认。</summary>
    private static string? NullText()=>SqlMapper.Settings.ApplyNullValues?null:"";
    /// <summary>固定实体元数据生成测试表标识。</summary>
    private static string Table(EntityMetadata map)=>PostgreSqlNames.Quote(map.SchemaName)+"."+PostgreSqlNames.Quote(map.TableName);

    /// <summary>强 failure 门禁，父 RC 全回滚并按准确种子身份复核无残留，不含 Receipt/编号/业务源。</summary>
    private static async Task InRollback(Func<Fixture,Task> scenario)
    {
        using var f=new Fixture(PostgreSqlIntegrationEnvironment.GetFailureConnectionString());
        try{await scenario(f);Assert.AreSame(f.Transaction,f.Session.Transaction);}
        finally{try{f.Transaction.Rollback();}finally{f.Session.Transaction=null;}}
        foreach(var seed in f.Seeds)
            Assert.AreEqual(0,await f.Session.Connection.ExecuteScalarAsync<int>($"SELECT count(*) FROM {Table(seed.Map)} WHERE {PostgreSqlNames.Quote(seed.Map.KeyName)}=@Uid",new{Uid=seed.Uid}),"父回滚后本人随机种子必须消失。");
    }
    /// <summary>单个真实会话及父事务；记录全部本人插入身份，只回滚，不清表或改约束。</summary>
    private sealed class Fixture:IDisposable
    {
        public readonly DbSession Session;public readonly IDbTransaction Transaction;public readonly PostgreSqlAfterSalesLookupRepository Repository;
        public readonly string Prefix="M25R"+Guid.NewGuid().ToString("N")[..16];
        public readonly List<(EntityMetadata Map,Guid Uid)> Seeds=[];
        /// <summary>连接只能由专用库强门禁提供，立即开启父 RC 事务。</summary>
        public Fixture(string connection){Session=new(new PostgreSqlConnectionFactory(connection));Transaction=Session.Connection.BeginTransaction(IsolationLevel.ReadCommitted);Session.Transaction=Transaction;var u=new Identity();Repository=new(new(Session,NullLoggerFactory.Instance,new AuditStampService(u),u));}
        /// <summary>只插入本人随机实体完整映射，保留指定软删/排序身份；各可空列明确类型。</summary>
        public async Task Seed<T>(T entity) where T:BaseEntity
        {
            entity.CreatedDate=entity.UpdatedDate=new DateTime(2026,10,5,0,0,0,DateTimeKind.Unspecified);entity.CreatedBy=entity.UpdatedBy="__m25_reference__";
            var map=EntityMetadataCache.GetMetadata(typeof(T));
            using var cmd=new NpgsqlCommand($"INSERT INTO {Table(map)} ({string.Join(',',map.Columns.Select(c=>PostgreSqlNames.Quote(c.ColumnName)))}) VALUES ({string.Join(',',map.Columns.Select(c=>"@"+c.PropertyName))})",(NpgsqlConnection)Session.Connection,(NpgsqlTransaction)Transaction);
            foreach(var column in map.Columns)
            {
                var type=Nullable.GetUnderlyingType(column.Property.PropertyType)??column.Property.PropertyType;var value=column.Property.GetValue(entity);
                if(type.IsEnum){value=value is null?null:Convert.ToInt32(value);type=typeof(int);}
                var pg=type==typeof(Guid)?NpgsqlDbType.Uuid:type==typeof(string)?NpgsqlDbType.Text:type==typeof(bool)?NpgsqlDbType.Boolean:type==typeof(DateTime)?NpgsqlDbType.Timestamp:type==typeof(decimal)?NpgsqlDbType.Numeric:type==typeof(int)?NpgsqlDbType.Integer:throw new InvalidOperationException("本夹具未声明该实体字段类型。");
                cmd.Parameters.Add(new NpgsqlParameter(column.PropertyName,pg){Value=value??DBNull.Value});
            }
            Assert.AreEqual(1,await cmd.ExecuteNonQueryAsync());Seeds.Add((map,entity.Uid));
        }
        /// <summary>仅释放本人父事务和连接，失败已由 finally 回滚。</summary>
        public void Dispose(){Transaction.Dispose();Session.Dispose();}
    }
    /// <summary>测试身份没有真实用户授权，仅用于本地上下文依赖。</summary>
    private sealed class Identity:ICurrentUser{public Guid? UserId=>null;public string UserName=>"售后引用实库夹具";public bool IsAuthenticated=>false;}
}
