using Microsoft.VisualStudio.TestTools.UnitTesting;
using Npgsql;
using SwiftExport.Core.Common;
using SwiftExport.Core.Interfaces.Data;
using SwiftExport.Infrastructure.Data;

namespace SwiftExport.Application.Tests.Database;

/// <summary>只验证专用历史信号和现有异常分类，不访问数据库或凭消息猜测业务类别。</summary>
[TestClass]
public sealed class PostgreSqlInboundHistoryMigrationTests
{
    /// <summary>明确的历史门禁信号保留既有UnverifiedReceiptHistory类别和源说明。</summary>
    [TestMethod]
    public void HistorySignal_UsesExistingFailureKind()
    {
        var signal = new UnverifiedReceiptHistoryException();
        Assert.AreEqual(DatabaseFailureKind.UnverifiedReceiptHistory, PostgreSqlExceptionClassifier.Instance.Classify(signal));
        Assert.AreEqual("入库反向历史不完整或存在旧负数反向单，净收货量尚未核实。", signal.Message);
    }
    /// <summary>普通异常即使复用历史消息，也不能冒充已完成SQL历史检查。</summary>
    [TestMethod]
    public void MatchingMessage_StaysUnknown()
        => Assert.AreEqual(DatabaseFailureKind.Unknown, PostgreSqlExceptionClassifier.Instance.Classify(new InvalidOperationException(new UnverifiedReceiptHistoryException().Message)));
    /// <summary>任意RAISE信号P0001不转换为历史门禁，不影响既有PG失败分类。</summary>
    [TestMethod]
    public void ArbitraryRaise_StaysUnknown()
        => Assert.AreEqual(DatabaseFailureKind.Unknown, PostgreSqlExceptionClassifier.Instance.Classify(new PostgresException(new UnverifiedReceiptHistoryException().Message, "ERROR", "ERROR", "P0001")));
}
