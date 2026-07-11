# Common 工具规则

## GuidSequentialGenerator

路径：`SwiftExport.Contracts/Common/GuidSequentialGenerator.cs`

用途：

- 生成 SQL Server 友好的顺序 Guid。
- 当前 Infrastructure `DapperRepository.AddAsync` 使用类似顺序 Guid 生成策略。

注意：

- 生成规则影响数据库索引局部性。
- 修改前必须搜索所有引用。
- 不要在前端页面逻辑里滥用它生成业务编码。

## SaveItemResultFactory

路径：`SwiftExport.Contracts/Common/SaveItemResultFactory.cs`

用途：

- `FromCreate`
- `FromUpdate`
- `FromDelete`
- `Failed`

把 `RepositoryWriteResult` 和保存请求转换为 `SaveItemResult`，保留：

- `ClientKey`
- `RowIndex`
- `Operation`
- `Uid`
- `OldVersion`
- `NewVersion`
- `ErrorCode`
- `Message`

## 禁止事项

- 禁止在 Common 中访问数据库。
- 禁止在 Common 中访问 HttpContext。
- 禁止在 Common 中写 WPF、DI、Repository、Controller 逻辑。
- 禁止把业务流程判断抽成 Common 静态工具绕开 Application Service。
