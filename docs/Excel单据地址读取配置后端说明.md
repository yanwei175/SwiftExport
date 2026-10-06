# Excel地址配置的当前契约

核对日期：2026-10-06。ExcelReadProfile、ExcelReadField保存共享读取配置和字段地址，不保存实际工作簿数据。List/Get/Create/Update/Delete由ExcelReadProfileController调用ExcelReadProfileService。

GET api/excel-read-profiles?groupCode=...列出分组配置；GET /{uid}返回完整配置及结构提示；POST创建空配置；PUT /{uid}按主版本原子提交完整Fields快照；DELETE /{uid}?expectedVersion=...软删配置和字段。删除单字段通过从Fields移除后提交完整快照，不是任意逐行更新入口。

保存使用主Version防止覆盖他人修改。字段种类、地址格式、编号唯一和结构校验以DTO/服务为准；维护权限由端点过滤器控制。客户端读取配置后自行操作工作簿，ForExcel检查另接收解析数据。

依据：[Controller](../SwiftExport.WebApi/Controllers/Systems/ExcelReadProfiles/ExcelReadProfileController.cs)、[服务](../SwiftExport.Application/Services/Systems/ExcelReadProfiles/ExcelReadProfileService.cs)、[实体](../SwiftExport.Core/Entities/Systems/ExcelReadProfiles)、[API](../SwiftExport.Contracts/Apis/SystemApis/IExcelReadProfileApi.cs)。不复述旧增量SQL建表步骤，本轮不访问或修改配置数据。
