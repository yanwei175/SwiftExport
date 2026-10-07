using System.ComponentModel.DataAnnotations.Schema;
using SwiftExport.Core.Common;
using SwiftExport.Core.DatabaseSchema;

namespace SwiftExport.Core.Entities.Commands;

/// <summary>保存已成功提交的业务命令原始结果，供相同命令身份安全重放。</summary>
[Table("command_receipt")]
[DbAppendOnly]
[DbCheck("CK_CommandReceipt_Identity", "\"command_id\" <> '00000000-0000-0000-0000-000000000000' AND \"actor_uid\" <> '00000000-0000-0000-0000-000000000000' AND \"is_deleted\" = FALSE")]
[DbCheck("CK_CommandReceipt_Versions", "\"fingerprint_version\" >= 1 AND \"result_version\" >= 1")]
[DbCheck("CK_CommandReceipt_Fingerprint", "length(rtrim(\"command_type\", ' ')) > 0 AND length(rtrim(\"request_fingerprint\", ' ')) = 64")]
[DbCheck("CK_CommandReceipt_ResultJson", "(\"result_json\" IS JSON OBJECT OR \"result_json\" IS JSON ARRAY)")]
[DbIndex("UX_CommandReceipt_CommandId", nameof(CommandId), IsUnique = true)]
public sealed class CommandReceipt : BaseEntity
{
    /// <summary>客户端提供的全库唯一业务命令身份。</summary>
    public Guid CommandId { get; set; }
    /// <summary>首次执行时由服务端认证得到的操作者。</summary>
    public Guid ActorUid { get; set; }
    /// <summary>服务端定义的稳定业务用例标识。</summary>
    [DbColumn(StoreType = "varchar(100)")]
    public string CommandType { get; set; } = "";
    /// <summary>请求明确指定的目标；创建命令允许为空。</summary>
    public Guid? RequestTargetUid { get; set; }
    /// <summary>请求指纹的规范化协议版本。</summary>
    public int FingerprintVersion { get; set; } = 1;
    /// <summary>规范化业务输入的 SHA-256 摘要。</summary>
    [DbColumn(StoreType = "varchar(64)")]
    public string RequestFingerprint { get; set; } = "";
    /// <summary>结果快照结构版本，用于阻止未知格式被重新执行。</summary>
    public int ResultVersion { get; set; } = 1;
    /// <summary>首次成功的业务结果 JSON。</summary>
    [DbColumn(StoreType = "text")]
    public string ResultJson { get; set; } = "";
    /// <summary>回执写入事务时的 UTC 时间，不代表精确提交时刻。</summary>
    public DateTime RecordedAtUtc { get; set; }
}
