using Microsoft.AspNetCore.Mvc;
using SwiftExport.Application.Interfaces;
using SwiftExport.Contracts.DTOs.Product;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.WebApi.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ProductController(IProductService service) : ControllerBase
{

    private readonly IProductService _service = service;

    // GET: api/Product
    [HttpGet]
    public async Task<ServiceResult<IReadOnlyList<ProductDto>>> GetAll()
    {
        return await _service.GetAllAsync();
    }

    // GET: api/Product/5
    [HttpGet("{id}")]
    public async Task<ServiceResult<ProductDto>> GetById(int id)
    {
        return await _service.GetByIdAsync(id);
    }

    // GET: api/Product/sku/A001
    [HttpGet("sku/{sku}")]
    public async Task<ServiceResult<ProductDto>> GetBySku(string sku)
    {
        return await _service.GetBySkuAsync(sku);
    }

    // POST: api/Product
    [HttpPost]
    public async Task<ServiceResult<int>> Create(ProductDto dto)
    {
        return await _service.AddAsync(dto);
    }

    // PUT: api/Product
    [HttpPut]
    public async Task<ServiceResult<int>> Update(ProductDto dto)
    {
        return await _service.UpdateAsync(dto);
    }

    // DELETE: api/Product/5
    [HttpDelete("{id}")]
    public async Task<ServiceResult<int>> Delete(int id)
    {
        return await _service.DeleteAsync(id);
    }





}