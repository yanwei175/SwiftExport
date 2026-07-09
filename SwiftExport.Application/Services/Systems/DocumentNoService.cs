using SwiftExport.Application.Interfaces;
using SwiftExport.Contracts.DTOs.SystemDtos.Requests;
using SwiftExport.Contracts.DTOs.SystemDtos.Responses;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Interfaces;

namespace SwiftExport.Application.Services.Systems
{
    /// <summary>
    /// 单据编号服务实现。
    /// </summary>
    public class DocumentNoService(IDocumentSequenceRepository documentSequenceRepository) : IDocumentNoService
    {
        private readonly IDocumentSequenceRepository _documentSequenceRepository = documentSequenceRepository;

        /// <summary>
        /// 获取下一个可用单号。
        /// 规则：{DocType}{yyyyMM}{序号补零}。
        /// </summary> 
        public async Task<ServiceResult<DocumentNoResponse>> GetNextDocumentNoAsync(DocumentNoRequest request)
        {
            request ??= new DocumentNoRequest();

            var docType = (request.DocType ?? string.Empty).Trim().ToUpperInvariant();
            if (string.IsNullOrWhiteSpace(docType))
                return ServiceResult<DocumentNoResponse>.Fail("DocType 不能为空");

            var serialWidth = request.SerialWidth > 0 ? request.SerialWidth : 4;
            var businessDate = request.BusinessDate ?? DateTime.Now;
            var yearMonth = businessDate.ToString("yyyyMM");

            var sequence = await _documentSequenceRepository.GetNextAsync(docType, yearMonth, request.Operator);
            var documentNo = $"{docType}{yearMonth}{sequence.CurrentNo.ToString().PadLeft(serialWidth, '0')}";

            return ServiceResult<DocumentNoResponse>.Ok(new DocumentNoResponse
            {
                DocType = docType,
                YearMonth = yearMonth,
                CurrentNo = sequence.CurrentNo,
                DocumentNo = documentNo
            });
        }
    }
}
