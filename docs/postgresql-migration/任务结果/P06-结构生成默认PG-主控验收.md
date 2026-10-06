# P06 结构生成默认PG主控验收

本批SchemaGenerator默认生成和核验PG评审脚本，消除应用已接PG但维护命令仍默认生成SQL的入口残留。仅 `_tools/SchemaGenerator/Program.cs` 的默认值与相邻中文职责注释两行，以及数据库README/迁移记录；没有修改生成算法、实体、仓储、服务、API或用户配置。

- 主Agent/工作流：Backend/后端API工作流，沿用后端/质量门禁技能；公共入口主控串行，A/B无续派。
- 原事实：工具默认provider=sqlserver，显式 `--provider postgresql --verify` 实际通过；DbSeed/AdminPasswordRecovery/默认DI当前已PG，未再更改这些入口。
- 当前实现：默认provider=postgresql，现有参数/输出路径/未知参数拒绝/漂移规则保持。无参数输出 `Generated/SwiftExport.PostgreSql.Schema.preview.sql`，默认 `--verify`核验同一PG文件；迁移期间显式SQL仅对照，P09前置未满足，不删除仍有效旧基线/技术测试/依赖，不建设长期提供者支持。
- 修改后Program SHA256：969E1272846E411E7AA345FA4E5A65E3C5CCF759A3CDF0AD373099B3E32FAB52。
- 真实命令：`dotnet build _tools/SchemaGenerator/SchemaGenerator.csproj --no-restore -v minimal`，0错误/1既有NU1900漏洞源访问警告，0.96秒。
- 修改前实际显式PG漂移检查：`dotnet run --project _tools/SchemaGenerator/SchemaGenerator.csproj --no-restore -- --verify --provider postgresql`，exit0/实体元数据一致，1NU1900环境警告。
- 修改后实际无参数执行：`dotnet run --project _tools/SchemaGenerator/SchemaGenerator.csproj --no-build --no-restore`，首次默认沙箱UnauthorizedAccessException在既有PG Generated文件，未成功生成；不视为业务回归。获准同一命令后exit0，明确输出PG本地文件，前后哈希确认两脚本内容未变。
- 修改后实际默认核验：`dotnet run --project _tools/SchemaGenerator/SchemaGenerator.csproj --no-build --no-restore -- --verify`，exit0/一致。
- 修改后实际旧SQL对照：`dotnet run --project _tools/SchemaGenerator/SchemaGenerator.csproj --no-build --no-restore -- --verify --provider sqlserver`，exit0/一致。

|文件|执行前后SHA256（完全相同）|
|---|---|
|`Generated/SwiftExport.PostgreSql.Schema.preview.sql`|E380942783225DA4F7A33AC2744423D066910B043CB4848DDC71217D34FD9730|
|`Generated/SwiftExport.Schema.sql`|C33B00E1C1C85F5EB0B56755495191F9E37AED2E7323D52CB3774EAB57A43015|

没有新增单元测试、运行普通或全量测试；直接CLI行为及既有漂移检查足以核验这两行选择改动。此工具无数据库连接代码，本批没有任何数据库读写/结构变更或部署，不需要同步dv_next，已有数据与备份未触及。未知参数行为未重新运行，原校验源码未改；PG结构部署/特殊比较/P08全验/P09仍开放，不用本地生成通过冒充部署验收。全部未提交修改保留，无提交/推送/合并。
