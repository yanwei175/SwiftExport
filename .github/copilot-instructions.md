---

---

# Copilot Instructions

## 项目指南
- ERP product domain rule: `MasterSku` is a non-transactional parent identifier (e.g., AA010011) carrying shared attributes, while business operations are on child `Sku` variants (e.g., AA010011CH/MB) with variant attributes like price/finish; `Sku` may be created without linking to `MasterSku` (no required FK), and each `Sku` owns BOM data.

- 在代码中添加中文注释，以提高可读性和维护性，尤其是每个方法/区域，以确保注释直观且完整。用户强调不要删除现有注释，注释很重要；修改代码时应保留并补充中文注释。

- The project is a WPF application targeting .NET Framework 4.8 and C# 7.3, eventually running in VSTO. Be cautious with newer Microsoft MVVM Toolkit packages as they may be incompatible. Note that VSTO is not currently in the development scope and will be handled separately.

- 在销售订单明细删除逻辑中：已持久化的行应仅从 UI 隐藏并保留在内部集合中标记为 Deleted，以便后端提交删除；未保存的新行才从内部集合和 UI 一并移除。

- 在销售订单明细中，行级 subtotal 字段（如 NwSubTotal/GwSubTotal/CbmSubTotal/金额小计）需要允许用户手动修改；只有集合汇总字段应作为只读展示。

- MateWpfThemes.FW 主题库是项目所有 WPF 样式/主题的唯一来源。所有业务视图依赖此库。

# 文件结构总览

SwiftExport/
├── .github/
│   └── copilot-instructions.md          ← 本文件
├── SwiftExport.Core/                     ← 核心层（域实体、接口）
│   ├── Entities/MasterData/
│   │   ├── Product.cs
│   │   ├── ProductCategory.cs
│   │   └── ...
│   ├── Common/
│   │   ├── BaseEntity.cs
│   │   ├── QueryCriteria.cs
│   │   └── QueryCondition.cs
│   ├── Interfaces/
│   │   ├── IRepository.cs
│   │   └── IProductRepository.cs
│   └── Enums/
├── SwiftExport.Application/              ← 应用层（业务逻辑）
│   ├── Services/Base/
│   │   └── BaseService.cs
│   ├── Services/Products/
│   │   └── ProductService.cs
│   ├── Interfaces/
│   │   ├── IBaseService.cs
│   │   └── IProductService.cs
│   ├── Mappings/
│   │   └── ProductProfile.cs
│   └── DependencyInjection.cs
├── SwiftExport.Infrastructure/          ← 基础设施层（数据访问）
│   ├── Repositories/Base/
│   │   └── DapperRepository.cs
│   ├── Repositories/Products/
│   │   └── ProductRepository.cs
│   ├── Data/
│   │   ├── DbSession.cs
│   │   └── UnitOfWork.cs
│   └── DependencyInjection.cs
├── SwiftExport.Contracts/               ← 契约层（DTO / API）
│   ├── DTOs/Base/
│   │   └── BaseDto.cs
│   ├── DTOs/Products/
│   │   └── ProductDto.cs
│   ├── Wrappers/
│   │   ├── ServiceResult.cs
│   │   └── BatchRequestGeneric.cs
│   └── Apis/
│       └── IProductApi.cs
├── SwiftExport.WebApi/                  ← WebAPI 层（控制器）
│   ├── Controllers/Base/
│   │   └── BaseController.cs
│   ├── Controllers/Products/
│   │   └── ProductController.cs
│   ├── Middlewares/
│   │   └── GlobalExceptionMiddleware.cs
│   ├── Program.cs
│   └── appsettings.json
├── SwiftExport.VSTO/                    ← Excel 插件层
│   ├── App.xaml.cs
│   └── ...
└── SwiftExport.View.FW/                 ← WPF 视图层（.NET Framework 4.8）
    ├── Services/
    ├── ViewModels/
    └── Views/

### 核心分层结构

┌─────────────────────────────────────────────────────────────────┐
│                       表现层 (Presentation)                      │
│  SwiftExport.VSTO (Excel 插件暂时还没开发) | SwiftExport.View.FW (WPF)    │
└──────────────────────┬──────────────────────────────────────────┘
                       │ (HTTP Client / Refit)
┌──────────────────────▼──────────────────────────────────────────┐
│                      WebAPI 层 (API Gateway)                    │
│  SwiftExport.WebApi (ASP.NET Core)                             │
│  ├─ Controllers/Base/BaseController<TEntity, TDto>             │
│  ├─ Middlewares (GlobalExceptionMiddleware)                    │
│  └─ Program.cs (Serilog 初始化、DI 配置)                       │
└──────────────────────┬──────────────────────────────────────────┘
                       │
┌──────────────────────▼──────────────────────────────────────────┐
│               应用层 (Application Logic)                         │
│  SwiftExport.Application                                        │
│  ├─ Services/Base/BaseService<TEntity, TDto>                  │
│  │  └─ 业务编排、事务协调、数据映射、日志记录                  │
│  ├─ Services/XxxService                                        │
│  │  └─ 领域特定业务逻辑 + 自定义验证 Hook                      │
│  ├─ Interfaces/IBaseService<TEntity, TDto>                    │
│  └─ DependencyInjection.cs                                     │
└──────────────────────┬──────────────────────────────────────────┘
                       │
┌──────────────────────▼──────────────────────────────────────────┐
│  契约层 (Contracts - DTO / API 协议)                            │
│  SwiftExport.Contracts                                          │
│  ├─ DTOs/Base/BaseDto (所有 DTO 继承)                          │
│  ├─ Wrappers/ServiceResult<T> (统一结果封装)                   │
│  ├─ Wrappers/BatchRequestGeneric<T> (批量操作请求)             │
│  └─ Apis/IGenericApi<TDto> (Refit 通用客户端)                  │
└──────────────────────┬──────────────────────────────────────────┘
                       │
┌──────────────────────▼──────────────────────────────────────────┐
│            基础设施层 (Infrastructure / Data Access)            │
│  SwiftExport.Infrastructure                                     │
│  ├─ Repositories/Base/DapperRepository<T>                      │
│  │  └─ 数据访问、参数化查询、事务管理                          │
│  ├─ Repositories/XxxRepository                                 │
│  │  └─ 特定领域的复杂查询                                      │
│  ├─ Data/DbSession (连接和事务容器)                            │
│  ├─ Data/UnitOfWork (事务协调)                                 │
│  └─ DependencyInjection.cs                                     │
└──────────────────────┬──────────────────────────────────────────┘
                       │
┌──────────────────────▼──────────────────────────────────────────┐
│              核心层 (Domain / Business Rules)                   │
│  SwiftExport.Core (纯业务逻辑，不依赖基础设施)                  │
│  ├─ Entities/MasterData/XxxEntity (域实体)                     │
│  ├─ Common/BaseEntity (基础实体类)                              │
│  ├─ Common/QueryCriteria (动态查询条件)                        │
│  ├─ Enums/XxxEnum (业务枚举)                                   │
│  └─ Interfaces/IRepository<T> (仓储接口契约)                   │
└─────────────────────────────────────────────────────────────────┘

### QueryCriteria - 动态查询条件构建
// SwiftExport.Core/Common/QueryCondition.cs
public enum 查询操作符
{
    等于,          // =
    模糊匹配,      // LIKE
    大于,          // >
    小于,          // <
    大于等于,      // >=
    小于等于,      // <=
    区间,          // BETWEEN
    包含某值       // IN
}

public class QueryCondition
{
    public 查询操作符 操作符 { get; set; }  // 操作符
    public object? 值1 { get; set; }       // 单值或区间起始
    public object? 值2 { get; set; }       // 区间结束
}

public class QueryItem
{
    public string Field { get; set; }           // 字段名
    public QueryCondition Condition { get; set; } // 条件
}

public class QueryCriteria
{
    // AND 条件列表（支持重名字段）
    public List<QueryItem> Items { get; set; } = [];
    // OR 分组列表（每组内是 OR 关系，组间是 AND 关系）
    public List<List<QueryItem>> OrGroups { get; set; } = [];

    // 快捷方法：添加 AND 条件
    public void Add(string field, 查询操作符 op, object v1, object? v2 = null)

    // 快捷方法：添加 OR 分组
    // 场景：(SKU LIKE @Kw OR Description LIKE @Kw) AND (Status = @St1 OR Status = @St2)
    public void AddOrGroup(params (string field, 查询操作符 op, object value)[] conditions)
}
#### **使用示例**:
// 查询：(Sku IN (...) OR CSku IN (...)) AND Status = 'Active'
var criteria = new QueryCriteria();
criteria.AddOrGroup(
    ("Sku", 查询操作符.包含某值, skuList),
    ("CSku", 查询操作符.包含某值, skuList)
);
criteria.Add("Status", 查询操作符.等于, "Active");

var result = await _service.GetListAsync(criteria);
### ServiceResult<T> - 统一结果封装
// SwiftExport.Contracts/Wrappers/ServiceResult.cs
public class ServiceResult<T>
{
    // 是否成功
    public bool Success { get; set; }
    // 提示消息
    public string Message { get; set; } = string.Empty;
    
    // 返回数据
    public T Data { get; set; }
    
    // 快捷方法：成功
    public static ServiceResult<T> Ok(T data, string msg = "成功") 
        => new ServiceResult<T> { Success = true, Data = data, Message = msg };
    
    // 快捷方法：失败
    public static ServiceResult<T> Fail(string msg) 
        => new ServiceResult<T> { Success = false, Message = msg };
}
**前端判断示例**:
const result = await api.getProduct(id);
if (result.success) {
    console.log("成功:", result.data);
} else {
    console.error("失败:", result.message);
}
### BatchRequestGeneric<T> - 批量操作请求
public class BatchRequestGeneric<T> where T : class
{
    // 新增列表
    public List<T>? AddList { get; set; }
    
    // 修改列表（必须带 ID）
    public List<T>? UpdateList { get; set; }
    
    // 删除 ID 列表
    public List<int>? DeleteIdList { get; set; }
}

public class BatchResultDto
{
    // 新增后的 ID 列表
    public List<int> AddIdList { get; set; } = [];
    // 修改的 ID 列表
    public List<int> UpdateIdList { get; set; } = [];
    
    // 删除的 ID 列表
    public List<int> DeleteIdList { get; set; } = [];
}
## 通用仓储基类详解 (DapperRepository)

### 核心特性
// SwiftExport.Infrastructure/Repositories/Base/DapperRepository.cs
public partial class DapperRepository<T>(RepositoryBaseContext context) : IRepository<T> 
    where T : BaseEntity
{
    // 1️⃣ 属性反射缓存：一次性获取可写属性，避免重复反射
    private static readonly List<string> _writeableProps = GetWriteableProperties();
    // 2️⃣ 连接和事务通过 DbSession 管理
    protected IDbConnection Connection => _session.Connection;
    protected IDbTransaction? Transaction => _session.Transaction;
    
    // 3️⃣ 日志记录（Info/Warning/Error）
    protected ILogger Logger { get; }
    
    // 4️⃣ 表名自动提取（从 [Table] 特性或类名）
    protected static readonly string TableName = 
        typeof(T).GetCustomAttribute<TableAttribute>()?.Name ?? typeof(T).Name;
}
#### 动态条件查询（QueryCriteria）
// 构建复杂查询：(Sku 包含 "AA" OR Description 包含 "AA") AND CategoryId = 5
var criteria = new QueryCriteria();
criteria.AddOrGroup(
    ("Sku", 查询操作符.模糊匹配, "AA"),
    ("Description", 查询操作符.模糊匹配, "AA")
);
criteria.Add("CategoryId", 查询操作符.等于, 5);

var result = await _repository.GetListAsync(criteria);

// 生成的 SQL 伪码：
// SELECT * FROM [Product] 
// WHERE IsDeleted = 0 OR IsDeleted IS NULL
// AND ([Sku] LIKE '%AA%' OR [Description] LIKE '%AA%')
// AND [CategoryId] = @CategoryId_0

#### 分页查询

var pageResult = await _repository.GetPagedListAsync(
    criteria: criteria,  // QueryCriteria 对象
    pageIndex: 1,        // 从 1 开始
    pageSize: 50,
    orderBy: "Id DESC"   // 必填，影响 OFFSET 语义
);

// 返回结构
public class PagedResult<T>
{
    public List<T> Items { get; set; }     // 当前页数据
    public int TotalCount { get; set; }    // 总条数
    public int PageIndex { get; set; }     // 当前页码
    public int PageSize { get; set; }      // 页大小
}
## 通用服务基类详解 (BaseService)
// SwiftExport.Application/Services/Base/BaseService.cs
public class BaseService<TEntity, TDto>(
    IRepository<TEntity> repository, 
    ServiceBaseContext context) : IBaseService<TEntity, TDto>
    where TEntity : BaseEntity
    where TDto : BaseDto
{
    // 1️⃣ 自动映射 Entity <-> DTO（使用 Mapster）
    var dto = entity.Adapt<TDto>();
    // 2️⃣ 业务校验和异常处理
    if (entity == null)
        return ServiceResult<TDto>.Fail("实体不存在");

    // 3️⃣ 结构化日志（Info/Warning/Error）
    Logger.LogInformation("成功新增 {EntityName}，ID: {Id}", EntityName, newId);

    // 4️⃣ 事务协调（批量保存时）
    _session.Transaction = _session.Connection.BeginTransaction();

    // 5️⃣ 统一返回 ServiceResult<T>
    return ServiceResult<TDto>.Ok(dto);
}
### 软/硬删除策略
// 子类可通过重写来控制删除策略
public class CustomerService : BaseService<Customer, CustomerDto>
{
    // 硬删除：数据库中完全删除
    protected override bool IsSoftDelete => false;
}

public class ProductService : BaseService<Product, ProductDto>
{
    // 软删除：只标记 IsDeleted = 1（默认）
    protected override bool IsSoftDelete => true;
}

// BaseService 在 BatchSaveAsync 中自动应用
if (IsSoftDelete)
    await _repository.DeleteRangeSoftAsync(deleteIds);
else
    await _repository.DeleteRangeAsync(deleteIds);
### 批量保存事务流程（BatchSaveAsync）
public virtual async Task<ServiceResult<BatchResultDto>> BatchSaveAsync(BatchRequestGeneric<TDto> batchReq)
{
    // 1️⃣ 开启事务
    _session.Transaction = _session.Connection.BeginTransaction();
    
    try
    {
        // 2️⃣ 映射 DTO 到 Entity
        var createList = batchReq.AddList?.Adapt<List<TEntity>>();
        var updateList = batchReq.UpdateList?.Adapt<List<TEntity>>();
        var deleteIds = batchReq.DeleteIdList;
        
        // 3️⃣ 执行校验（基础 + 子类 Hook）
        var errorMsg = new StringBuilder();
        
        if (createList != null)
        {
            // 基础校验 + 调用 ValidateBeforeAddAsync()
            var customError = await ValidateBeforeAddAsync(createList);
            if (!string.IsNullOrEmpty(customError)) 
                errorMsg.AppendLine(customError);
        }
        
        if (updateList != null)
        {
            // ID 校验 + 存在性校验 + 调用 ValidateBeforeUpdateAsync()
            var invalidIds = updateList.Where(x => x.Id <= 0).ToList();
            if (invalidIds.Any()) 
                errorMsg.AppendLine($"包含无效 ID: {string.Join(", ", invalidIds)}");
            
            var customError = await ValidateBeforeUpdateAsync(updateList);
            if (!string.IsNullOrEmpty(customError)) 
                errorMsg.AppendLine(customError);
        }
        
        // 如果校验失败，直接中断（不提交事务）
        if (errorMsg.Length > 0)
            return ServiceResult<BatchResultDto>.Fail(errorMsg.ToString());
        
        // 4️⃣ 执行数据库写入
        var result = new BatchResultDto();
        
        if (createList?.Count > 0)
            result.AddIdList = await _repository.AddRangeAsync(createList);
        
        if (updateList?.Count > 0)
            await _repository.UpdateRangeAsync(updateList);
        
        if (deleteIds?.Count > 0)
        {
            if (IsSoftDelete)
                await _repository.DeleteRangeSoftAsync(deleteIds);
            else
                await _repository.DeleteRangeAsync(deleteIds);
        }
        
        // 5️⃣ 提交事务
        _session.Transaction.Commit();
        return ServiceResult<BatchResultDto>.Ok(result);
    }
    catch (Exception ex)
    {
        // 6️⃣ 异常回滚
        _session.Transaction.Rollback();
        Logger.LogError(ex, "批量保存事务失败");
        throw;
    }
    finally
    {
        _session.Transaction.Dispose();
    }
}
### 子类可继承的 Hook 方法
public class ProductService : BaseService<Product, ProductDto>
{
    // Hook 1: 新增前的业务校验
    protected override async Task<string?> ValidateBeforeAddAsync(List<Product> entities)
    {
        // 检查 SKU 是否重复
        var skus = entities.Select(x => x.Sku).ToList();
        var existedSkus = await _repository.GetExistingSkusAsync(skus);
        
        if (existedSkus.Any())
            return $"以下 SKU 已存在: {string.Join(", ", existedSkus)}";
        
        return null; // 返回 null 表示校验通过
    }
    
    // Hook 2: 修改前的业务校验
    protected override async Task<string?> ValidateBeforeUpdateAsync(List<Product> entities)
    {
        // 检查是否可修改（例如：已发货的产品不允许修改）
        var immutableIds = new List<int>();
        foreach (var product in entities)
        {
            var status = await _repository.GetStatusAsync(product.Id);
            if (status == "已发货") immutableIds.Add(product.Id);
        }
        
        if (immutableIds.Any())
            return $"已发货的产品不允许修改，ID: {string.Join(", ", immutableIds)}";
        
        return null;
    }
    
    // Hook 3: 删除前的业务校验
    protected override async Task<string?> ValidateBeforeDeleteAsync(List<int> ids)
    {
        // 检查是否有订单关联
        var idsWithOrders = await _repository.GetProductsWithOrdersAsync(ids);
        
        if (idsWithOrders.Any())
            return $"以下产品有订单关联，无法删除: {string.Join(", ", idsWithOrders)}";
        
        return null;
    }
}
## WebAPI 层设计

### BaseController - 通用 RESTful 端点
// SwiftExport.WebApi/Controllers/Base/BaseController.cs
[ApiController]
[Route("api/[controller]")]
public abstract class BaseController<TEntity, TDto>(IBaseService<TEntity, TDto> service) 
    : ControllerBase
    where TEntity : BaseEntity
    where TDto : class
{
    // ✅ 必须是 abstract，防止 ASP.NET 把它当成一个真实的 Controller 注册
    
    protected readonly IBaseService<TEntity, TDto> _service = service;
    
    // 1️⃣ 查询单条
    [HttpGet("{id}")]
    public virtual async Task<ServiceResult<TDto>> GetById(int id)
    {
        return await _service.GetByIdAsync(id);
    }
    
    // 2️⃣ 新增单条
    [HttpPost]
    public virtual async Task<ServiceResult<int>> Create([FromBody] TDto dto)
    {
        return await _service.AddAsync(dto);
    }
    
    // 3️⃣ 修改单条
    [HttpPut("{id}")]
    public virtual async Task<ServiceResult<int>> Update(int id, [FromBody] TDto dto)
    {
        return await _service.UpdateAsync(dto);
    }
    
    // 4️⃣ 删除单条
    [HttpDelete("{id}")]
    public virtual async Task<ServiceResult<int>> Delete(int id)
    {
        return await _service.DeleteAsync(id);
    }
    
    // 5️⃣ 分页查询
    [HttpPost("page")]
    public virtual async Task<ServiceResult<PagedResultDto<TDto>>> GetPagedList(
        [FromBody] QueryCriteria criteria, 
        [FromQuery] int pageIndex = 1, 
        [FromQuery] int pageSize = 500)
    {
        return await _service.GetPagedListAsync(criteria, pageIndex, pageSize);
    }
    
    // 6️⃣ 批量保存
    [HttpPost("batch-save")]
    public virtual async Task<ServiceResult<BatchResultDto>> BatchSave([FromBody] BatchRequestGeneric<TDto> req)
    {
        return await _service.BatchSaveAsync(req);
    }
}
### 具体控制器示例
// SwiftExport.WebApi/Controllers/Products/ProductController.cs
public class ProductController(IProductService service) : BaseController<Product, ProductDto>(service)
{
    // 继承基类的所有标准端点
    // 也可以添加领域特定的端点
    
    // 1️⃣ 特殊查询：按 SKU 搜索
    [HttpGet("sku/{sku}")]
    public async Task<ServiceResult<ProductDto>> GetBySku(string sku)
    {
        return await service.GetBySkuAsync(sku);
    }
    
    // 2️⃣ 高级分页：产品搜索
    [HttpPost("search")]
    public async Task<ServiceResult<PagedResultDto<ProductDto>>> GetProductPagedList(
        [FromBody] ProductSearchDto search)
    {
        // 构建动态查询条件
        var criteria = new QueryCriteria();
        
        if (!string.IsNullOrWhiteSpace(search.BatchKeywords))
        {
            var keywords = search.BatchKeywords
                .Split(['\r', '\n', ','], StringSplitOptions.RemoveEmptyEntries)
                .Select(k => k.Trim())
                .Distinct()
                .ToList();
            
            // (Sku IN (...) OR CSku IN (...))
            criteria.AddOrGroup(
                ("Sku", 查询操作符.包含某值, keywords),
                ("CSku", 查询操作符.包含某值, keywords)
            );
        }
        
        if (!string.IsNullOrWhiteSpace(search.Keyword))
        {
            criteria.AddOrGroup(
                ("Sku", 查询操作符.模糊匹配, search.Keyword),
                ("Description", 查询操作符.模糊匹配, search.Keyword)
            );
        }
        
        int pageIndex = search.PageIndex > 0 ? search.PageIndex : 1;
        int pageSize = search.PageSize > 0 ? search.PageSize : 50;
        
        return await service.GetPagedListAsync(criteria, pageIndex, pageSize, "Sku ASC");
    }
}
## Program.cs 启动流程
// SwiftExport.WebApi/Middlewares/GlobalExceptionMiddleware.cs
public class GlobalExceptionMiddleware(RequestDelegate next, ILogger<GlobalExceptionMiddleware> logger)
{
    // ★ 静态只读：全局复用一个 JsonSerializerOptions 实例，避免重复分配
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };
    
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            // 正常流程
            await _next(context);
        }
        catch (Exception ex)
        {
            // 异常捕获并日志记录
            await HandleExceptionAsync(context, ex);
        }
    }
    
    private async Task HandleExceptionAsync(HttpContext context, Exception ex)
    {
        // 1️⃣ 记录错误日志
        _logger.LogError(ex, "未捕获异常 - 请求路径: {Path}", context.Request.Path);
        
        // 2️⃣ 构建统一的错误响应
        var response = ServiceResult<string>.Fail("系统繁忙，请稍后再试");
        
        // 3️⃣ 返回 JSON
        context.Response.ContentType = "application/json";
        context.Response.StatusCode = StatusCodes.Status200OK;  // 始终 200，错误信息在 Success 字段
        
        var json = JsonSerializer.Serialize(response, _jsonOptions);
        await context.Response.WriteAsync(json);
    }
}
## 异常处理中间件
// SwiftExport.WebApi/Middlewares/GlobalExceptionMiddleware.cs
public class GlobalExceptionMiddleware(RequestDelegate next, ILogger<GlobalExceptionMiddleware> logger)
{
    // ★ 静态只读：全局复用一个 JsonSerializerOptions 实例，避免重复分配
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };
    
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            // 正常流程
            await _next(context);
        }
        catch (Exception ex)
        {
            // 异常捕获并日志记录
            await HandleExceptionAsync(context, ex);
        }
    }
    
    private async Task HandleExceptionAsync(HttpContext context, Exception ex)
    {
        // 1️⃣ 记录错误日志
        _logger.LogError(ex, "未捕获异常 - 请求路径: {Path}", context.Request.Path);
        
        // 2️⃣ 构建统一的错误响应
        var response = ServiceResult<string>.Fail("系统繁忙，请稍后再试");
        
        // 3️⃣ 返回 JSON
        context.Response.ContentType = "application/json";
        context.Response.StatusCode = StatusCodes.Status200OK;  // 始终 200，错误信息在 Success 字段
        
        var json = JsonSerializer.Serialize(response, _jsonOptions);
        await context.Response.WriteAsync(json);
    }
}
### 日志级别使用

| 级别            | 场景                  | 示例                           |
| --------------- | --------------------- | ------------------------------ |
| **Debug**       | 动态 SQL 追踪、参数值 | 仓储层生成的查询 SQL           |
| **Information** | 业务成功操作          | "成功新增产品 ID: 123"         |
| **Warning**     | 预期的异常情况        | "查询失败：产品不存在 ID: 999" |
| **Error**       | 系统错误、异常捕获    | 数据库连接失败、业务异常       |
| **Fatal**       | 应用启动失败          | 数据库初始化失败、配置缺失     |
// 1️⃣ 新增操作 (Information)
Logger.LogInformation("成功新增 {EntityName} 数据，生成的 ID: {Id}", EntityName, id);

// 2️⃣ 查询失败 (Warning)
Logger.LogWarning("查询失败：未找到 ID 为 {Id} 的 {EntityName} 数据", id, EntityName);

// 3️⃣ 动态 SQL (Debug)
Logger.LogDebug("执行动态查询 [{TableName}]，生成的 SQL: {Sql}", TableName, finalSql);

// 4️⃣ 系统异常 (Error)
Logger.LogError(ex, "批量保存事务失败，已回滚");

// 5️⃣ 应用启动 (Information)
Log.Information("SwiftExport WebAPI 启动中...");

// 6️⃣ 启动失败 (Fatal)
Log.Fatal(ex, "WebAPI 启动失败，遭遇致命异常！");
### 前端 (Refit 客户端)
// 定义 API 接口
[Headers("Authorization: Bearer")]
public interface IProductApi
{
    [Get("/api/product/{id}")]
    Task<ServiceResult<ProductDto>> GetByIdAsync(int id);
    
    [Post("/api/product")]
    Task<ServiceResult<int>> CreateAsync([Body] ProductDto dto);
    
    [Post("/api/product/search")]
    Task<ServiceResult<PagedResultDto<ProductDto>>> SearchAsync([Body] ProductSearchDto search);
    
    [Post("/api/product/batch-save")]
    Task<ServiceResult<BatchResultDto>> BatchSaveAsync([Body] BatchRequestGeneric<ProductDto> request);
}

// 使用示例
var api = RestService.For<IProductApi>("https://api.example.com");

// 单条查询
var result = await api.GetByIdAsync(5);
if (result.Success) {
    Console.WriteLine($"产品: {result.Data.Sku}");
} else {
    Console.WriteLine($"错误: {result.Message}");
}

// 批量保存
var batchResult = await api.BatchSaveAsync(new BatchRequestGeneric<ProductDto> {
    AddList = newProducts,
    UpdateList = editedProducts,
    DeleteIdList = new[] { 1, 2, 3 }
});
## Architecture Guidelines

- Implement global dictionary loading via a singleton cache service initialized at application startup.
- Continue to use generic RESTful APIs for data access.

## DataGrid Guidelines
- Keep potentially expensive visual DataGrid triggers (like tracking dirty states) optional or attach them via properties, rather than hardcoding them into the default DataGrid style.