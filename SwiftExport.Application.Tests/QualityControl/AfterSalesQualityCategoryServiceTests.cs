using System.Data;
using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Application.Contexts;
using SwiftExport.Application.Interfaces.Auth.Permissions.Evaluation;
using SwiftExport.Application.Services.QualityControl.Categories;
using SwiftExport.Contracts.DTOs.QualityControl.Categories;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.QualityControl;
using SwiftExport.Core.Interfaces.Auth;
using SwiftExport.Core.Interfaces.Data;
using SwiftExport.Core.Interfaces.QualityControl.Categories;
using SwiftExport.Infrastructure.Data;

namespace SwiftExport.Application.Tests.QualityControl;

/// <summary>真实分类服务的权限、层级、版本和回滚测试；内存事务不能替代 PostgreSQL 验收。</summary>
[TestClass]
public sealed class AfterSalesQualityCategoryServiceTests
{
    /// <summary>子先父输入仍按父先子写入，临时身份被替换，停用父不级联停用子。</summary>
    [TestMethod]
    public async Task ParentOrderAndTemporaryIdentity()
    {
        var f=new Fixture(); var parent=Edit("根"); parent.IsEnabled=false; var child=Edit("子",parent.Uid);
        var result=await f.Service.BatchSaveAsync(new(){CreateItems=[Create(child),Create(parent)]});
        Assert.IsTrue(result.Success,result.Message); Assert.AreEqual("根",f.Rows[0].CategoryName);
        Assert.AreNotEqual(parent.Uid,f.Rows[0].Uid); Assert.AreEqual(f.Rows[0].Uid,f.Rows[1].ParentId);
        Assert.IsTrue(f.Rows[1].IsEnabled); Assert.AreEqual(1,f.Commits); Assert.IsNull(f.Session.Transaction);
    }
    /// <summary>缺父、自引用、循环、重复编码、空名称和负排序在写入前拒绝。</summary>
    [TestMethod]
    [DataRow("missing")] [DataRow("self")] [DataRow("cycle")] [DataRow("code")] [DataRow("name")] [DataRow("sort")]
    public async Task InvalidTree(string kind)
    {
        var f=new Fixture(); var a=Edit("甲"); var b=Edit("乙");
        switch(kind){case "missing":a.ParentId=Guid.NewGuid();break;case "self":a.ParentId=a.Uid;break;case "cycle":a.ParentId=b.Uid;b.ParentId=a.Uid;break;case "code":a.CategoryCode="ABC";b.CategoryCode="abc";break;case "name":a.CategoryName=" ";break;case "sort":a.SortNo=-1;break;}
        var result=await f.Service.BatchSaveAsync(new(){CreateItems=[Create(a),Create(b)]});
        Assert.AreEqual("VALIDATION",result.Error.Code); Assert.AreEqual(0,f.Writes); Assert.AreEqual(1,f.Rollbacks);
    }
    /// <summary>旧版本更新和删除返回并发码，引用和保留子节点阻止删除。</summary>
    [TestMethod]
    [DataRow("update")] [DataRow("delete")] [DataRow("reference")] [DataRow("child")]
    public async Task VersionAndDeleteProtection(string kind)
    {
        var f=new Fixture();var row=new AfterSalesQualityCategory{CategoryName="根",Version=2};f.Rows.Add(row);
        var request=new AfterSalesQualityCategoryBatchSaveRequest();
        if(kind=="update")request.UpdateItems.Add(new(){Uid=row.Uid,Version=1,Data=Edit("改名")});
        else request.DeleteItems.Add(new(){Uid=row.Uid,Version=kind=="delete"?1:2});
        if(kind=="reference")f.References.Add(row.Uid);
        if(kind=="child")f.Rows.Add(new(){CategoryName="子",ParentId=row.Uid});
        var result=await f.Service.BatchSaveAsync(request);Assert.IsFalse(result.Success);Assert.AreEqual(0,f.Writes);
        if(kind is "update" or "delete")Assert.AreEqual("CONCURRENCY_CONFLICT",result.Error.Code);
        else StringAssert.Contains(result.Message,kind=="reference"?"仍被售后单引用":"上级分类不存在");
    }
    /// <summary>第二次写入失败撤销第一次新增，不能返回部分成功或遗留事务。</summary>
    [TestMethod]
    public async Task MidBatchFailureRollsBack()
    {
        var f=new Fixture{FailAt=2};var result=await f.Service.BatchSaveAsync(new(){CreateItems=[Create(Edit("甲")),Create(Edit("乙"))]});
        Assert.IsFalse(result.Success);Assert.HasCount(0,f.Rows);Assert.AreEqual(1,f.Rollbacks);Assert.AreEqual(0,f.Commits);Assert.IsNull(f.Session.Transaction);
    }
    /// <summary>无权限及未认证账号拒绝时，仓储和事务均不被访问。</summary>
    [TestMethod] [DataRow(false)] [DataRow(true)]
    public async Task PermissionDenial(bool authenticated)
    {
        var f=new Fixture{Allowed=false,Authenticated=authenticated};
        Assert.AreEqual("FORBIDDEN",(await f.Service.GetAllAsync()).Error.Code);
        Assert.AreEqual("FORBIDDEN",(await f.Service.BatchSaveAsync(new(){CreateItems=[Create(Edit("甲"))]})).Error.Code);
        Assert.AreEqual(0,f.Reads);Assert.AreEqual(0,f.Writes);Assert.AreEqual(0,f.Begins);
        Assert.AreEqual(authenticated?2:0,f.Keys.Count);
        if(authenticated)CollectionAssert.AreEqual(new[]{"QualityControl.AfterSales.View","QualityControl.AfterSales.Maintain"},f.Keys.ToArray());
    }
    /// <summary>读取包含停用节点，外部事务不能被服务接管或回滚。</summary>
    [TestMethod]
    public async Task DisabledReadAndExternalTransaction()
    {
        var f=new Fixture();Assert.HasCount(0,(await f.Service.GetAllAsync()).Data);f.Rows.Add(new(){CategoryName="停用",IsEnabled=false});
        Assert.IsFalse((await f.Service.GetAllAsync()).Data.Single().IsEnabled);
        using var tx=f.Session.Connection.BeginTransaction();f.Session.Transaction=tx;
        Assert.IsFalse((await f.Service.BatchSaveAsync(new(){CreateItems=[Create(Edit("甲"))]})).Success);
        Assert.AreSame(tx,f.Session.Transaction);Assert.AreEqual(0,f.Rollbacks);Assert.AreEqual(0,f.Writes);
    }
    /// <summary>整批删除无引用树时必须子先父。</summary>
    [TestMethod]
    public async Task DeleteChildrenFirst()
    {
        var f=new Fixture();var root=new AfterSalesQualityCategory{CategoryName="根"};var child=new AfterSalesQualityCategory{CategoryName="子",ParentId=root.Uid};f.Rows.AddRange([root,child]);
        var result=await f.Service.BatchSaveAsync(new(){DeleteItems=[new(){Uid=root.Uid,Version=1},new(){Uid=child.Uid,Version=1}]});
        Assert.IsTrue(result.Success,result.Message);CollectionAssert.AreEqual(new[]{child.Uid,root.Uid},f.Deleted.ToArray());
    }
    private static AfterSalesQualityCategoryEditDto Edit(string name,Guid? parent=null)=>new(){Uid=Guid.NewGuid(),CategoryName=name,ParentId=parent};
    private static CreateItemRequest<AfterSalesQualityCategoryEditDto> Create(AfterSalesQualityCategoryEditDto dto)=>new(){Data=dto,ClientKey=dto.Uid.ToString()};
    private static List<AfterSalesQualityCategory> Clone(List<AfterSalesQualityCategory> rows)=>JsonSerializer.Deserialize<List<AfterSalesQualityCategory>>(JsonSerializer.Serialize(rows))!;
    private static T Port<T>(Func<string,object?[],object?> action)where T:class{var p=DispatchProxy.Create<T,TestPort>();((TestPort)(object)p).Action=action;return p;}
    /// <summary>只转发被调用方法，不在替身内复制树规则。</summary>
    public class TestPort:DispatchProxy
    {
        public Func<string,object?[],object?> Action{get;set;}=null!;
        /// <summary>反射调用交给当前夹具。</summary>
        protected override object? Invoke(MethodInfo? targetMethod,object?[]? args)=>Action(targetMethod!.Name,args??[]);
    }
    /// <summary>可观察持久化和事务快照，验证真实服务的原子性及拒绝路径。</summary>
    private sealed class Fixture
    {
        public List<AfterSalesQualityCategory> Rows{get;set;}=[];public HashSet<Guid> References{get;}=[];public List<Guid> Deleted{get;}=[];public List<string> Keys{get;}=[];
        public bool Allowed{get;set;}=true;public bool Authenticated{get;set;}=true;public int FailAt{get;set;}
        public int Writes;public int Reads;public int Begins;public int Commits;public int Rollbacks;
        public Session Session{get;}public AfterSalesQualityCategoryService Service{get;}
        /// <summary>依赖代理只响应本切片真实调用，意外端口使用立即失败。</summary>
        public Fixture()
        {
            Session=new(new Connection(this));
            var user=Port<ICurrentUser>((n,_)=>n switch{"get_IsAuthenticated"=>Authenticated,"get_UserId"=>(Guid?)Guid.Parse("00000000-0000-0000-0000-000000000001"),"get_UserName"=>"分类测试",_=>throw new NotSupportedException(n)});
            var permissions=Port<IPermissionEvaluationService>((n,a)=>{Keys.Add((string)a[1]!);return Task.FromResult(Allowed);});
            var repo=Port<IAfterSalesQualityCategoryRepository>((n,a)=>n switch{
                "ReadAllAsync"=>Read(),"GetReferencedCategoryIdsAsync"=>Task.FromResult<IReadOnlyList<Guid>>(((IEnumerable<Guid>)a[0]!).Where(References.Contains).ToArray()),
                "AddAsync"=>Write((AfterSalesQualityCategory)a[0]!,true),"UpdateAsync"=>Write((AfterSalesQualityCategory)a[0]!,false),
                "DeleteAsync"=>Delete((Guid)a[0]!, (int)a[1]!),_=>throw new NotSupportedException(n)});
            Service=new(repo,new ServiceBaseContext(Session,NullLoggerFactory.Instance),permissions,user);
        }
        private Task<IReadOnlyList<AfterSalesQualityCategory>> Read(){Reads++;return Task.FromResult<IReadOnlyList<AfterSalesQualityCategory>>(Clone(Rows));}
        /// <summary>新增强制新身份，使临时父标识未转换的错误可观测。</summary>
        private Task<RepositoryWriteResult> Write(AfterSalesQualityCategory row,bool create)
        {
            Writes++;if(Writes==FailAt)return Task.FromResult(RepositoryWriteResult.Fail(row.Uid,row.Version,"CONCURRENCY_CONFLICT","模拟第二项失败"));
            if(create)row.Uid=Guid.NewGuid();else{Rows.RemoveAll(x=>x.Uid==row.Uid);row.Version++;}Rows.Add(Clone([row]).Single());
            return Task.FromResult(RepositoryWriteResult.Ok(row.Uid,create?null:row.Version-1,row.Version));
        }
        private Task<RepositoryWriteResult> Delete(Guid uid,int version){Writes++;Deleted.Add(uid);Rows.RemoveAll(x=>x.Uid==uid);return Task.FromResult(RepositoryWriteResult.Ok(uid,version,version));}
    }
    /// <summary>最小会话保留事务及公共异常分类，不访问数据库。</summary>
    private sealed class Session(IDbConnection connection):IDbSession{public IDbConnection Connection=>connection;public IDbTransaction? Transaction{get;set;}public IDatabaseExceptionClassifier Errors=>PostgreSqlExceptionClassifier.Instance;}
    /// <summary>开启事务保存真实替身数据快照，记录隔离级别。</summary>
    private sealed class Connection(Fixture f):IDbConnection
    {
        [System.Diagnostics.CodeAnalysis.AllowNull]public string ConnectionString{get;set;}="";public int ConnectionTimeout=>0;public string Database=>"memory";public ConnectionState State=>ConnectionState.Open;
        public IDbTransaction BeginTransaction()=>BeginTransaction(IsolationLevel.Serializable);
        /// <summary>服务必须使用 Serializable；回滚由快照恢复持久化集合。</summary>
        public IDbTransaction BeginTransaction(IsolationLevel level){Assert.AreEqual(IsolationLevel.Serializable,level);f.Begins++;return new Transaction(this,f,Clone(f.Rows));}
        public void ChangeDatabase(string databaseName)=>throw new NotSupportedException();public void Close(){}public void Open(){}public void Dispose(){}public IDbCommand CreateCommand()=>throw new NotSupportedException();
    }
    /// <summary>事务仅能终结一次，未提交释放恢复原始快照。</summary>
    private sealed class Transaction(IDbConnection connection,Fixture f,List<AfterSalesQualityCategory> snapshot):IDbTransaction
    {
        private bool ended;public IDbConnection Connection=>connection;public IsolationLevel IsolationLevel=>IsolationLevel.Serializable;
        public void Commit(){f.Commits++;ended=true;}public void Rollback(){if(ended)return;f.Rows=snapshot;f.Rollbacks++;ended=true;}public void Dispose(){if(!ended)Rollback();}
    }
}
