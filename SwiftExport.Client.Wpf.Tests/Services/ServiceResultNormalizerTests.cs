using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Contracts.DTOs.Errors;
using SwiftExport.Contracts.Wrappers;
using SwiftExportClient.Services.Api;

namespace SwiftExport.Client.Wpf.Tests.Services;

/// <summary>验证通用服务结果归一为稳定的客户端语义。</summary>
[TestClass]
public sealed class ServiceResultNormalizerTests
{
    /// <summary>成功包装保留数据、消息和成功状态。</summary>
    [TestMethod]
    public void Success_result_keeps_client_shape()
    {
        var result = ServiceResult<int>.Ok(7, "完成").ToClientResult();

        Assert.IsTrue(result.Success);
        Assert.AreEqual(7, result.Data);
        Assert.AreEqual("完成", result.Message);
    }

    /// <summary>结构化失败保留稳定错误代码、字段和展示消息。</summary>
    [TestMethod]
    public void Structured_failure_keeps_error_contract()
    {
        var result = ServiceResult<int>.Fail(
            ServiceErrorCodes.VersionConflict, "版本冲突。", "ExpectedVersion").ToClientResult();

        Assert.IsFalse(result.Success);
        Assert.AreEqual("版本冲突。", result.Message);
        Assert.AreEqual(ServiceErrorCodes.VersionConflict, result.ErrorCode);
        Assert.AreEqual("ExpectedVersion", result.ErrorField);
    }

    /// <summary>单消息业务阻断保留稳定类别与顶层消息。</summary>
    [TestMethod]
    public void Business_failure_keeps_code_and_message()
    {
        var result = ServiceResult<int>.Fail("业务条件不满足。").ToClientResult();

        Assert.IsFalse(result.Success);
        Assert.AreEqual("业务条件不满足。", result.Message);
        Assert.AreEqual(ServiceErrorCodes.BusinessBlocked, result.ErrorCode);
        Assert.IsNull(result.ErrorField);
    }
}
