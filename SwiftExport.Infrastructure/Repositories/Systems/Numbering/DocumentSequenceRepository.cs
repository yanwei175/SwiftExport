using SwiftExport.Core.Interfaces.Systems.Numbering;
using Dapper;
using Microsoft.Data.SqlClient;
using SwiftExport.Contracts.Common;
using SwiftExport.Core.Entities.Systems.Numbering;
using SwiftExport.Core.Interfaces;
using SwiftExport.Infrastructure.Contexts;
using SwiftExport.Infrastructure.Repositories.Base;

namespace SwiftExport.Infrastructure.Repositories.Systems.Numbering
{
    /// <summary>
    /// 单据流水号仓储实现。
    /// </summary>
    public class DocumentSequenceRepository(RepositoryBaseContext context)
        : DapperRepository<DocumentSequence>(context), IDocumentSequenceRepository
    {
        /// <summary>
        /// 原子获取并递增流水号。
        /// </summary>
        public async Task<DocumentSequence> GetNextAsync(string docType, string yearMonth, string? updatedBy)
        {
            const string sql = @"
                            DECLARE @newNo TABLE(CurrentNo INT);

                            UPDATE [DocumentSequence] WITH (UPDLOCK, HOLDLOCK)
                            SET 
                                [CurrentNo] = [CurrentNo] + 1,
                                [UpdatedDate] = SYSUTCDATETIME(),
                                [UpdatedBy] = @UpdatedBy
                            OUTPUT INSERTED.CurrentNo INTO @newNo(CurrentNo)
                            WHERE [DocType] = @DocType
                              AND [YearMonth] = @YearMonth
                              AND [IsDeleted] = 0;

                            IF NOT EXISTS (SELECT 1 FROM @newNo)
                            BEGIN
                                INSERT INTO [DocumentSequence]
                                (

                                    [Uid],
                                    [DocType],
                                    [YearMonth],
                                    [CurrentNo],
                                    [IsDeleted],
                                    [CreatedDate],
                                    [CreatedBy],
                                    [UpdatedDate],
                                    [UpdatedBy]
                                )
                                OUTPUT INSERTED.CurrentNo INTO @newNo(CurrentNo)
                                VALUES
                                (
                                    @Uid,
                                    @DocType,
                                    @YearMonth,
                                    1,
                                    0,
                                    SYSUTCDATETIME(),
                                    @UpdatedBy,
                                    SYSUTCDATETIME(),
                                    @UpdatedBy
                                );
                            END;

                            SELECT TOP 1 CurrentNo FROM @newNo;";
            try
            {
                var newUid = GuidSequentialGenerator.NewGuid();
                var currentNo = await Connection.QueryFirstAsync<int>(
                    sql,
                    new
                    {
                        Uid = newUid,
                        DocType = docType,
                        YearMonth = yearMonth,
                        UpdatedBy = updatedBy
                    },
                    transaction: Transaction);

                return new DocumentSequence
                {
                    Uid = newUid,
                    DocType = docType,
                    YearMonth = yearMonth,
                    CurrentNo = currentNo,
                    UpdatedBy = updatedBy
                };
            }
            catch (SqlException ex)
            {
                foreach (SqlError error in ex.Errors)
                {
                    Console.WriteLine($"SQL错误 {error.Number}: {error.Message}");
                }

                throw;
            }

        }
    }
}
