---
name: aspdotnet10-api-application-service
description: SwiftExport 后端 API 与 Application Service 开发规则。用于新增或修改 Controller、Application Interface/Service、业务命令、状态流转和事务编排。
---

# 后端 API 与 Application Service Skill

## 必读

公共启动沿用 `AGENTS.md`；以下仅补齐当前业务任务尚未读取的依赖，不回溯已读入口。

- `.agents/references/工程架构总纲.md`
- 涉及 Controller、路由、HTTP 方法时读 `../webapi-controller-boundary/SKILL.md`
- 涉及复杂单据、业务命令、CommandId、Preview、幂等、过账或反向时读 `../application-document-command/SKILL.md`
- 涉及服务、接口、Policy、Repository 或 Controller 的职责与文件拆分时读 `../backend-file-organization/SKILL.md`
- 涉及实现验证时读 `../backend-quality-gates/SKILL.md`
- 涉及认证、当前用户、DevUser 时读 `../webapi-auth-current-user/SKILL.md`
- 涉及 Program、Filter、Middleware、JSON、Swagger 时读 `../webapi-pipeline-filters/SKILL.md`
- 涉及服务基础依赖、事务或日志时读 `../application-service-context/SKILL.md`
- 继承或复用 CRUD 基类时读 `../application-base-service/SKILL.md`
- 使用批量保存、分页、保存结果传输模型时读 `../application-transfer-dtos/SKILL.md`
- 涉及 Entity/DTO 转换时读 `../application-mapster-mapping/SKILL.md`
- 业务流程任务补读 `.agents/references/业务架构总纲.md`
- 状态/库存任务补读 `.agents/references/数据模型与状态规则.md`

## 开发顺序

1. 确认业务场景和模块边界。
2. 设计或确认 `SwiftExport.Contracts` 中的 DTO、Request、Response、Refit API 和共享枚举。
3. 在 `SwiftExport.Application/Interfaces` 声明服务接口方法。
4. 在 `SwiftExport.Application/Services/<Module>` 实现业务编排。
5. 标准 CRUD 优先复用 `IBaseService` / `BaseService`；复杂命令在具体 Service 中显式实现，并按真实用例族划分 Query、Draft、Confirmation、Posting、Reversal 等职责。
6. 优先复用现有 Repository。
7. 如现有 Repository 不足，再新增 Core Interface 和 Infrastructure Dapper 实现。
8. 在 `SwiftExport.WebApi/Controllers/<Module>` 新增或修改 action。
9. 运行 `dotnet build .\SwiftExport-WebAPI.slnx`。

## WebApi 边界

Controller 只负责：

- 路由。
- HTTP 方法。
- 模型绑定。
- 调用 Application Service。
- 返回结果。

Controller 禁止：

- 写 SQL。
- 直接访问 Infrastructure。
- 承担业务流程判断。
- 直接处理库存、过账、改单等复杂业务。

## Application 边界

Application Service 负责：

- 业务校验。
- 业务命令。
- 状态流转。
- 多 Repository 编排。
- 事务边界。

Application Service 禁止：

- 直接创建 `NpgsqlConnection` 或其他数据库连接；使用 Core `IDbSession` 共享会话。
- 硬编码连接字符串。
- 处理 WPF UI 状态。
- 调用 Controller。
- 按页面或固定行数建立大而全 Manager Service。

## 业务命令规则

确认、取消确认、拆解、转采购、入库过账、PackingList 过账、反过账、红冲、关闭、作废都必须是明确业务命令。

不要把业务命令伪装成普通 `Update`。

正式命令还必须明确 Version、CommandId、持久化回执、业务审计、事务所有者、锁顺序和失败恢复；防抖 Filter 不属于业务正确性保障。

## 输出要求

完成后说明：

- Controller / Application / Contracts / Repository 改了什么。
- 事务边界在哪里。
- 是否涉及状态或库存流水。
- 是否运行后端构建。
