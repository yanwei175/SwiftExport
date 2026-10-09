---
name: react-web-api
description: SwiftExport Web API 契约接入。用于审核 OpenAPI、Orval Fetch 生成、统一请求边界、TanStack Query 键、mutation、认证与错误事实接线；不修改后端契约或业务行为。
---

# Web API 契约接入

## 输入与范围

输入是端点/生成配置/schema 快照、锁文件、生成操作、真实 Controller/DTO/调用方及指定验收证据。Web 尚未建立时只记录目标，不创建第二套客户端。写入限本次 Web schema、生成配置/产物、queries/mutations、shared/api/query/session 和相关测试；后端/Contracts/契约工具修改须独立授权。

权威： [WEB-API-01 与调用分层](../../../../docs/Web客户端架构与脚手架决策.md#web-api-layers)、[提交分类](../../../../docs/Web多标签工作区设计.md#workspace-save)、[实际接口证据](../../../../docs/Web首批接口契约验收.md)。其他端点没有首批证据时另核验，不照搬 Product。

## 实施与复用入口

1. 核对 paths/method/requestBody/response/security、枚举/null/日期及 auth 裸 DTO 例外；更新审核过的快照，以锁定 Orval 配置生成 HTTP 操作，不手改 generated。
2. 搜索并复用唯一请求执行边界；生成操作委托它传 token、signal、HTTP/网络事实及允许的追踪。选择 schema 中真实函数名，不手写同构 DTO/HTTP 操作。
3. Query Key 包含规范操作、输入、身份及 permissionEpoch；POST page/search 仍属读取。复用 [公共设施基线](../../../../docs/Web客户端架构与脚手架决策.md#web-common-services) 的三类 queryPolicies；查询信号透传，Activity 外的 enabled/防抖另按 isActive/权限接线。写 mutation 关闭自动重试，精确失效相关键；核权变化移除旧业务快照，不覆盖草稿。
4. 按端点记录“响应形态→可证明结果→证据范围”。通用层保留发送阶段、状态和完整响应，feature 按公共保存契约决定 A/B/C；展示错误与证明未生效分开。取消/超时不作回滚证据，不分析中文 message。
5. 复用已确认的 12 小时原 JWT 恢复 Cookie + 内存 Bearer/me 接线。浏览器会话操作才允许同源 credentials，不携带旧 Bearer；普通业务仍为 Bearer/omit。新增真实端点必须先导出/审核 OpenAPI 并 Orval 生成，再接前端；权限键由后端目录机器投影。401/到期保留同用户会话，退出确认与换身份按 [多标签会话规则](../../../../docs/Web多标签工作区设计.md#workspace-session)；不续签、不将 token 写入 Storage。

## 禁止事项

不强行解包所有 data、不吞行级失败、不因 200/422/503 统一接纳或解锁。Product BUSINESS_BLOCKED/UNEXPECTED 的具体限制以 [接入门槛](../../../../docs/Web首批接口契约验收.md#product-recovery) 为准。attemptId 不作为服务器幂等键；没有真实协议不重发写入。

## 验收

重新生成核对 diff，运行实际类型/契约检查；校验路径编码、信号、成功形态、短路证据范围、网络无响应和已有行级结果。输出命令及快照范围；需要真实 PG 证据而未具备时保留门槛，不用模拟 Fetch 代替。
