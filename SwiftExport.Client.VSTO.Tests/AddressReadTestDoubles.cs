using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SwiftExport.Client.VSTO.Services.ExcelImport;
using SwiftExport.Contracts.Apis.SystemApis;
using SwiftExport.Contracts.DTOs.SystemDtos.ExcelReadProfiles;
using SwiftExport.Contracts.Wrappers;
namespace SwiftExport.Client.VSTO.Testing
{
    /// <summary>可控地址 API，模拟版本冲突和迟到响应，不访问服务器。</summary>
    internal sealed class AddressProfileApi : IExcelReadProfileApi
    {
        internal ExcelReadProfileDto Profile = new ExcelReadProfileDto { Uid = Guid.NewGuid(), Version = 1, GroupCode = "SalesOrder", ConfigName = "模板", CanRead = true, EndMode = 2, SummaryCellAddress = "D30",
            Fields = new List<ExcelReadFieldDto> { new ExcelReadFieldDto { TargetFieldKey = "Header.InvoiceNo", CellAddress = "M12" },
                new ExcelReadFieldDto { TargetFieldKey = "Line.CustomerSku", CellAddress = "C18", IsDownward = true }, new ExcelReadFieldDto { TargetFieldKey = "Line.Qty", CellAddress = "J18", IsDownward = true } } };
        internal int Lists, Gets, Saves;
        internal bool FailSave = false;
        internal UpdateExcelReadProfileRequest LastRequest;
        internal Func<Task<ServiceResult<ExcelReadProfileDto>>> GetOverride = null;
        public Task<ServiceResult<IReadOnlyList<ExcelReadProfileListItemDto>>> ListAsync(string groupCode, CancellationToken token = default(CancellationToken))
        { Lists++; return Task.FromResult(ServiceResult<IReadOnlyList<ExcelReadProfileListItemDto>>.Ok(new[] { new ExcelReadProfileListItemDto { Uid = Profile.Uid, ConfigName = Profile.ConfigName, GroupCode = Profile.GroupCode, Version = Profile.Version } })); }
        public Task<ServiceResult<ExcelReadProfileDto>> GetAsync(Guid uid, CancellationToken token = default(CancellationToken))
        { Gets++; return GetOverride != null ? GetOverride() : Task.FromResult(ServiceResult<ExcelReadProfileDto>.Ok(Profile)); }
        public Task<ServiceResult<ExcelReadProfileDto>> CreateAsync(CreateExcelReadProfileRequest request, CancellationToken token = default(CancellationToken)) => Task.FromResult(ServiceResult<ExcelReadProfileDto>.Ok(Profile));
        public Task<ServiceResult<ExcelReadProfileDto>> UpdateAsync(Guid uid, UpdateExcelReadProfileRequest request, CancellationToken token = default(CancellationToken))
        {
            Saves++; LastRequest = request;
            if (FailSave) return Task.FromResult(ServiceResult<ExcelReadProfileDto>.Fail("版本冲突"));
            Profile.Fields = request.Fields; Profile.ConfigName = request.ConfigName; Profile.EndMode = request.EndMode; Profile.LastRowColumn = request.LastRowColumn; Profile.SummaryCellAddress = request.SummaryCellAddress; Profile.Version++;
            return Task.FromResult(ServiceResult<ExcelReadProfileDto>.Ok(Profile));
        }
        public Task<ServiceResult<bool>> DeleteAsync(Guid uid, int expectedVersion, CancellationToken token = default(CancellationToken)) => Task.FromResult(ServiceResult<bool>.Ok(true));
    }
    /// <summary>地址读取快照夹具，Office 场景另做实机验收。</summary>
    internal sealed class AddressReaderStub : IExcelAddressReader
    {
        internal ExcelCellSelection Selection = null;
        internal Func<bool, Task<ExcelAddressSnapshot>> Read = null;
        public Task<ExcelCellSelection> SelectCellAsync(CancellationToken token) => Task.FromResult(Selection);
        public Task<ExcelAddressSnapshot> ReadAsync(ExcelReadProfileDto profile, bool detail, CancellationToken token)
            => Read != null ? Read(detail) : Task.FromResult(new ExcelAddressSnapshot { Detail = detail, WorkbookName = "test.xlsx", WorksheetName = "Sheet1",
                Rows = new List<List<ExcelAddressValue>> { new List<ExcelAddressValue> { new ExcelAddressValue { FieldKey = detail ? "Line.CustomerSku" : "Header.InvoiceNo", Row = detail ? 18 : 12, Column = detail ? 3 : 13, Value = detail ? "AQ4101BG" : "260820" } } } });
    }
}
