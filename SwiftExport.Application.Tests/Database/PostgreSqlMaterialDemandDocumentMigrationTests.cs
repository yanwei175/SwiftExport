using System.Collections;
using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Dapper;
using Npgsql;
using NpgsqlTypes;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Core.Entities.DemandPlanning;
using SwiftExport.Core.Interfaces.Auth;
using SwiftExport.Core.Interfaces.DemandPlanning.Materials.Document;
using SwiftExport.Infrastructure.Contexts;
using SwiftExport.Infrastructure.Data;
using SwiftExport.Infrastructure.Repositories.DemandPlanning.Materials.Document;
using SwiftExport.Infrastructure.Services.Auth;

namespace SwiftExport.Application.Tests.Database;

/// <summary>仅验证离线可证的接口、空输入、异常和无覆盖边界；不伪造真实租约或宣称物理并发通过。</summary>
[TestClass]
public sealed class PostgreSqlMaterialDemandDocumentMigrationTests
{
    /// <summary>完整七方法与共同准备能力均可赋值，无占位接口或新增公共框架。</summary>
    [TestMethod]
    public void CompleteInterfaces()
    {
        var type = typeof(PostgreSqlMaterialDemandDocumentRepository);
        Assert.IsTrue(typeof(IMaterialDemandDocumentRepository).IsAssignableFrom(type));
        Assert.IsTrue(typeof(IMaterialDemandTransactionPreparation).IsAssignableFrom(type));
        Assert.AreEqual(8,type.GetMethods(BindingFlags.Public|BindingFlags.Instance|BindingFlags.DeclaredOnly).Length);
    }
    /// <summary>空枚举在父事务内也不获取租约、不执行SQL、不结束父事务。</summary>
    [TestMethod]
    public async Task EmptyInputs()
    {
        using var f = new Fixture(); var tx=f.Session.Transaction;
        Assert.HasCount(0,await f.Repository.AddDecompositionAsync([]));
        Assert.HasCount(0,await f.Repository.AssessSourceExitForUpdateAsync([]));
        Assert.HasCount(0,f.Connection.Commands); Assert.AreSame(tx,f.Session.Transaction);
    }
    /// <summary>保留原LINQ与foreach空引用异常，不添加批量业务验证。</summary>
    [TestMethod]
    public async Task NullInputs()
    {
        using var f=new Fixture();
        await Assert.ThrowsExactlyAsync<ArgumentNullException>(()=>f.Repository.AssessSourceExitForUpdateAsync(null!));
        await Assert.ThrowsExactlyAsync<NullReferenceException>(()=>f.Repository.AddDecompositionAsync(null!));
        await Assert.ThrowsExactlyAsync<NullReferenceException>(()=>f.Repository.AddDecompositionAsync([null!]));
        Assert.HasCount(0,f.Connection.Commands);
    }
    /// <summary>逐行枚举先打戳补身份版本；缺租约阻断首行，不预枚举第二行。</summary>
    [TestMethod]
    [DataRow(0)] [DataRow(-5)]
    public async Task AddSideEffectsBeforeMissingCoverage(int version)
    {
        using var f=new Fixture();var row=new NetDemand{Uid=Guid.Empty,Version=version,GrossDemandId=Guid.NewGuid(),IsDeleted=true};
        var moved=0;
        IEnumerable<NetDemand> Rows(){moved++;yield return row;moved++;throw new InvalidOperationException("不应预枚举");}
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(()=>f.Repository.AddDecompositionAsync(Rows()));
        Assert.AreEqual(1,moved);Assert.AreNotEqual(Guid.Empty,row.Uid);Assert.AreEqual(1,row.Version);
        Assert.IsNotNull(row.CreatedDate);Assert.AreEqual(row.CreatedDate,row.UpdatedDate);Assert.IsTrue(row.IsDeleted);
        Assert.HasCount(0,f.Connection.Commands);
    }
    /// <summary>已给审计和正版本在拒绝写入后仍原样保留。</summary>
    [TestMethod]
    public async Task AddPreservesProvidedAudit()
    {
        using var f=new Fixture();var now=new DateTime(2026,10,5);var uid=Guid.NewGuid();
        var row=new NetDemand{Uid=uid,Version=9,GrossDemandId=Guid.NewGuid(),CreatedDate=now,UpdatedDate=now,CreatedBy="",UpdatedBy="原文"};
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(()=>f.Repository.AddDecompositionAsync([row]));
        Assert.AreEqual(uid,row.Uid);Assert.AreEqual(9,row.Version);Assert.AreEqual(now,row.CreatedDate);Assert.AreEqual("",row.CreatedBy);Assert.AreEqual("原文",row.UpdatedBy);
    }
    /// <summary>直接Gross三读写缺父租约时必须在任何SQL之前拒绝，包括Empty身份。</summary>
    [TestMethod]
    [DataRow("active")] [DataRow("revision")] [DataRow("assess")] [DataRow("rollback")]
    public async Task MissingGrossCoverage(string operation)
    {
        using var f=new Fixture();var id=Guid.Empty;var tx=f.Session.Transaction;
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(async ()=>
        {
            switch(operation){case "active":await f.Repository.GetActiveByGrossDemandForUpdateAsync(id);break;case "revision":await f.Repository.GetNextDecompositionRevisionForUpdateAsync(id);break;case "assess":await f.Repository.AssessSourceExitForUpdateAsync([id,id]);break;default:await f.Repository.RollbackRevisionAsync(id,1," 原因 ");break;}
        });
        Assert.HasCount(0,f.Connection.Commands);Assert.AreSame(tx,f.Session.Transaction);
    }
    /// <summary>未知Net原定位缺失返回空或零；存在定位则要求真实Gross覆盖，只执行一次定位且不晚补锁。</summary>
    [TestMethod]
    [DataRow(false,false)] [DataRow(false,true)] [DataRow(true,false)] [DataRow(true,true)]
    public async Task LocatorBeforeCoverage(bool family,bool exists)
    {
        using var f=new Fixture();var table=new DataTable();table.Columns.Add("GrossDemandId",typeof(Guid));table.Columns.Add("RootNetDemandId",typeof(Guid));
        if(exists)table.Rows.Add(Guid.NewGuid(),Guid.NewGuid());f.Connection.Results=[table];var id=Guid.NewGuid();
        if(exists)await Assert.ThrowsExactlyAsync<InvalidOperationException>(async ()=>{if(family)await f.Repository.GetActiveFamilyForUpdateAsync(id);else await f.Repository.UpdateSuggestedPurchaseSupplierAsync(id,5,null);});
        else if(family)Assert.HasCount(0,await f.Repository.GetActiveFamilyForUpdateAsync(id));else Assert.AreEqual(0,await f.Repository.UpdateSuggestedPurchaseSupplierAsync(id,5,null));
        Assert.HasCount(1,f.Connection.Commands);Assert.AreEqual(id,f.Connection.Commands[0].Values["NetDemandId"]);Assert.AreSame(f.Session.Transaction,f.Connection.Commands[0].Transaction);Assert.AreEqual(0,f.Connection.Commits);Assert.AreEqual(0,f.Connection.Rollbacks);
    }
    /// <summary>共同能力委托保留空集合检查和父事务先行拒绝，不借助假的Npgsql连接通过准备。</summary>
    [TestMethod]
    public async Task PreparationDelegationBoundaries()
    {
        using var f=new Fixture();
        await Assert.ThrowsExactlyAsync<ArgumentNullException>(()=>f.Repository.AcquireMaterialBeforeTransactionAsync(null!,[],[],[]));
        await Assert.ThrowsExactlyAsync<ArgumentNullException>(()=>f.Repository.AcquireMaterialBeforeTransactionAsync([],null!,[],[]));
        await Assert.ThrowsExactlyAsync<ArgumentNullException>(()=>f.Repository.AcquireMaterialBeforeTransactionAsync([],[],null!,[]));
        await Assert.ThrowsExactlyAsync<ArgumentNullException>(()=>f.Repository.AcquireMaterialBeforeTransactionAsync([],[],[],null!));
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(()=>f.Repository.AcquireMaterialBeforeTransactionAsync([],[],[],[]));
        Assert.HasCount(0,f.Connection.Commands);
    }
    /// <summary>本例始终有父事务，内存连接仅检查拒绝和定位，不代表PG锁或业务资格。</summary>
    private sealed class Fixture:IDisposable
    {
        public readonly RecordingConnection Connection=new();public readonly DbSession Session;public readonly PostgreSqlMaterialDemandDocumentRepository Repository;
        public Fixture(){Session=new(new Factory(Connection));Session.Transaction=Connection.BeginTransaction();var user=new Identity();Repository=new(new(Session,NullLoggerFactory.Instance,new AuditStampService(user),user));}
        public void Dispose()=>Session.Dispose();
    }
    /// <summary>只创建指定内存连接，沿PG异常分类不接触网络。</summary>
    private sealed class Factory(RecordingConnection connection):IDbConnectionFactory
    {
        public IDbConnection CreateConnection()=>connection;
        public SwiftExport.Core.Interfaces.Data.IDatabaseExceptionClassifier Errors=>PostgreSqlExceptionClassifier.Instance;
    }
    /// <summary>离线只读用户，仓储不调用打戳。</summary>
    private sealed class Identity:ICurrentUser{public Guid? UserId=>null;public string UserName=>"来源交付离线";public bool IsAuthenticated=>false;}
    /// <summary>记录参数、阶段和reader释放；数据表不能证明PG批次推进或服务器错误排空。</summary>
    private sealed class RecordingConnection:DbConnection
    {
        public DataTable[] Results=[];
        public bool ReaderClosed;
        public int Begins,Commits,Rollbacks,NextResults,Disposals;
        public int[] ReadCounts=new int[2];
        public CancellationToken LastToken;
        public readonly List<(string Sql,Dictionary<string,object?> Values,Dictionary<string,NpgsqlDbType> Types,DbTransaction? Transaction)> Commands=[];
        [AllowNull]public override string ConnectionString{get;set;}="";
        public override string Database=>"offline";public override string DataSource=>"none";public override string ServerVersion=>"test";public override ConnectionState State=>ConnectionState.Open;
        public override void Open(){}public override void Close(){}public override void ChangeDatabase(string databaseName)=>throw new NotSupportedException();
        protected override DbTransaction BeginDbTransaction(IsolationLevel level){Begins++;return new RecordingTransaction(this);}
        protected override DbCommand CreateDbCommand()=>new RecordingCommand(this);
        /// <summary>只记录调用者事务动作。</summary>
        private sealed class RecordingTransaction(RecordingConnection owner):DbTransaction
        {protected override DbConnection DbConnection=>owner;public override IsolationLevel IsolationLevel=>IsolationLevel.ReadCommitted;public override void Commit()=>owner.Commits++;public override void Rollback()=>owner.Rollbacks++;}
        /// <summary>借用真实Npgsql参数容器，并以数据表返回单个结果。</summary>
        private sealed class RecordingCommand(RecordingConnection owner):DbCommand
        {
            private readonly NpgsqlCommand container=new();
            [AllowNull]public override string CommandText{get;set;}="";public override int CommandTimeout{get;set;}public override CommandType CommandType{get;set;}
            public override bool DesignTimeVisible{get;set;}public override UpdateRowSource UpdatedRowSource{get;set;}
            protected override DbConnection? DbConnection{get;set;}=owner;protected override DbTransaction? DbTransaction{get;set;}
            protected override DbParameterCollection DbParameterCollection=>container.Parameters;protected override DbParameter CreateDbParameter()=>new NpgsqlParameter();
            public override void Cancel(){}public override void Prepare(){}public override int ExecuteNonQuery()=>throw new NotSupportedException();
            /// <summary>执行前记录真实命令及独立参数类型。</summary>
            private void Record(){owner.Commands.Add((CommandText,container.Parameters.Cast<NpgsqlParameter>().ToDictionary(x=>x.ParameterName,x=>(object?)x.Value),container.Parameters.Cast<NpgsqlParameter>().ToDictionary(x=>x.ParameterName,x=>x.NpgsqlDbType),DbTransaction));}
            public override object ExecuteScalar(){Record();return false;}
            public override Task<object?> ExecuteScalarAsync(CancellationToken token){owner.LastToken=token;token.ThrowIfCancellationRequested();return Task.FromResult<object?>(ExecuteScalar());}
            protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior){Record();return new RecordingReader(owner,new DataTableReader(owner.Results));}
            protected override Task<DbDataReader> ExecuteDbDataReaderAsync(CommandBehavior behavior,CancellationToken token){owner.LastToken=token;token.ThrowIfCancellationRequested();return Task.FromResult(ExecuteDbDataReader(behavior));}
            protected override void Dispose(bool disposing){if(disposing)container.Dispose();base.Dispose(disposing);}
        }
        /// <summary>转发全部列类型，只观察各结果Read/NextResult/Dispose；不自动排空未读取的结果。</summary>
        private sealed class RecordingReader(RecordingConnection owner,DbDataReader inner):DbDataReader
        {
            private int phase;private bool disposed;
            /// <summary>观察当前结果读取次数，原行和列仍由内部reader提供。</summary>
            public override bool Read(){owner.ReadCounts[phase]++;return inner.Read();}
            /// <summary>观察结果推进边界，不主动排空未读结果或改变驱动返回。</summary>
            public override bool NextResult(){owner.NextResults++;var result=inner.NextResult();if(result)phase++;return result;}
            public override Task<bool> ReadAsync(CancellationToken token){owner.LastToken=token;token.ThrowIfCancellationRequested();return Task.FromResult(Read());}
            public override Task<bool> NextResultAsync(CancellationToken token){owner.LastToken=token;token.ThrowIfCancellationRequested();return Task.FromResult(NextResult());}
            public override void Close(){inner.Close();owner.ReaderClosed=true;}
            protected override void Dispose(bool disposing){if(disposing&&!disposed){disposed=true;owner.Disposals++;inner.Dispose();owner.ReaderClosed=true;}base.Dispose(disposing);}
            public override int FieldCount=>inner.FieldCount;public override bool HasRows=>inner.HasRows;public override bool IsClosed=>inner.IsClosed;public override int RecordsAffected=>inner.RecordsAffected;public override int Depth=>inner.Depth;
            public override object this[int ordinal]=>inner[ordinal];public override object this[string name]=>inner[name];
            public override string GetName(int ordinal)=>inner.GetName(ordinal);public override string GetDataTypeName(int ordinal)=>inner.GetDataTypeName(ordinal);public override Type GetFieldType(int ordinal)=>inner.GetFieldType(ordinal);
            public override object GetValue(int ordinal)=>inner.GetValue(ordinal);public override int GetValues(object[] values)=>inner.GetValues(values);public override int GetOrdinal(string name)=>inner.GetOrdinal(name);public override bool IsDBNull(int ordinal)=>inner.IsDBNull(ordinal);
            public override bool GetBoolean(int ordinal)=>inner.GetBoolean(ordinal);public override byte GetByte(int ordinal)=>inner.GetByte(ordinal);public override long GetBytes(int ordinal,long offset,byte[]? buffer,int bufferOffset,int length)=>inner.GetBytes(ordinal,offset,buffer,bufferOffset,length);
            public override char GetChar(int ordinal)=>inner.GetChar(ordinal);public override long GetChars(int ordinal,long offset,char[]? buffer,int bufferOffset,int length)=>inner.GetChars(ordinal,offset,buffer,bufferOffset,length);
            public override Guid GetGuid(int ordinal)=>inner.GetGuid(ordinal);public override short GetInt16(int ordinal)=>inner.GetInt16(ordinal);public override int GetInt32(int ordinal)=>inner.GetInt32(ordinal);public override long GetInt64(int ordinal)=>inner.GetInt64(ordinal);
            public override float GetFloat(int ordinal)=>inner.GetFloat(ordinal);public override double GetDouble(int ordinal)=>inner.GetDouble(ordinal);public override string GetString(int ordinal)=>inner.GetString(ordinal);public override decimal GetDecimal(int ordinal)=>inner.GetDecimal(ordinal);public override DateTime GetDateTime(int ordinal)=>inner.GetDateTime(ordinal);
            public override IEnumerator GetEnumerator()=>((IEnumerable)inner).GetEnumerator();public override DataTable? GetSchemaTable()=>inner.GetSchemaTable();
        }
    }
}
