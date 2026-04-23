using Microsoft.AspNetCore.Mvc;
using SwiftExport.Application.Interfaces;
using SwiftExport.Contracts.DTOs.Products;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.MasterData;
using SwiftExport.WebApi.Controllers.Base;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace SwiftExport.WebApi.Controllers.Products;

public class BomController(IBomService service) : BaseController<Bom, BomDto>(service)
{
    private readonly IBomService _bomService = service;

    [HttpGet("tree/{productSku}")]
    public async Task<ServiceResult<IEnumerable<BomTreeDto>>> GetBomTree(string productSku)
    {
        return await _bomService.GetBomTreeAsync(productSku);
    }

    [HttpGet("{bonNo}")]
    public async Task<ServiceResult<List<BomDto>>> GetBomByBomNo(string bonNo)
    {
        return await _bomService.GetBomByBomNoAsync(bonNo);
    }


    [HttpGet("validate/{productSku}")]
    public async Task<ServiceResult<bool>> ValidateBom(string productSku)
    {
        return await _bomService.ValidateBomAsync(productSku);
    }
}