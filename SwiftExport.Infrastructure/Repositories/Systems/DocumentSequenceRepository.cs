using Dapper;
using SwiftExport.Core.Entities.Systems;
using SwiftExport.Core.Interfaces;
using SwiftExport.Infrastructure.Contexts;
using SwiftExport.Infrastructure.Repositories.Base;

namespace SwiftExport.Infrastructure.Repositories.Systems
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

                                    UPDATE [DocumentSequence]
                                    SET [CurrentNo] = [CurrentNo] + 1,
                                        [UpdatedDate] = SYSUTCDATETIME(),
                                        [UpdatedBy] = @UpdatedBy
                                    OUTPUT INSERTED.CurrentNo INTO @newNo(CurrentNo)
                                    WHERE [DocType] = @DocType AND [YearMonth] = @YearMonth;

                                    IF @@ROWCOUNT = 0
                                    BEGIN
                                        INSERT INTO [DocumentSequence]
                                        (
                                            [DocType],
                                            [YearMonth],
                                            [CurrentNo],
                                            [IsDeleted],
                                            [CreatedDate],
                                            [CreatedBy],
                                            [UpdatedDate],
                                            [UpdatedBy],
                                            [Even],
                                            [Status]
                                        )
                                        VALUES
                                        (
                                            @DocType,
                                            @YearMonth,
                                            1,
                                            0,
                                            SYSUTCDATETIME(),
                                            @UpdatedBy,
                                            NULL,
                                            NULL,
                                            NULL,
                                            NULL
                                        );

                                        INSERT INTO @newNo(CurrentNo) VALUES (1);
                                    END

                                    SELECT TOP 1 CurrentNo FROM @newNo;";

            var currentNo = await Connection.QueryFirstAsync<int>(
                sql,
                new
                {
                    DocType = docType,
                    YearMonth = yearMonth,
                    UpdatedBy = updatedBy
                },
                transaction: Transaction);

            return new DocumentSequence
            {
                DocType = docType,
                YearMonth = yearMonth,
                CurrentNo = currentNo,
                UpdatedBy = updatedBy
            };
        }
    }
}
