using System.Globalization;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using Npgsql;
using SwiftExport.Core.Common;
using SwiftExport.Infrastructure.Repositories.Base;

/// <summary>预检并补迁本次七张基础表；显式区分隔离验收和运行库同步，不搬历史或账号，不覆盖数据。</summary>
internal static class Program
{
    /// <summary>读取明确的源/目标环境连接；显式写入参数允许原子增量补迁，输出不含字段值或凭据。</summary>
    private static async Task<int> Main(string[] args)
    {
        var apply = args.Length == 2 && args[0] is "--apply-isolated" or "--apply-runtime";
        if (!apply && (args.Length != 1 || args[0] != "--preview"))
        {
            Console.Error.WriteLine("用法：--preview 或 --apply-isolated/--apply-runtime <相应库的已核对预检JSON>。");
            return 2;
        }
        var sourceString = Environment.GetEnvironmentVariable("SWIFTEXPORT_P05_SOURCE_CONNECTION")
            ?? throw new InvalidOperationException("缺少本批源连接环境变量。");
        var targetString = Environment.GetEnvironmentVariable("SWIFTEXPORT_P05_TARGET_CONNECTION")
            ?? throw new InvalidOperationException("缺少本批目标连接环境变量。");
        var sourceSettings = new SqlConnectionStringBuilder(sourceString);
        var targetSettings = new NpgsqlConnectionStringBuilder(targetString);
        // 防止误读备份库或错误实例；只允许用户已指定的源库和两个当前目标库。
        if (sourceSettings.DataSource is not ("192.168.3.12" or "192.168.3.12,1433")
            || !sourceSettings.InitialCatalog.Equals("dv_next", StringComparison.OrdinalIgnoreCase)
            || targetSettings.Host != "192.168.3.12" || targetSettings.Port != 5432
            || targetSettings.Database is not ("dv_next" or "dv_next_pg_failure_tests_20261004"))
            throw new InvalidOperationException("连接不属于本批指定实例和库；已停止预检。");
        if (apply && targetSettings.Database != (args[0] == "--apply-isolated" ? "dv_next_pg_failure_tests_20261004" : "dv_next"))
            throw new InvalidOperationException("写入模式和目标库不匹配，不能写其他库或备份。");

        using var source = new SqlConnection(sourceString);
        await using var target = new NpgsqlConnection(targetString);
        await source.OpenAsync();
        await target.OpenAsync();
        // 源未启用Snapshot。写入批次只在短时Serializable读事务中取得完整候选，释放源锁后才写PG。
        // 超时立即退出，不ALTER源库、不使用NOLOCK，也不把普通逐表预检称为一致快照。
        using (var settings = new SqlCommand("SET LOCK_TIMEOUT 5000", source)) await settings.ExecuteNonQueryAsync();
        using var readDeadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var sourceWatch = Stopwatch.StartNew();
        using var sourceTransaction = apply ? source.BeginTransaction(System.Data.IsolationLevel.Serializable) : null;
        var reports = new List<object>();
        var batches = new List<(EntityMetadata Map, List<object[]> Rows, string[] Types, string Hash)>();
        var totalProblems = 0;
        foreach (var (entityName, predicate) in Selections)
        {
            var entity = typeof(BaseEntity).Assembly.GetTypes().Single(t => t.Name == entityName);
            var sourceMap = EntityMetadataCache.GetMetadata(entity);
            var targetMap = PostgreSqlEntityMetadata.GetMetadata(entity);
            var targetColumns = new Dictionary<string, (bool Nullable, int? MaxLength, string Type)>(StringComparer.Ordinal);
            await using (var catalog = new NpgsqlCommand("""
                SELECT column_name, is_nullable='YES', character_maximum_length, udt_name
                FROM information_schema.columns WHERE table_schema='public' AND table_name=@table
                """, target))
            {
                catalog.Parameters.AddWithValue("table", targetMap.TableName);
                await using var reader = await catalog.ExecuteReaderAsync(readDeadline.Token);
                while (await reader.ReadAsync(readDeadline.Token))
                    targetColumns.Add(reader.GetString(0), (reader.GetBoolean(1), reader.IsDBNull(2) ? null : reader.GetInt32(2), reader.GetString(3)));
            }
            if (!targetColumns.Keys.Order().SequenceEqual(targetMap.Columns.Select(c => c.ColumnName).Order()))
                throw new InvalidOperationException($"{targetMap.TableName}实际列与实体映射不同，停止预检。");
            var rows = new List<object[]>();
            var uids = new HashSet<Guid>();
            var nullViolations = 0;
            var lengthViolations = 0;
            var emptyOrDuplicateUids = 0;
            var subMicrosecondValues = 0;
            using (var read = new SqlCommand($"SELECT {string.Join(',', sourceMap.Columns.Select(c => $"d.[{c.ColumnName}]"))} FROM [dbo].[{sourceMap.TableName}] d WHERE {predicate} ORDER BY d.Uid", source, sourceTransaction))
            {
                read.CommandTimeout = 30;
                using var reader = await read.ExecuteReaderAsync(readDeadline.Token);
                while (await reader.ReadAsync(readDeadline.Token))
                {
                    var row = new object[reader.FieldCount];
                    reader.GetValues(row);
                    if (rows.Count >= 100000) throw new InvalidOperationException("候选数量超过本批上限，停止预检。");
                    for (var i = 0; i < row.Length; i++)
                    {
                        var column = targetColumns[targetMap.Columns[i].ColumnName];
                        if (row[i] is DBNull && !column.Nullable) nullViolations++;
                        if (row[i] is string text && column.MaxLength is int max && text.EnumerateRunes().Count() > max) lengthViolations++;
                        if (row[i] is DateTime time && time.Ticks % 10 != 0) subMicrosecondValues++;
                    }
                    var uid = (Guid)row[sourceMap.Columns.ToList().FindIndex(c => c.PropertyName == "Uid")];
                    if (uid == Guid.Empty || !uids.Add(uid)) emptyOrDuplicateUids++;
                    rows.Add(row);
                }
            }
            await using var count = new NpgsqlCommand($"SELECT count(*) FROM public.\"{targetMap.TableName}\"", target);
            var targetRows = Convert.ToInt64(await count.ExecuteScalarAsync(readDeadline.Token), CultureInfo.InvariantCulture);
            var uidText = string.Join('\n', uids.Select(u => u.ToString("D")).Order(StringComparer.Ordinal));
            var fieldHash = FieldHash(rows, sourceMap);
            batches.Add((targetMap, rows, targetMap.Columns.Select(c => targetColumns[c.ColumnName].Type).ToArray(), fieldHash));
            totalProblems += nullViolations + lengthViolations + emptyOrDuplicateUids;
            reports.Add(new
            {
                sourceTable = sourceMap.TableName, targetTable = targetMap.TableName, columns = rows.Count == 0 ? targetMap.Columns.Count : rows[0].Length,
                sourceRows = rows.Count, targetRows, uidMd5 = Convert.ToHexString(MD5.HashData(Encoding.UTF8.GetBytes(uidText))).ToLowerInvariant(),
                normalizedFieldSha256 = fieldHash,
                nullViolations, lengthViolations, emptyOrDuplicateUids, subMicrosecondValues
            });
        }
        readDeadline.Token.ThrowIfCancellationRequested();
        sourceTransaction?.Commit();
        sourceWatch.Stop();
        if (apply)
        {
            if (totalProblems != 0) throw new InvalidOperationException("预检存在字段违规；没有目标写入。");
            using var expected = JsonDocument.Parse(await File.ReadAllTextAsync(args[1]));
            if (expected.RootElement.GetProperty("mode").GetString() != "preview"
                || expected.RootElement.GetProperty("targetDatabase").GetString() != targetSettings.Database
                || expected.RootElement.GetProperty("totalProblems").GetInt32() != 0)
                throw new InvalidOperationException("必须提供指定目标库的零违规预检摘要。");
            var expectedTables = expected.RootElement.GetProperty("tables").EnumerateArray().ToArray();
            if (expectedTables.Length != batches.Count) throw new InvalidOperationException("预检文件不是本批七表摘要。");
            foreach (var batch in batches)
            {
                var previous = expectedTables.Single(t => t.GetProperty("targetTable").GetString() == batch.Map.TableName);
                if (previous.GetProperty("sourceRows").GetInt32() != batch.Rows.Count
                    || previous.GetProperty("normalizedFieldSha256").GetString() != batch.Hash)
                    throw new InvalidOperationException($"{batch.Map.TableName}源候选已变化；停止写入，重新预检。");
            }
            await ApplyEmptyTables(target, batches);
        }
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            mode = apply ? args[0][2..] : "preview", targetDatabase = targetSettings.Database,
            insertedRows = apply ? batches.Sum(b => b.Rows.Count) : 0,
            sourceReadDurationMs = sourceWatch.ElapsedMilliseconds,
            consistentSourceSnapshot = apply, readIsolation = apply ? "短时Serializable源读取已提交，PG写入前已释放源锁" : "各表READ COMMITTED；此预检不作为补迁的稳定源快照",
            timestampNormalization = "字段摘要仅将日期截断到PG微秒精度，源字段不改；精度损失数量单列",
            tables = reports, totalProblems
        }, new JsonSerializerOptions { WriteIndented = true }));
        return totalProblems == 0 ? 0 : 1;
    }

    /// <summary>仅向七张当前空表插入并逐字段读回；同一事务内锁住写入边界，任何不一致均回滚。</summary>
    private static async Task ApplyEmptyTables(NpgsqlConnection target,
        List<(EntityMetadata Map, List<object[]> Rows, string[] Types, string Hash)> batches)
    {
        await using var transaction = await target.BeginTransactionAsync();
        await using (var guard = new NpgsqlCommand($"SET LOCAL lock_timeout='5s'; SET LOCAL statement_timeout='30s'; LOCK TABLE {string.Join(',', batches.Select(b => $"public.\"{b.Map.TableName}\""))} IN SHARE ROW EXCLUSIVE MODE", target, transaction))
            await guard.ExecuteNonQueryAsync();
        foreach (var batch in batches)
        {
            await using var count = new NpgsqlCommand($"SELECT count(*) FROM public.\"{batch.Map.TableName}\"", target, transaction);
            if (Convert.ToInt64(await count.ExecuteScalarAsync(), CultureInfo.InvariantCulture) != 0)
                throw new InvalidOperationException($"{batch.Map.TableName}不再为空；停止整批，绝不覆盖或清理。");
        }
        foreach (var batch in batches)
        {
            await using (var copy = await target.BeginBinaryImportAsync($"COPY public.\"{batch.Map.TableName}\" ({string.Join(',', batch.Map.Columns.Select(c => $"\"{c.ColumnName}\""))}) FROM STDIN (FORMAT BINARY)"))
            {
                foreach (var row in batch.Rows)
                {
                    await copy.StartRowAsync();
                    for (var i = 0; i < row.Length; i++)
                    {
                        if (row[i] is DBNull) await copy.WriteNullAsync();
                        else await copy.WriteAsync(row[i], batch.Types[i]);
                    }
                }
                await copy.CompleteAsync();
            }
            var actual = new List<object[]>();
            await using var read = new NpgsqlCommand($"SELECT {string.Join(',', batch.Map.Columns.Select(c => $"\"{c.ColumnName}\""))} FROM public.\"{batch.Map.TableName}\"", target, transaction);
            await using var reader = await read.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var row = new object[reader.FieldCount];
                reader.GetValues(row);
                actual.Add(row);
            }
            if (actual.Count != batch.Rows.Count || FieldHash(actual, batch.Map) != batch.Hash)
                throw new InvalidOperationException($"{batch.Map.TableName}逐字段读回不一致；整批回滚。");
        }
        await transaction.CommitAsync();
    }

    /// <summary>按UID确定行顺序，对所有持久化字段生成一致摘要，避免只核数量漏掉字段错位。</summary>
    private static string FieldHash(List<object[]> rows, EntityMetadata map)
    {
        var uidIndex = map.Columns.ToList().FindIndex(c => c.PropertyName == "Uid");
        var text = string.Join('\n', rows.OrderBy(r => (Guid)r[uidIndex]).Select(r => JsonSerializer.Serialize(r.Select(CanonicalValue))));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    }

    /// <summary>构造逐字段对照值；保留NULL、字符串、GUID及数值，日期摘要统一为目标微秒精度。</summary>
    private static string? CanonicalValue(object value) => value switch
    {
        DBNull => null,
        DateTime time => new DateTime(time.Ticks - time.Ticks % 10, DateTimeKind.Unspecified).ToString("yyyy-MM-ddTHH:mm:ss.ffffff", CultureInfo.InvariantCulture),
        Guid uid => uid.ToString("D"),
        decimal number => number.ToString("G29", CultureInfo.InvariantCulture),
        bool boolean => boolean ? "true" : "false",
        IFormattable formatted => formatted.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString()
    };

    // 固定本批有效基础数据边界；六条无产品关系的资料留在源库，不猜测或补写。
    private static readonly (string Entity, string Predicate)[] Selections =
    [
        ("ProductDetail", "d.IsDeleted=0 AND EXISTS(SELECT 1 FROM dbo.Product p WHERE p.Uid=d.ProductId AND p.IsDeleted=0 AND p.IsActive=1)"),
        ("CustomerProduct", "d.IsDeleted=0 AND d.IsActive=1 AND EXISTS(SELECT 1 FROM dbo.Customer c WHERE c.Uid=d.CustomerId AND c.IsDeleted=0 AND c.IsActive=1) AND(d.SkuId IS NULL OR EXISTS(SELECT 1 FROM dbo.Product p WHERE p.Uid=d.SkuId AND p.IsDeleted=0 AND p.IsActive=1))"),
        ("CustomerPoConfig", "d.IsDeleted=0 AND(d.CustomerId IS NULL OR EXISTS(SELECT 1 FROM dbo.Customer c WHERE c.Uid=d.CustomerId AND c.IsDeleted=0 AND c.IsActive=1)) AND(d.SupplierId IS NULL OR EXISTS(SELECT 1 FROM dbo.Supplier s WHERE s.Uid=d.SupplierId AND s.IsDeleted=0 AND s.IsActive=1))"),
        ("Bom", "d.IsDeleted=0 AND EXISTS(SELECT 1 FROM dbo.Product p WHERE p.Uid=d.ParentProductId AND p.IsDeleted=0 AND p.IsActive=1) AND EXISTS(SELECT 1 FROM dbo.Product p WHERE p.Uid=d.ComponentProductId AND p.IsDeleted=0 AND p.IsActive=1)"),
        ("ProductNeutral", "d.IsDeleted=0 AND EXISTS(SELECT 1 FROM dbo.ProductMaster m WHERE m.Uid=d.ProductMasterId AND m.IsDeleted=0) AND EXISTS(SELECT 1 FROM dbo.ProductColor c WHERE c.Uid=d.ColorId AND c.IsDeleted=0)"),
        ("ExcelReadProfile", "d.IsDeleted=0"),
        ("ExcelReadField", "d.IsDeleted=0 AND EXISTS(SELECT 1 FROM dbo.ExcelReadProfile p WHERE p.Uid=d.ProfileUid AND p.IsDeleted=0)")
    ];
}
