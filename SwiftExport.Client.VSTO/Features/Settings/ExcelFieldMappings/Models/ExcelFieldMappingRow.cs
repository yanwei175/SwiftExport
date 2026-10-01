// 已淘汰：旧表头别名映射链路，仅保留历史兼容；新导入使用 ExcelReadProfile／ExcelReadField 地址配置。
using System;
using CommunityToolkit.Mvvm.ComponentModel;
using SwiftExport.Contracts.DTOs.SystemDtos.ExcelFieldMappings;

namespace SwiftExport.Client.VSTO.Features.Settings.ExcelFieldMappings.Models
{
    /// <summary>隔离字段映射的页面编辑状态，不直接修改缓存或传输 DTO。</summary>
    public sealed class ExcelFieldMappingRow : ObservableObject
    {
        private string excelHeader;
        private string targetFieldKey;
        private bool isEnabled;
        private string remark;

        /// <summary>由服务器分配的身份；空值代表尚未保存的新别名。</summary>
        public Guid Uid { get; private set; }
        /// <summary>已有记录的乐观并发版本。</summary>
        public int Version { get; private set; }
        public string ExcelHeader { get { return excelHeader; } set { SetProperty(ref excelHeader, value); } }
        /// <summary>保留稳定目标键；候选重建时的空回写不能抹掉历史配置。</summary>
        public string TargetFieldKey { get { return targetFieldKey; } set { if (value != null) SetProperty(ref targetFieldKey, value); } }
        public bool IsEnabled { get { return isEnabled; } set { SetProperty(ref isEnabled, value); } }
        public string Remark { get { return remark; } set { SetProperty(ref remark, value); } }

        /// <summary>将服务端快照复制到本地可编辑行。</summary>
        public static ExcelFieldMappingRow FromDto(ExcelFieldMappingDto dto) => new ExcelFieldMappingRow
        {
            Uid = dto.Uid, Version = dto.Version, ExcelHeader = dto.ExcelHeader,
            TargetFieldKey = dto.TargetFieldKey, IsEnabled = dto.IsEnabled, Remark = dto.Remark
        };

        /// <summary>构造待新增的别名行，身份和版本留给服务端。</summary>
        public static ExcelFieldMappingRow New(string fieldKey) => new ExcelFieldMappingRow
        {
            ExcelHeader = string.Empty, TargetFieldKey = fieldKey, IsEnabled = false
        };

        /// <summary>仅传递可维护字段及并发版本。</summary>
        public SaveExcelFieldMappingRowRequest ToRequest() => new SaveExcelFieldMappingRowRequest
        {
            Uid = Uid, ExpectedVersion = Version, ExcelHeader = ExcelHeader,
            TargetFieldKey = TargetFieldKey, IsEnabled = IsEnabled, Remark = Remark
        };
    }
}
