using Dapper;
using System.Data;
using SwiftExport.Core.Common;
using SwiftExport.Core.Entities.QualityControl;
using SwiftExport.Core.Interfaces.QualityControl;
using SwiftExport.Contracts.DTOs.QualityControl.AfterSales;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Infrastructure.Contexts;

using SwiftExport.Core.Interfaces.QualityControl.AfterSales.Document;
namespace SwiftExport.Infrastructure.Repositories.QualityControl.AfterSales.Document;

/// <summary>在同一请求事务内读取并写入售后头行及原命令锁。</summary>
public sealed class AfterSalesDocumentRepository(RepositoryBaseContext context) : IAfterSalesDocumentRepository
{
    private IDbConnection Connection => context.Session.Connection;
    private IDbTransaction? Transaction => context.Session.Transaction;
    private static readonly string[] HeaderColumns = ["Uid","Version","IsDeleted","CreatedDate","CreatedBy","UpdatedDate","UpdatedBy","AfterSalesNo","CustomerId","CustomerCodeSnapshot","CustomerNameSnapshot","AcceptedDate","OwnerId","DocumentStatus","ConfirmationStatus","DemandConfirmedAt","DemandConfirmedBy","ExpectedCompletionDate","Conclusion","Remark","OriginalSalesOrderLineId","CustomerComplaintNo","LocalFileFolder","ResponsibleSupplierId","IssueProductId","IssueSkuSnapshot","IssueCustomerSkuSnapshot","IssueLinkSkuSnapshot","IssueDescription","QualityCategoryId","CauseAnalysis","RequiredDate","ServiceAmountCny","ReturnedReceivedDate","TreatmentDescription"];
    private static readonly string[] LineColumns = ["Uid","Version","IsDeleted","CreatedDate","CreatedBy","UpdatedDate","UpdatedBy","HeaderId","LineNumber","ProductId","IsNonStandard","SkuSnapshot","CustomerSkuSnapshot","LinkSkuSnapshot","DescriptionSnapshot","ColorId","ColorSnapshot","SpecificationSnapshot","Quantity","UnitSnapshot","Remark"];

    /// <inheritdoc />
    public Task<AfterSalesHeader?> GetHeaderAsync(Guid uid, bool forUpdate = false)
        => Connection.QuerySingleOrDefaultAsync<AfterSalesHeader>($"SELECT {Columns(HeaderColumns)} FROM dbo.AfterSalesHeader {(forUpdate ? "WITH(UPDLOCK,HOLDLOCK)" : "")} WHERE Uid=@uid AND IsDeleted=0", new { uid }, Transaction);
    /// <inheritdoc />
    public async Task<IReadOnlyList<AfterSalesLine>> GetLinesAsync(Guid uid, bool forUpdate = false)
        => (await Connection.QueryAsync<AfterSalesLine>($"SELECT {Columns(LineColumns)} FROM dbo.AfterSalesLine {(forUpdate ? "WITH(UPDLOCK,HOLDLOCK)" : "")} WHERE HeaderId=@uid ORDER BY LineNumber,Uid", new { uid }, Transaction)).ToList();
    /// <inheritdoc />
    public Task<bool> WriteHeaderAsync(AfterSalesHeader head, bool insert) => WriteAsync("AfterSalesHeader", HeaderColumns, head, insert);
    /// <inheritdoc />
    public Task<bool> WriteLineAsync(AfterSalesLine line, bool insert) => WriteAsync("AfterSalesLine", LineColumns, line, insert);

    // 列名来自本类固定清单，不能由客户端输入；新增不调用会重写 Uid 的泛型 AddAsync。
    private async Task<bool> WriteAsync(string table, string[] columns, BaseEntity entity, bool insert)
    {
        if (Transaction is null) throw new InvalidOperationException("售后写入必须处于业务事务。");
        string sql;
        if (insert)
        {
            context.AuditStamp.StampForInsert(entity);
            entity.Version = 1;
            sql = $"INSERT dbo.[{table}] ({Columns(columns)}) VALUES ({string.Join(",", columns.Select(c => "@" + c))})";
        }
        else
        {
            context.AuditStamp.StampForUpdate(entity);
            var writable = columns.Except(["Uid","Version","CreatedDate","CreatedBy"]);
            sql = $"UPDATE dbo.[{table}] SET {string.Join(",", writable.Select(c => $"[{c}]=@{c}"))},Version=Version+1 WHERE Uid=@Uid AND Version=@Version";
        }
        var count = await Connection.ExecuteAsync(sql, entity, Transaction);
        if (count == 1 && !insert) entity.Version++;
        return count == 1;
    }
    private static string Columns(IEnumerable<string> columns)=>string.Join(",",columns.Select(c=>"["+c+"]"));
}
