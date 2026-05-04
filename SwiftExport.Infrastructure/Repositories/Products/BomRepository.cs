using Dapper;
using SwiftExport.Contracts.DTOs.Products;
using SwiftExport.Core.Entities.MasterData;
using SwiftExport.Core.Interfaces;
using SwiftExport.Infrastructure.Contexts;
using SwiftExport.Infrastructure.Repositories.Base;
using System.Data;

namespace SwiftExport.Infrastructure.Repositories.Products;

public class BomRepository(RepositoryBaseContext context) : DapperRepository<Bom>(context), IBomRepository
{

    public async Task<IEnumerable<BomDto>> GetBomDtoByBomNoAsync(string bomNo)
    {
        var sql = @"SELECT 
                        t1.*, 
                        t2.DescriptionZh, 
                        t3.ColorNameZh as Finish,
                        t2.FeaturesZh,
                        t2.MainImage
                    FROM Bom t1
                    LEFT JOIN Product t2 ON t1.SubSkuId = t2.Id
                    LEFT JOIN ProductColor t3 ON t2.ColorId = t3.Id
                    WHERE t1.BomNo = @BomNo";

        return await Connection.QueryAsync<BomDto>(sql, new { BomNo = bomNo });
    }


    public async Task<IEnumerable<Bom>> GetBomByBomNoAsync(string bomNo)
    {
        var sql = $"SELECT * FROM  [{TableName}] Where BomNo = @BomNo";
        return await Connection.QueryAsync<Bom>(sql, new { BomNo = bomNo });
    }

    public async Task<Dictionary<string, IEnumerable<Bom>>> Get一堆BomBy一堆Bom号(IEnumerable<string> bomNos)
    {
        if (bomNos == null || !bomNos.Any()) return new Dictionary<string, IEnumerable<Bom>>();

        // 【核心优化】使用 WHERE IN 进行单次查询
        var sql = $"SELECT * FROM [{TableName}] WHERE BomNo IN @BomNos";

        // Dapper 会自动将 IEnumerable<string> BomNos 转换为 SQL 参数列表

        var allBoms = await Connection.QueryAsync<Bom>(
                                 sql,
                                 new { BomNos = bomNos });
        // 使用 LINQ 将结果分组并转换为字典
        return allBoms.GroupBy(b => b.BomNo)
                      .ToDictionary(g => g.Key, g => g.AsEnumerable());

    }

    public async Task<int> DeleteBomByBomNoAsync(string bomNo)
    {
        var sql = $"DELETE FROM [{TableName}] WHERE BomNo = @BomNo";
        // 使用基类已有的字段
        return await Connection.ExecuteAsync(sql, new { BomNo = bomNo }, transaction: Transaction);
    }

    public async Task<bool> IsBomItemExistAsync(string bomNo, string productSku)
    {
        return await Connection.ExecuteScalarAsync<bool>(
            $"SELECT COUNT(1) FROM [{TableName}] WHERE BomNo = @BomNo AND SubSku = @SubSku",
            new { BomNo = bomNo, SubSku = productSku },
            transaction: Transaction
        );
    }



    //public async Task<int> SyncBomItemsAsync(IEnumerable<Bom> items)
    //{
    //   var dt= await _fac.CreatDtByEntityPropertysAsync(items);
    //            return await Connection.ExecuteAsync(
    //                "SyncBoms",
    //                new { Boms = dt.AsTableValuedParameter("BomType") },
    //                transaction: Transaction,
    //                commandType: CommandType.StoredProcedure
    //            );
    //}


}
