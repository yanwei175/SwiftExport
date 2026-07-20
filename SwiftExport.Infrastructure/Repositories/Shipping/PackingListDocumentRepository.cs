using Dapper;
using SwiftExport.Contracts.SysEnums.Shipping;
using SwiftExport.Core.Entities.Shipping;
using SwiftExport.Core.Interfaces.Shipping;
using SwiftExport.Infrastructure.Contexts;

namespace SwiftExport.Infrastructure.Repositories.Shipping;

public sealed class PackingListDocumentRepository(RepositoryBaseContext context) : IPackingListDocumentRepository
{
    private const string HeadColumns = "Uid,Version,IsDeleted,CreatedDate,CreatedBy,UpdatedDate,UpdatedBy,PackingListNo,CustomerId,CustomerCodeSnapshot,CustomerNameSnapshot,PackingDate,ShipmentDate,Etd,Eta,InvoiceNo,ContainerNo,SealNo,ShippingMark,DeliveryAddressSnapshot,LifecycleStatus,PostingStatus,PostedDate,PostedBy,ReversedDate,ReversedBy,ReverseReason,CreateCommandId,CreateRequestFingerprint,PostCommandId,PostPreviewToken,ReverseCommandId,ReversePreviewToken";

    public Task<PackingListDocument?> GetForUpdateAsync(Guid uid)
        => context.Session.Connection.QuerySingleOrDefaultAsync<PackingListDocument>($"SELECT {HeadColumns} FROM dbo.PackingListDocument WITH(UPDLOCK,HOLDLOCK) WHERE Uid=@Uid AND ISNULL(IsDeleted,0)=0;", new { Uid = uid }, context.Session.Transaction);

    public async Task<IReadOnlyList<PackingListContributionLine>> GetLinesForUpdateAsync(Guid uid)
        => (await context.Session.Connection.QueryAsync<PackingListContributionLine>("""
            SELECT * FROM dbo.PackingListContributionLine WITH(UPDLOCK,HOLDLOCK)
            WHERE PackingListDocumentId=@Uid AND ISNULL(IsDeleted,0)=0 ORDER BY LineNumber,Uid;
            """, new { Uid = uid }, context.Session.Transaction)).ToList();

    public Task<PackingListDocument?> GetByCreateCommandForUpdateAsync(Guid commandId) => ByCommandAsync("CreateCommandId", commandId);
    public Task<PackingListDocument?> GetByPostCommandForUpdateAsync(Guid commandId) => ByCommandAsync("PostCommandId", commandId);
    public Task<PackingListDocument?> GetByReverseCommandForUpdateAsync(Guid commandId) => ByCommandAsync("ReverseCommandId", commandId);

    public async Task<int> InsertAsync(PackingListDocument document, IReadOnlyList<PackingListContributionLine> lines)
    {
        context.AuditStamp.StampForInsert(document);
        var inserted = await context.Session.Connection.ExecuteAsync("""
            INSERT dbo.PackingListDocument(Uid,Version,IsDeleted,CreatedDate,CreatedBy,UpdatedDate,UpdatedBy,PackingListNo,CustomerId,CustomerCodeSnapshot,CustomerNameSnapshot,
              PackingDate,ShipmentDate,Etd,Eta,InvoiceNo,ContainerNo,SealNo,ShippingMark,DeliveryAddressSnapshot,LifecycleStatus,PostingStatus,CreateCommandId,CreateRequestFingerprint)
            VALUES(@Uid,@Version,@IsDeleted,@CreatedDate,@CreatedBy,@UpdatedDate,@UpdatedBy,@PackingListNo,@CustomerId,@CustomerCodeSnapshot,@CustomerNameSnapshot,
              @PackingDate,@ShipmentDate,@Etd,@Eta,@InvoiceNo,@ContainerNo,@SealNo,@ShippingMark,@DeliveryAddressSnapshot,@LifecycleStatus,@PostingStatus,@CreateCommandId,@CreateRequestFingerprint);
            """, document, context.Session.Transaction);
        foreach (var line in lines)
        {
            line.PackingListDocumentId = document.Uid;
            context.AuditStamp.StampForInsert(line);
        }
        return inserted + await InsertLinesAsync(lines);
    }

    public async Task<int> UpdateDraftAsync(PackingListDocument document, IReadOnlyList<PackingListContributionLine> lines, int expectedVersion)
    {
        var affected = await context.Session.Connection.ExecuteAsync("""
            UPDATE dbo.PackingListDocument SET PackingListNo=@PackingListNo,CustomerId=@CustomerId,CustomerCodeSnapshot=@CustomerCodeSnapshot,CustomerNameSnapshot=@CustomerNameSnapshot,
              PackingDate=@PackingDate,ShipmentDate=@ShipmentDate,Etd=@Etd,Eta=@Eta,InvoiceNo=@InvoiceNo,ContainerNo=@ContainerNo,SealNo=@SealNo,ShippingMark=@ShippingMark,
              DeliveryAddressSnapshot=@DeliveryAddressSnapshot,Version=Version+1,UpdatedDate=@Now,UpdatedBy=@UserName
            WHERE Uid=@Uid AND Version=@ExpectedVersion AND LifecycleStatus=@Active AND PostingStatus=@Unposted AND ISNULL(IsDeleted,0)=0;
            """, new { document.Uid, document.PackingListNo, document.CustomerId, document.CustomerCodeSnapshot, document.CustomerNameSnapshot, document.PackingDate,
                document.ShipmentDate, document.Etd, document.Eta, document.InvoiceNo, document.ContainerNo, document.SealNo, document.ShippingMark,
                document.DeliveryAddressSnapshot, ExpectedVersion = expectedVersion, Active = PackingListLifecycleStatusEnum.Active,
                Unposted = PackingListPostingStatusEnum.Unposted, Now = DateTime.Now, UserName = context.CurrentUser.UserName }, context.Session.Transaction);
        if (affected != 1) return affected;
        await context.Session.Connection.ExecuteAsync("DELETE FROM dbo.PackingListContributionLine WHERE PackingListDocumentId=@Uid;", new { document.Uid }, context.Session.Transaction);
        foreach (var line in lines)
        {
            line.PackingListDocumentId = document.Uid;
            context.AuditStamp.StampForInsert(line);
        }
        return affected + await InsertLinesAsync(lines);
    }

    public Task<int> SetVoidedAsync(Guid uid, int expectedVersion, string userName) => context.Session.Connection.ExecuteAsync("""
        UPDATE dbo.PackingListDocument SET LifecycleStatus=9,Version=Version+1,UpdatedDate=@Now,UpdatedBy=@UserName
        WHERE Uid=@Uid AND Version=@ExpectedVersion AND LifecycleStatus=0 AND PostingStatus=0 AND ISNULL(IsDeleted,0)=0;
        """, new { Uid = uid, ExpectedVersion = expectedVersion, Now = DateTime.Now, UserName = userName }, context.Session.Transaction);

    public Task<int> SetPostedAsync(Guid uid, int expectedVersion, Guid commandId, string previewToken, DateTime date, string userName) => context.Session.Connection.ExecuteAsync("""
        UPDATE dbo.PackingListDocument SET PostingStatus=1,PostedDate=@Date,PostedBy=@UserName,PostCommandId=@CommandId,PostPreviewToken=@PreviewToken,Version=Version+1,UpdatedDate=@Date,UpdatedBy=@UserName
        WHERE Uid=@Uid AND Version=@ExpectedVersion AND LifecycleStatus=0 AND PostingStatus=0 AND ISNULL(IsDeleted,0)=0;
        """, new { Uid = uid, ExpectedVersion = expectedVersion, CommandId = commandId, PreviewToken = previewToken, Date = date, UserName = userName }, context.Session.Transaction);

    public Task<int> SetReversedAsync(Guid uid, int expectedVersion, Guid commandId, string previewToken, DateTime date, string userName, string reason) => context.Session.Connection.ExecuteAsync("""
        UPDATE dbo.PackingListDocument SET PostingStatus=8,ReversedDate=@Date,ReversedBy=@UserName,ReverseReason=@Reason,ReverseCommandId=@CommandId,ReversePreviewToken=@PreviewToken,
          Version=Version+1,UpdatedDate=@Date,UpdatedBy=@UserName WHERE Uid=@Uid AND Version=@ExpectedVersion AND PostingStatus=1 AND ISNULL(IsDeleted,0)=0;
        """, new { Uid = uid, ExpectedVersion = expectedVersion, CommandId = commandId, PreviewToken = previewToken, Date = date, UserName = userName, Reason = reason }, context.Session.Transaction);

    private Task<PackingListDocument?> ByCommandAsync(string column, Guid commandId)
        => context.Session.Connection.QuerySingleOrDefaultAsync<PackingListDocument>($"SELECT TOP(1) {HeadColumns} FROM dbo.PackingListDocument WITH(UPDLOCK,HOLDLOCK) WHERE {column}=@CommandId AND ISNULL(IsDeleted,0)=0;", new { CommandId = commandId }, context.Session.Transaction);

    private Task<int> InsertLinesAsync(IReadOnlyList<PackingListContributionLine> lines) => context.Session.Connection.ExecuteAsync("""
        INSERT dbo.PackingListContributionLine(Uid,Version,IsDeleted,CreatedDate,CreatedBy,UpdatedDate,UpdatedBy,PackingListDocumentId,LineNumber,ProductId,SkuSnapshot,CustomerSkuSnapshot,
          DescriptionSnapshot,UnitSnapshot,Quantity,PackageCount,NetWeight,GrossWeight,Volume,Marks,Note,GrossDemandId,NetDemandId,NetDemandAllocationId,DemandExecutionLinkId,
          SalesOrderId,SalesOrderLineId,SalesOrderNoSnapshot,SalesOrderLineNumberSnapshot,SourceSupplierId,SourceSupplierNameSnapshot,WarehouseSiteId,WarehouseCodeSnapshot,WarehouseNameSnapshot,
          SnapshotRequiredDate,SupplyType,CommandItemId)
        VALUES(@Uid,@Version,@IsDeleted,@CreatedDate,@CreatedBy,@UpdatedDate,@UpdatedBy,@PackingListDocumentId,@LineNumber,@ProductId,@SkuSnapshot,
          @CustomerSkuSnapshot,@DescriptionSnapshot,@UnitSnapshot,@Quantity,@PackageCount,@NetWeight,@GrossWeight,@Volume,@Marks,@Note,@GrossDemandId,@NetDemandId,
          @NetDemandAllocationId,@DemandExecutionLinkId,@SalesOrderId,@SalesOrderLineId,@SalesOrderNoSnapshot,@SalesOrderLineNumberSnapshot,@SourceSupplierId,
          @SourceSupplierNameSnapshot,@WarehouseSiteId,@WarehouseCodeSnapshot,@WarehouseNameSnapshot,@SnapshotRequiredDate,@SupplyType,@CommandItemId);
        """, lines, context.Session.Transaction);
}
