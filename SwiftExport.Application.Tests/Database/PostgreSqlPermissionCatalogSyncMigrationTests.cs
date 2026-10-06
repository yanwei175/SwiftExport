using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Text.RegularExpressions;
using Dapper;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Npgsql;
using NpgsqlTypes;
using SwiftExport.Contracts.DTOs.Auth;
using SwiftExport.Infrastructure.Data;
using SwiftExport.Tools.DbSeed;

namespace SwiftExport.Application.Tests.Database;

/// <summary>真实同步类与Dapper的无网络传输测试；不把预定读取结果当作PG比较/锁/事务验收。</summary>
[TestClass, DoNotParallelize]
public sealed class PostgreSqlPermissionCatalogSyncMigrationTests
{
    /// <summary>只读反射核验固定绑定的NULL文本显式text，不覆盖生产目录或公开计划API。</summary>
    [TestMethod]
    public async Task NullTextBinding_RemainsExplicit()
    {
        var type=typeof(PostgreSqlPermissionCatalogSync);
        var planType=type.GetNestedType("PlannedPermission",BindingFlags.NonPublic)!;
        var plan=Activator.CreateInstance(planType,BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance,null,
            [new PermissionDefinition("binding-only",null!,null!,true),1,null],null)!;
        var parameters=(DynamicParameters)type.GetMethod("BindDefinition",BindingFlags.NonPublic|BindingFlags.Static)!.Invoke(null,[plan])!;
        // 通过真实Dapper传输生成Identity；不能传null伪造库内部绑定上下文。
        using var fixture=new Fixture();await fixture.Session.Connection.ExecuteAsync("UPDATE binding-only",parameters);
        var bound=fixture.Connection.Writes.Single().Parameters.ToDictionary(x=>x.ParameterName);
        foreach(var name in new[]{"DisplayName","GroupKey"})
        {Assert.AreEqual(DBNull.Value,bound[name].Value);Assert.AreEqual(NpgsqlDbType.Text,bound[name].NpgsqlDbType);}
        Assert.AreEqual(NpgsqlDbType.Integer,bound["SortOrder"].NpgsqlDbType);
    }

    /// <summary>dry-run同首锁/事务和全目录计划；实际写只更新既有三列，目录外始终保留。</summary>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task PlanAndUpdate_PreserveScope(bool dryRun)
    {
        using var fixture = new Fixture(); using var cancel = new CancellationTokenSource();
        fixture.Connection.Rows.Rows[0][2] = "旧显示"; fixture.Connection.Rows.Rows[0][3] = "旧组";
        fixture.Connection.Rows.Rows[0][4] = -1;
        fixture.Connection.Rows.Rows.Add(Guid.NewGuid(), "ZZ_EXTERNAL", "外部", "外部", 9000);
        await fixture.Sync.RunAsync(dryRun, cancel.Token);
        PrefixAndTransaction(fixture, cancel.Token);
        Assert.AreEqual(dryRun ? 0 : 1, fixture.Connection.Writes.Count);
        StringAssert.Contains(fixture.Output.ToString(), "新增 0，更新 1，目录外 1");
        StringAssert.Contains(fixture.Output.ToString(), "目录外保留：ZZ_EXTERNAL");
        Assert.IsNull(fixture.Session.Transaction); Assert.AreEqual(1, fixture.Connection.Transaction!.Commits);
        Assert.IsTrue(fixture.Connection.Transaction.Disposed); Assert.AreEqual(0, fixture.Connection.Transaction.Rollbacks);
        var read = fixture.Connection.Commands.Single(x => x.Kind == "read");
        Assert.IsFalse(read.Sql.Contains("WHERE", StringComparison.OrdinalIgnoreCase), "必须读入所有状态和目录外定义。");
        if (!dryRun)
        {
            var write = fixture.Connection.Writes.Single(); var set = Regex.Match(write.Sql, @"SET(.*?)WHERE", RegexOptions.Singleline).Groups[1].Value;
            CollectionAssert.AreEquivalent(new[] { "display_name", "group_key", "sort_order" }, Regex.Matches(set, "\"([^\"]+)\"\\s*=").Select(x => x.Groups[1].Value).ToArray());
            Assert.AreEqual(PermissionCatalog.All[0].PermissionKey, write.Parameters.Single(x => x.ParameterName == "PermissionKey").Value);
            Assert.AreEqual(NpgsqlDbType.Uuid, write.Parameters.Single(x => x.ParameterName == "Uid").NpgsqlDbType);
            Assert.AreEqual(NpgsqlDbType.Integer, write.Parameters.Single(x => x.ParameterName == "SortOrder").NpgsqlDbType);
            Assert.IsTrue(write.Parameters.Where(x => x.ParameterName is "PermissionKey" or "DisplayName" or "GroupKey").All(x => x.NpgsqlDbType == NpgsqlDbType.Text));
        }
    }

    /// <summary>缺少一个代码键只插定义，保持初始状态、UTC墙上审计和typed参数，不补关系。</summary>
    [TestMethod]
    public async Task MissingKey_InsertsOnlyDefinition()
    {
        using var fixture = new Fixture(); fixture.Connection.Rows.Rows.RemoveAt(0);
        await fixture.Sync.RunAsync(false);
        var command = fixture.Connection.Writes.Single(); StringAssert.Contains(command.Sql, "INSERT INTO \"public\".\"sys_permission\"");
        StringAssert.Contains(command.Sql, "clock_timestamp() AT TIME ZONE 'UTC'");
        StringAssert.Contains(command.Sql, "@Uid,1,FALSE");
        StringAssert.Contains(command.Sql, "'DbSeed'"); StringAssert.Contains(command.Sql, "@SortOrder,TRUE");
        Assert.IsTrue(command.Parameters.Single(x => x.ParameterName == "Uid").Value is Guid uid && uid != Guid.Empty);
        Assert.AreEqual(1, command.Parameters.Single(x => x.ParameterName == "SortOrder").Value);
        Assert.IsFalse(command.Sql.Contains("sys_role_permission", StringComparison.Ordinal));
    }

    /// <summary>大小写拼写漂移与字典重复均在DML前拒绝，不依CI自动改Key或合并。</summary>
    [TestMethod]
    [DataRow("spelling")]
    [DataRow("duplicate")]
    [DataRow("duplicate-case")]
    public async Task KeyPolicy_RejectsBeforeWriting(string mode)
    {
        using var fixture = new Fixture(); var first = fixture.Connection.Rows.Rows[0];
        if (mode == "spelling") first[1] = ((string)first[1]).ToLowerInvariant();
        else fixture.Connection.Rows.Rows.Add(Guid.NewGuid(), mode == "duplicate" ? first[1] : ((string)first[1]).ToLowerInvariant(), "重复", "重复", 1);
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Sync.RunAsync(false));
        Assert.AreEqual(0, fixture.Connection.Writes.Count); FailedAndCleared(fixture);
    }

    /// <summary>缺结构只诊断、不DDL；已有父事务拒绝接管并保持原事务。</summary>
    [TestMethod]
    public async Task SchemaFailure_RollsBackAndForeignTransactionIsUntouched()
    {
        using var fixture = new Fixture(); fixture.Connection.SchemaValid = false;
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Sync.RunAsync(true));
        StringAssert.Contains(error.Message, "目录同步不迁移结构");
        Assert.AreEqual(3, fixture.Connection.Commands.Count); FailedAndCleared(fixture);
        var foreign = fixture.Connection.BeginTransaction(); fixture.Session.Transaction = foreign;
        var before = fixture.Connection.Commands.Count;
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Sync.RunAsync(false));
        Assert.AreSame(foreign, fixture.Session.Transaction); Assert.AreEqual(before, fixture.Connection.Commands.Count);
    }

    /// <summary>只转换首角色/权限锁缺表；其他数据库错误实例原样，回滚用None。</summary>
    [TestMethod]
    [DataRow("42P01", 1)]
    [DataRow("42P01", 2)]
    [DataRow("40001", 5)]
    [DataRow("40P01", 5)]
    [DataRow("23505", 5)]
    [DataRow("42703", 3)]
    public async Task DatabaseFailure_PreservesClassification(string code, int call)
    {
        using var fixture = new Fixture(); fixture.Connection.Rows.Rows[0][2] = "需更新";
        var original = new PostgresException("离线错误", "ERROR", "ERROR", code);
        fixture.Connection.Failure = original; fixture.Connection.FailCall = call;
        Exception? observed = null; try { await fixture.Sync.RunAsync(false); } catch (Exception error) { observed = error; }
        if (code == "42P01") { Assert.IsInstanceOfType<InvalidOperationException>(observed); Assert.AreSame(original, observed.InnerException); }
        else Assert.AreSame(original, observed);
        FailedAndCleared(fixture);
    }

    /// <summary>取消在事务前无SQL；执行中取消原样，不变成dry-run成功。</summary>
    [TestMethod]
    public async Task Cancellation_PreservesCallerAndCleanup()
    {
        using var fixture = new Fixture(); using var source = new CancellationTokenSource(); source.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(() => fixture.Sync.RunAsync(true, source.Token));
        Assert.AreEqual(0, fixture.Connection.Commands.Count); Assert.AreEqual(0, fixture.Connection.Begins);
        var original = new OperationCanceledException(source.Token); fixture.Connection.Failure = original; fixture.Connection.FailCall = 2;
        Exception? observed = null; try { await fixture.Sync.RunAsync(true); } catch (Exception error) { observed = error; }
        Assert.AreSame(original, observed); FailedAndCleared(fixture);
    }

    /// <summary>零写入、提交失败或清理失败不重试，原失败不被回滚/Dispose错误遮盖。</summary>
    [TestMethod]
    [DataRow("zero")]
    [DataRow("commit")]
    [DataRow("cleanup")]
    public async Task AtomicFailure_IsNotRetriedOrMasked(string mode)
    {
        using var fixture = new Fixture(); fixture.Connection.Rows.Rows[0][2] = "需更新";
        var original = new IOException("离线原失败");
        if (mode == "zero") fixture.Connection.WriteCount = 0;
        else if (mode == "commit") fixture.Connection.CommitFailure = original;
        else { fixture.Connection.Failure = original; fixture.Connection.FailCall = 5; fixture.Connection.CleanupFailure = new IOException("清理失败"); }
        Exception? observed = null; try { await fixture.Sync.RunAsync(false); } catch (Exception error) { observed = error; }
        if (mode == "zero") Assert.IsInstanceOfType<InvalidOperationException>(observed); else Assert.AreSame(original, observed);
        Assert.AreEqual(1, fixture.Connection.Writes.Count); Assert.AreEqual(1, fixture.Connection.Begins);
        FailedAndCleared(fixture); if (mode == "cleanup") Assert.IsTrue(fixture.Connection.Closed);
    }

    /// <summary>核验每命令同事务/token，首两条纯LOCK先于schema或数据。</summary>
    private static void PrefixAndTransaction(Fixture fixture, CancellationToken token)
    {
        var commands = fixture.Connection.Commands;
        StringAssert.Contains(commands[0].Sql, "LOCK TABLE \"public\".\"sys_role\" IN SHARE ROW EXCLUSIVE MODE");
        StringAssert.Contains(commands[1].Sql, "LOCK TABLE \"public\".\"sys_permission\" IN SHARE ROW EXCLUSIVE MODE");
        Assert.AreEqual("scalar", commands[2].Kind); Assert.AreEqual("read", commands[3].Kind);
        Assert.AreEqual(IsolationLevel.Serializable, fixture.Connection.Transaction!.IsolationLevel);
        foreach (var command in commands) { Assert.AreSame(fixture.Connection.Transaction, command.Transaction); Assert.AreEqual(token, command.Token); }
    }
    /// <summary>失败不得留事务引用，回滚不受原已取消令牌阻断。</summary>
    private static void FailedAndCleared(Fixture fixture)
    {
        Assert.IsNull(fixture.Session.Transaction); Assert.AreEqual(1, fixture.Connection.Transaction!.Rollbacks);
        Assert.AreEqual(CancellationToken.None, fixture.Connection.Transaction.RollbackToken); Assert.IsTrue(fixture.Connection.Transaction.Disposed);
    }

    /// <summary>截取原Console计划，隔离无网络fixture和可观察会话。</summary>
    private sealed class Fixture : IDisposable
    {
        private readonly TextWriter original = Console.Out;
        internal readonly StringWriter Output = new(); internal readonly Transport Connection = new();
        internal readonly DbSession Session; internal readonly PostgreSqlPermissionCatalogSync Sync;
        internal Fixture() { Console.SetOut(Output); Session = new(new Factory(Connection)); Sync = new(Session); }
        /// <summary>始终归还Console及本例连接。</summary>
        public void Dispose() { try { Session.Dispose(); } finally { Console.SetOut(original); Output.Dispose(); } }
    }
    /// <summary>仅提供无网络传输。</summary>
    private sealed class Factory(Transport connection) : IDbConnectionFactory
    {
        /// <summary>返回记录连接。</summary>
        public IDbConnection CreateConnection() => connection;
    }
    /// <summary>一次实际Dapper传输的SQL、类型参数、事务及令牌。</summary>
    private sealed record Call(string Sql, string Kind, NpgsqlParameter[] Parameters, DbTransaction? Transaction, CancellationToken Token);

    /// <summary>完整当前代码目录作为明确传输行；不解释SQL或提供PG引擎。</summary>
    private sealed class Transport : DbConnection
    {
        internal readonly DataTable Rows = new(); internal readonly List<Call> Commands = []; internal List<Call> Writes => Commands.Where(x=>x.Kind=="write").ToList();
        internal bool SchemaValid = true; internal bool Closed; internal int Begins; internal int WriteCount = 1; internal int FailCall;
        internal Exception? Failure; internal Exception? CommitFailure; internal Exception? CleanupFailure; internal TransactionStub? Transaction;
        internal Transport()
        {
            Rows.Columns.Add("Uid",typeof(Guid)); Rows.Columns.Add("Key",typeof(string)); Rows.Columns.Add("Name",typeof(string)); Rows.Columns.Add("Group",typeof(string)); Rows.Columns.Add("SortOrder",typeof(int));
            for(var i=0;i<PermissionCatalog.All.Count;i++){var d=PermissionCatalog.All[i];Rows.Rows.Add(Guid.NewGuid(),d.PermissionKey,d.DisplayName,d.GroupKey,i+1);}
        }
        [AllowNull] public override string ConnectionString { get; set; } = "";
        /// <summary>无网络测试库。</summary>
        public override string Database => "offline";
        /// <summary>不提供真实实例。</summary>
        public override string DataSource => "none";
        /// <summary>标识替身。</summary>
        public override string ServerVersion => "test";
        /// <summary>连接状态随清理关闭变化。</summary>
        public override ConnectionState State => Closed ? ConnectionState.Closed : ConnectionState.Open;
        /// <summary>无网络打开。</summary>
        public override void Open() => Closed = false;
        /// <summary>记录清理关闭。</summary>
        public override void Close() => Closed = true;
        /// <summary>拒绝改库。</summary>
        public override void ChangeDatabase(string databaseName) => throw new NotSupportedException();
        /// <summary>记录唯一自有事务。</summary>
        protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel){Begins++;return Transaction=new(this,isolationLevel);}
        /// <summary>提供参数观察命令。</summary>
        protected override DbCommand CreateDbCommand()=>new Command(this);
        /// <summary>记录命令后按指定边界抛原异常。</summary>
        internal void Capture(Command command,string kind,CancellationToken token)
        {
            Commands.Add(new(command.CommandText,kind,command.Parameters.Cast<NpgsqlParameter>().Select(x=>x.Clone()).ToArray(),command.Parent,token));
            if(Commands.Count==FailCall&&Failure is not null)throw Failure;
        }
        /// <summary>Dispose关闭传输，证明清理失败后不复用连接。</summary>
        protected override void Dispose(bool disposing){Closed=true;base.Dispose(disposing);}

        /// <summary>记录原生事务边界与失败，不执行数据库提交。</summary>
        internal sealed class TransactionStub(Transport owner,IsolationLevel isolation):DbTransaction
        {
            internal int Commits;internal int Rollbacks;internal bool Disposed;internal CancellationToken RollbackToken { get; set; }
            /// <summary>实际请求的隔离。</summary>
            public override IsolationLevel IsolationLevel=>isolation;
            /// <summary>绑定记录连接。</summary>
            protected override DbConnection DbConnection=>owner;
            /// <summary>同步提交沿同一失败边界。</summary>
            public override void Commit()=>CommitAsync().GetAwaiter().GetResult();
            /// <summary>记录提交请求，保留指定失败。</summary>
            public override Task CommitAsync(CancellationToken cancellationToken=default){Commits++;return owner.CommitFailure is null?Task.CompletedTask:Task.FromException(owner.CommitFailure);}
            /// <summary>同步回滚沿同一边界。</summary>
            public override void Rollback()=>RollbackAsync().GetAwaiter().GetResult();
            /// <summary>记录回滚None与清理失败。</summary>
            public override Task RollbackAsync(CancellationToken cancellationToken=default){Rollbacks++;RollbackToken=cancellationToken;return owner.CleanupFailure is null?Task.CompletedTask:Task.FromException(owner.CleanupFailure);}
            /// <summary>记录释放并按需抛清理错误。</summary>
            public override ValueTask DisposeAsync(){Disposed=true;return owner.CleanupFailure is null?ValueTask.CompletedTask:ValueTask.FromException(owner.CleanupFailure);}
        }
        /// <summary>真实Npgsql参数容器加无网络执行截获。</summary>
        internal sealed class Command(Transport owner):DbCommand
        {
            private readonly NpgsqlCommand bag=new();internal DbTransaction? Parent=>DbTransaction;
            [AllowNull]public override string CommandText{get;set;}="";
            public override int CommandTimeout{get;set;}public override CommandType CommandType{get;set;}public override bool DesignTimeVisible{get;set;}public override UpdateRowSource UpdatedRowSource{get;set;}
            protected override DbConnection? DbConnection{get;set;}=owner;protected override DbTransaction? DbTransaction{get;set;}
            protected override DbParameterCollection DbParameterCollection=>bag.Parameters;
            /// <summary>创建真实参数。</summary>
            protected override DbParameter CreateDbParameter()=>new NpgsqlParameter();
            /// <summary>无网络取消。</summary>
            public override void Cancel(){}
            /// <summary>拒绝未要求预编译。</summary>
            public override void Prepare()=>throw new NotSupportedException();
            /// <summary>同步写路径也截获。</summary>
            public override int ExecuteNonQuery()=>ExecuteNonQueryAsync(default).GetAwaiter().GetResult();
            /// <summary>截获锁与定义DML，不执行SQL。</summary>
            public override Task<int> ExecuteNonQueryAsync(CancellationToken token){owner.Capture(this,CommandText.StartsWith("LOCK",StringComparison.Ordinal)?"lock":"write",token);return Task.FromResult(CommandText.StartsWith("LOCK",StringComparison.Ordinal)?0:owner.WriteCount);}
            /// <summary>同步scalar也截获。</summary>
            public override object? ExecuteScalar()=>ExecuteScalarAsync(default).GetAwaiter().GetResult();
            /// <summary>仅返回明确schema结果。</summary>
            public override Task<object?> ExecuteScalarAsync(CancellationToken token){owner.Capture(this,"scalar",token);return Task.FromResult<object?>(owner.SchemaValid);}
            /// <summary>同步reader也截获。</summary>
            protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior)=>ExecuteDbDataReaderAsync(behavior,default).GetAwaiter().GetResult();
            /// <summary>提供typed目录行，空值行为由真实DataTableReader决定。</summary>
            protected override Task<DbDataReader> ExecuteDbDataReaderAsync(CommandBehavior behavior,CancellationToken token){owner.Capture(this,"read",token);return Task.FromResult<DbDataReader>(owner.Rows.CreateDataReader());}
            /// <summary>释放参数容器不接管会话。</summary>
            protected override void Dispose(bool disposing){if(disposing)bag.Dispose();base.Dispose(disposing);}
        }
    }
}

