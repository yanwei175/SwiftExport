using System.Data;
using Dapper;
using SwiftExport.Core.Common;
using SwiftExport.Core.Entities.QualityControl;
using SwiftExport.Core.Interfaces.QualityControl.AfterSales.Document;
using SwiftExport.Infrastructure.Contexts;
using SwiftExport.Infrastructure.DatabaseSchema;
using SwiftExport.Infrastructure.Repositories.Base;

namespace SwiftExport.Infrastructure.Repositories.QualityControl.AfterSales.Document;

/// <summary>借用当前请求会话完成 PostgreSQL 售后头行读写；保留固定持久化列、审计和版本，不接管事务。</summary>
public sealed class PostgreSqlAfterSalesDocumentRepository(RepositoryBaseContext context) : IAfterSalesDocumentRepository
{
    private IDbConnection Connection => context.Session.Connection;
    private IDbTransaction? Transaction => context.Session.Transaction;
    private static readonly EntityMetadata HeaderMetadata = PostgreSqlEntityMetadata.GetMetadata(typeof(AfterSalesHeader));
    private static readonly EntityMetadata LineMetadata = PostgreSqlEntityMetadata.GetMetadata(typeof(AfterSalesLine));
    private static readonly string[] HeaderColumns = ["Uid","Version","IsDeleted","CreatedDate","CreatedBy","UpdatedDate","UpdatedBy","AfterSalesNo","CustomerId","CustomerCodeSnapshot","CustomerNameSnapshot","AcceptedDate","OwnerId","DocumentStatus","ConfirmationStatus","DemandConfirmedAt","DemandConfirmedBy","ExpectedCompletionDate","Conclusion","Remark","OriginalSalesOrderLineId","CustomerComplaintNo","LocalFileFolder","ResponsibleSupplierId","IssueProductId","IssueSkuSnapshot","IssueCustomerSkuSnapshot","IssueLinkSkuSnapshot","IssueDescription","QualityCategoryId","CauseAnalysis","RequiredDate","ServiceAmountCny","ReturnedReceivedDate","TreatmentDescription"];
    private static readonly string[] LineColumns = ["Uid","Version","IsDeleted","CreatedDate","CreatedBy","UpdatedDate","UpdatedBy","HeaderId","LineNumber","ProductId","IsNonStandard","SkuSnapshot","CustomerSkuSnapshot","LinkSkuSnapshot","DescriptionSnapshot","ColorId","ColorSnapshot","SpecificationSnapshot","Quantity","UnitSnapshot","Remark"];

    /// <summary>读取未软删单头；指定锁定时锁实际返回行，锁寿命由调用方的业务事务决定，缺失返回 null。</summary>
    public Task<AfterSalesHeader?> GetHeaderAsync(Guid uid, bool forUpdate = false)
        => Connection.QuerySingleOrDefaultAsync<AfterSalesHeader>(
            $"SELECT {Projection(HeaderMetadata, HeaderColumns)} FROM {Table(HeaderMetadata)} WHERE {Column(HeaderMetadata, "Uid")}=@uid AND {Column(HeaderMetadata, "IsDeleted")}=FALSE{(forUpdate ? " FOR UPDATE" : "")}",
            new { uid }, Transaction);

    /// <summary>按原稳定顺序读取全部历史行；锁定模式先锁实际父头再锁行，在同一外层事务内为已有父头的空行集合提供锚点。</summary>
    public async Task<IReadOnlyList<AfterSalesLine>> GetLinesAsync(Guid uid, bool forUpdate = false)
    {
        if (forUpdate)
        {
            // 只锁已有父头（含软删头），不改变历史行返回资格；缺失父头没有可锁锚点，不虚构间隙保护。
            await Connection.QuerySingleOrDefaultAsync<Guid?>(
                $"SELECT {Column(HeaderMetadata, "Uid")} FROM {Table(HeaderMetadata)} WHERE {Column(HeaderMetadata, "Uid")}=@uid FOR UPDATE",
                new { uid }, Transaction);
        }
        var uidColumn = Column(LineMetadata, "Uid");
        return (await Connection.QueryAsync<AfterSalesLine>(
            $"SELECT {Projection(LineMetadata, LineColumns)} FROM {Table(LineMetadata)} WHERE {Column(LineMetadata, "HeaderId")}=@uid ORDER BY {Column(LineMetadata, "LineNumber")} ASC NULLS FIRST,{SqlGuidOrder(uidColumn)} ASC{(forUpdate ? " FOR UPDATE" : "")}",
            new { uid }, Transaction)).ToList();
    }

    /// <summary>在已有业务事务内按原单头固定列插入或乐观更新；返回是否恰好影响一行，并保留审计和实体版本副作用。</summary>
    public Task<bool> WriteHeaderAsync(AfterSalesHeader head, bool insert)
        => WriteAsync(HeaderMetadata, HeaderColumns, head, insert);

    /// <summary>在已有业务事务内按原行固定列插入或乐观更新；生产调用方负责先锁或写父头，再写行。</summary>
    public Task<bool> WriteLineAsync(AfterSalesLine line, bool insert)
        => WriteAsync(LineMetadata, LineColumns, line, insert);

    /// <summary>仅翻译固定列 SQL；新增保留调用方 Uid，更新不增加软删过滤，失败也保留原审计打戳时机。</summary>
    private async Task<bool> WriteAsync(EntityMetadata metadata, string[] columns, BaseEntity entity, bool insert)
    {
        if (Transaction is null) throw new InvalidOperationException("售后写入必须处于业务事务。");
        string sql;
        if (insert)
        {
            context.AuditStamp.StampForInsert(entity);
            entity.Version = 1;
            sql = $"INSERT INTO {Table(metadata)} ({string.Join(',', columns.Select(c => Column(metadata, c)))}) VALUES ({string.Join(',', columns.Select(c => "@" + c))})";
        }
        else
        {
            context.AuditStamp.StampForUpdate(entity);
            var writable = columns.Except(["Uid","Version","CreatedDate","CreatedBy"]);
            var version = Column(metadata, "Version");
            sql = $"UPDATE {Table(metadata)} SET {string.Join(',', writable.Select(c => $"{Column(metadata, c)}=@{c}"))},{version}={version}+1 WHERE {Column(metadata, "Uid")}=@Uid AND {version}=@Version";
        }
        var count = await Connection.ExecuteAsync(sql, PostgreSqlParameters.Entity(entity, metadata), Transaction);
        if (count == 1 && !insert) entity.Version++;
        return count == 1;
    }

    /// <summary>按共享 PostgreSQL 映射引用物理列；固定属性名不接受外部输入。</summary>
    private static string Column(EntityMetadata metadata, string property)
        => PostgreSqlNames.Quote(metadata.GetProperty(property).ColumnName);

    /// <summary>固定投影保留CLR别名；售后两个date列转换为无时区午夜时间，匹配原DateTime?实体而不改变存储或空值语义。</summary>
    private static string Projection(EntityMetadata metadata, IEnumerable<string> columns)
        => string.Join(',', columns.Select(c =>
            $"{Column(metadata, c)}{(metadata == HeaderMetadata && (c is "RequiredDate" or "ReturnedReceivedDate") ? "::timestamp without time zone" : "")} AS {PostgreSqlNames.Quote(c)}"));

    /// <summary>只使用共享元数据的模式和表名，不新增提供者或名称映射框架。</summary>
    private static string Table(EntityMetadata metadata)
        => PostgreSqlNames.Quote(metadata.SchemaName) + "." + PostgreSqlNames.Quote(metadata.TableName);

    /// <summary>用 SQL Server uniqueidentifier 的字节比较顺序保留同行号时的原稳定排序。</summary>
    private static string SqlGuidOrder(string column) => "ARRAY[" + string.Join(',',
        new[] { 10,11,12,13,14,15,8,9,7,6,5,4,3,2,1,0 }.Select(i => $"get_byte(uuid_send({column}),{i})")) + "]";
}
