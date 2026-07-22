# Application 服务未实现方法待办

扫描时间：2026-07-21

## 纳入标准

- 扫描范围：`SwiftExport.Application` 的有效服务源码。
- 方法体除注释外，唯一可执行语句为 `throw new NotImplementedException();`。
- 本清单只记录待办，不代表旧注释代码可以直接恢复；实现时仍需按当前接口、仓储和 Controller 契约重新核对。

## 产品

- [x] `BomService.BatchSaveAsync(BomBatchSaveRequest batchReq)`
  - 源码：`Services/Products/BomService.cs:230`
  - 占位抛出：第 296 行

- [x] `BomService.GetPagedListAsync(BomSearchRequest request)`
  - 源码：`Services/Products/BomService.cs:299`
  - 占位抛出：第 301 行

- [x] `ProductCategoryService.BatchSaveAsync(ProductCategoryBatchSaveRequest batchReq)`
  - 源码：`Services/Products/ProductCategoryService.cs:38`
  - 占位抛出：第 104 行

- [x] `ProductCategoryService.GetPagedAsync(ProductCategorySearchRequest request)`
  - 源码：`Services/Products/ProductCategoryService.cs:107`
  - 占位抛出：第 109 行

- [x] `ProductColorService.BatchSaveAsync(ProductColorBatchSaveRequest batchReq)`
  - 源码：`Services/Products/ProductColorService.cs:20`
  - 占位抛出：第 22 行

## 系统配置

- [x] `SysViewRouteService.GetPagedAsync(SysViewRouteSearchRequest request)`
  - 源码：`Services/SystemServices/SysViewRouteService.cs:55`
  - 占位抛出：第 57 行

- [x] `SysViewRouteService.BatchSaveAsync(SysViewRouteBatchSaveRequest batchReq)`
  - 源码：`Services/SystemServices/SysViewRouteService.cs:62`
  - 占位抛出：第 64 行

- [x] `SysDicCurrencyExchangeService.GetPagedAsync(SysDicCurrencyExchangeSearchRequest request)`
  - 源码：`Services/Systems/SysDicCurrencyExchangeService.cs:85`
  - 占位抛出：第 87 行

- [x] `SysDicCurrencyExchangeService.BatchSaveAsync(SysDicCurrencyExchangeBatchSaveRequest batchReq)`
  - 源码：`Services/Systems/SysDicCurrencyExchangeService.cs:90`
  - 占位抛出：第 92 行

- [x] `SysDicCurrencyService.GetBaseCurrencyAsync()`
  - 源码：`Services/Systems/SysDicCurrencyService.cs:68`
  - 占位抛出：第 81 行

- [x] `SysDicCurrencyService.GetEnabledListAsync()`
  - 源码：`Services/Systems/SysDicCurrencyService.cs:87`
  - 占位抛出：第 89 行

- [x] `SysDicCurrencyService.GetPagedAsync(SysDicCurrencySearchRequest request)`
  - 源码：`Services/Systems/SysDicCurrencyService.cs:217`
  - 占位抛出：第 219 行

- [x] `SysDicCurrencyService.BatchSaveAsync(SysDicCurrencyBatchSaveRequest batchReq)`
  - 源码：`Services/Systems/SysDicCurrencyService.cs:222`
  - 占位抛出：第 224 行

- [x] `SysDictDataService.GetByCodeAsync(string dictCode)`
  - 源码：`Services/Systems/SysDictDataService.cs:59`
  - 占位抛出：第 61 行

- [x] `SysDictDataService.BatchSaveAsync(SysDictDataBatchSaveRequest batchReq)`
  - 源码：`Services/Systems/SysDictDataService.cs:65`
  - 占位抛出：第 67 行

- [x] `SysDictTypeService.GetPagedAsync(SysDictTypeSearchRequest request)`
  - 源码：`Services/Systems/SysDictTypeService.cs:250`
  - 占位抛出：第 252 行

- [x] `SysDictTypeService.BatchSaveAsync(SysDictTypeBatchSaveRequest batchReq)`
  - 源码：`Services/Systems/SysDictTypeService.cs:255`
  - 占位抛出：第 257 行

## 汇总

- 产品服务：0 个待实现，5 个已完成。
- 系统配置服务：0 个待实现，12 个已完成。
- 合计：0 个待实现，17 个已完成。
