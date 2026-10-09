# SwiftExport Web

独立 React Router Framework SPA，使用 React、TypeScript、Ant Design、TanStack Query 与 Orval Fetch。

在本目录执行 `npm ci`、`npm run dev`。生成检查、类型、lint、纯状态与真实浏览器命令见 [工程入口与命令](docs/工程入口与命令.md)。

- [当前目录职责](docs/目录职责与依赖规则.md)
- [业务页面接入](docs/业务页面开发流程.md)
- [骨架验收与限制](docs/骨架验收记录.md)

生产发布只部署 `build/client`，不启动 Node 业务服务器。真实后端与数据库验收、IIS/Nginx 托管、产品写入接入另按现有门槛进行。本轮不提交、不推送、不部署。
