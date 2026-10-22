using System.Reflection;
using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Npgsql;
using NpgsqlTypes;
using SwiftExport.Contracts.DTOs.Procurement.OrderManagement;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.Core.Models.Procurement;
using SwiftExport.Core.Interfaces.Data;
using SwiftExport.Infrastructure.Contexts;
using SwiftExport.Infrastructure.Data;
using SwiftExport.Infrastructure.Repositories.Procurement.PurchaseOrders.Reversal;

namespace SwiftExport.Application.Tests.Procurement;

/// <summary>核对反向入库异常的采购范围和最终阻断门禁；离线 SQL 契约不替代真实 PG 锁与数据执行。</summary>
[TestClass]
public sealed class ProcurementReversalInboundScopeTests
{
    /// <summary>调用实际仓储锁定入口，确认全部历史行身份、UUID数组类型和父事务传给异常查询，不连接数据库。</summary>
    [TestMethod]
    [DataRow(false,10)]
    [DataRow(true,10)]
    [DataRow(true,11)]
    [DataRow(true,12)]
    public async Task LockBindsCurrentAndDeletedLineIdsToTheSameTransaction(bool anomaly,int documentType)
    {
        var uid=Guid.NewGuid();var active=Guid.NewGuid();var deleted=Guid.NewGuid();
        var connection=new RecordingConnection();
        var head=new DataTable();head.Columns.Add("Uid",typeof(Guid));head.Columns.Add("Version",typeof(int));head.Rows.Add(uid,1);
        var lines=new DataTable();lines.Columns.Add("Uid",typeof(Guid));lines.Columns.Add("IsDeleted",typeof(bool));
        lines.Rows.Add(active,false);lines.Rows.Add(deleted,true);
        // 按真实仓储顺序提供头行及相关整头明细；不模拟或声称执行了SQL范围谓词。
        var badHeads=Empty();var badHead=Guid.NewGuid();if(anomaly){badHeads.Rows.Add(badHead);badHeads.Rows.Add(badHead);}
        foreach(var table in new[]{Empty(),Empty(),head,lines,Empty(),badHeads})connection.Readers.Enqueue(table);
        var foreignLine=Guid.NewGuid();
        if(anomaly)
        {
            var inbounds=new DataTable();inbounds.Columns.Add("Uid",typeof(Guid));inbounds.Columns.Add("DocumentType",typeof(int));inbounds.Rows.Add(badHead,documentType);
            var inboundLines=new DataTable();inboundLines.Columns.Add("InboundDocumentId",typeof(Guid));inboundLines.Columns.Add("SourceLineId",typeof(Guid));inboundLines.Columns.Add("IsDeleted",typeof(bool));
            inboundLines.Rows.Add(badHead,active,false);inboundLines.Rows.Add(badHead,foreignLine,false);inboundLines.Rows.Add(badHead,deleted,true);
            connection.Readers.Enqueue(inbounds);connection.Readers.Enqueue(inboundLines);connection.Readers.Enqueue(Empty());
        }
        else connection.Readers.Enqueue(Empty());
        connection.Readers.Enqueue(Empty());
        connection.Scalars.Enqueue(false);connection.Scalars.Enqueue(false);
        using var session=new DbSession(new Factory(connection));
        using var transaction=connection.BeginTransaction(IsolationLevel.Serializable);session.Transaction=transaction;
        var repository=new PostgreSqlProcurementReversalRepository(new RepositoryBaseContext(session,NullLoggerFactory.Instance,null!,null!));
        var result=await repository.LockAsync(uid,default);
        Assert.IsNull(result.Error);
        Assert.AreEqual(anomaly,result.Facts!.HasUnattributedInbound);
        Assert.AreEqual(anomaly?1:0,result.Facts.UnattributedInboundHeadIds.Count);
        if(anomaly)Assert.IsTrue(result.Facts.UnattributedInboundHeadIds.Contains(badHead));
        if(anomaly)
        {
            Assert.AreEqual(documentType,(int)result.Facts.Inbounds.Single().DocumentType);
            Assert.HasCount(3,result.Facts.InboundLines);
            Assert.IsTrue(result.Facts.InboundLines.Any(line=>line.SourceLineId==foreignLine));
            Assert.IsTrue(result.Facts.InboundLines.Any(line=>line.SourceLineId==deleted && line.IsDeleted));
            var headQuery=connection.Commands.Single(item=>item.Sql.Contains("AS \"DocumentType\"",StringComparison.Ordinal));
            Assert.IsFalse(headQuery.Sql.Contains("document_type=",StringComparison.Ordinal),"读取相关整头不能过滤旧入库类型。");
        }
        Assert.IsFalse(result.Facts.HasConfirmationHistory);Assert.IsFalse(result.Facts.HasDownstreamHistory);
        var command=connection.Commands.Single(item=>item.Sql.Contains("p.uid IS NULL",StringComparison.Ordinal));
        CollectionAssert.AreEquivalent(new[]{active,deleted},(Guid[])command.Values["lineIds"]!);
        Assert.AreEqual(NpgsqlDbType.Array|NpgsqlDbType.Uuid,command.Types["lineIds"]);
        Assert.AreSame(transaction,command.Transaction);
        Assert.AreEqual(2,connection.Commands.Count(item=>item.Scalar));
        Assert.AreEqual(0,connection.Readers.Count);Assert.AreEqual(0,connection.Scalars.Count);
        session.Transaction=null;
    }

    /// <summary>异常以当前采购行找到整头，外层不按采购行过滤坏行；同头孤立行仍可触发阻断。</summary>
    [TestMethod]
    public void UnattributedInboundQueryScopesHeadsAndKeepsAllTheirLines()
    {
        var field=typeof(PostgreSqlProcurementReversalRepository).GetField("UnattributedInboundSql",BindingFlags.NonPublic|BindingFlags.Static);
        Assert.IsNotNull(field);
        var sql=(string)field.GetRawConstantValue()!;
        StringAssert.Contains(sql,"scope.inbound_document_id=h.uid AND scope.source_line_id=ANY(@lineIds)");
        StringAssert.Contains(sql,"JOIN public.inbound_document_line l ON l.inbound_document_id=h.uid");
        StringAssert.Contains(sql,"p.uid=l.source_line_id");
        Assert.IsFalse(sql.Contains("l.source_line_id=ANY(@lineIds)",StringComparison.Ordinal),"不能仅检查本单行而漏掉同头孤立来源。");
        StringAssert.Contains(sql,"h.document_type IN(11,12)");
        StringAssert.Contains(sql,"h.document_type=10 AND p.uid IS NULL");
        StringAssert.Contains(sql,"h.posting_status IN(1,8) OR h.lifecycle_status=0");
        StringAssert.Contains(sql,"FOR SHARE OF scope");
        StringAssert.Contains(sql,"FOR SHARE OF h,l");
    }

    /// <summary>三个操作仍拒绝相关入库异常；无异常时正常资格不因该门禁被阻断。</summary>
    [TestMethod]
    [DataRow(ProcurementReversalOperation.CancelConfirmation)]
    [DataRow(ProcurementReversalOperation.DeleteDraft)]
    [DataRow(ProcurementReversalOperation.Void)]
    public void RelatedInboundAnomalyStillBlocksEveryReversal(ProcurementReversalOperation operation)
    {
        var facts=new ProcurementReversalFacts
        {
            Head=new(){Uid=Guid.NewGuid(),Version=1,OriginKind=2,ConfirmationStatus=operation==ProcurementReversalOperation.CancelConfirmation
                ?ConfirmationStatusEnum.已确认:ConfirmationStatusEnum.待确认},
            Lines=[new(){Uid=Guid.NewGuid(),Version=1,Quantity=1,UnlinkedQty=1}]
        };
        var request=new ProcurementReversalPreviewRequest{ExpectedVersion=1};
        if(operation==ProcurementReversalOperation.DeleteDraft)
            request.Lines=[new(){PurchaseOrderLineId=facts.Lines[0].Uid,Version=1}];
        Assert.IsTrue(ProcurementReversalPolicy.Build(facts,operation,request).CanExecute);
        facts.UnattributedInboundHeadIds.Add(Guid.NewGuid());
        var preview=ProcurementReversalPolicy.Build(facts,operation,request);
        Assert.IsFalse(preview.CanExecute);
        Assert.IsTrue(preview.Blockers.Any(message=>message.Contains("来源无法归属",StringComparison.Ordinal)));
    }

    /// <summary>空查询保留Uid列形状，让Dapper按正常路径读取空集合。</summary>
    private static DataTable Empty(){var table=new DataTable();table.Columns.Add("Uid",typeof(Guid));return table;}

    /// <summary>仅向会话注入内存命令记录器，不创建网络连接。</summary>
    private sealed class Factory(RecordingConnection connection):IDbConnectionFactory
    {public IDbConnection CreateConnection()=>connection;}

    /// <summary>为实际Dapper调用提供排队结果并记录SQL、参数和事务，禁止执行写入。</summary>
    private sealed class RecordingConnection:DbConnection
    {
        internal readonly Queue<DataTable> Readers=new();
        internal readonly Queue<bool> Scalars=new();
        internal readonly List<(string Sql,Dictionary<string,object?> Values,Dictionary<string,NpgsqlDbType> Types,DbTransaction? Transaction,bool Scalar)> Commands=new();
        [AllowNull] public override string ConnectionString{get;set;}="";
        public override string Database=>"in-memory-test";
        public override string DataSource=>"none";
        public override string ServerVersion=>"test";
        public override ConnectionState State=>ConnectionState.Open;
        public override void ChangeDatabase(string databaseName)=>throw new NotSupportedException();
        public override void Open(){}public override void Close(){}
        protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel)=>new Transaction(this);
        protected override DbCommand CreateDbCommand()=>new Command(this);

        /// <summary>保留父事务身份；锁定仓储不得自行提交或回滚。</summary>
        private sealed class Transaction(RecordingConnection connection):DbTransaction
        {
            public override IsolationLevel IsolationLevel=>IsolationLevel.Serializable;
            protected override DbConnection DbConnection=>connection;
            public override void Commit()=>throw new AssertFailedException("仓储不得提交父事务。");
            public override void Rollback()=>throw new AssertFailedException("仓储不得回滚父事务。");
        }

        /// <summary>接收提供者参数并返回内存结果；同步与异步读取均不执行SQL。</summary>
        private sealed class Command(RecordingConnection connection):DbCommand
        {
            private readonly NpgsqlCommand parameters=new();
            [AllowNull] public override string CommandText{get;set;}="";
            public override int CommandTimeout{get;set;}
            public override CommandType CommandType{get;set;}
            public override bool DesignTimeVisible{get;set;}
            public override UpdateRowSource UpdatedRowSource{get;set;}
            protected override DbConnection? DbConnection{get;set;}=connection;
            protected override DbTransaction? DbTransaction{get;set;}
            protected override DbParameterCollection DbParameterCollection=>parameters.Parameters;
            protected override DbParameter CreateDbParameter()=>new NpgsqlParameter();
            public override void Cancel(){}public override void Prepare(){}
            public override int ExecuteNonQuery()=>throw new AssertFailedException("事实锁定不得写入。");
            public override object ExecuteScalar(){Capture(true);return connection.Scalars.Dequeue();}
            protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior){Capture(false);return connection.Readers.Dequeue().CreateDataReader();}
            /// <summary>复制实际命令参数，避免命令释放后丢失数组类型及事务证据。</summary>
            private void Capture(bool scalar)=>connection.Commands.Add((CommandText,
                parameters.Parameters.Cast<NpgsqlParameter>().ToDictionary(p=>p.ParameterName,p=>(object?)p.Value),
                parameters.Parameters.Cast<NpgsqlParameter>().ToDictionary(p=>p.ParameterName,p=>p.NpgsqlDbType),DbTransaction,scalar));
            protected override void Dispose(bool disposing){if(disposing)parameters.Dispose();base.Dispose(disposing);}
        }
    }
}
