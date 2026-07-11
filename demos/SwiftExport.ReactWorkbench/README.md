# SwiftExport React Workbench

隔离的 BS 前端验证台，用来验证：

- React + TypeScript + Ant Design 的 ERP 页面组织方式。
- AG Grid Community 作为免费表格底座。
- 自研 `ProductLookupCell` 在单据行里完成产品搜索、图片候选、键盘选择和多字段回填。
- 产品管理页通过 Vite 代理调用真实 ASP.NET API：`POST /api/Product/search`。

边界：

- 不修改主项目业务代码。
- 不加入 `.slnx`。
- 销售订单 lookup 使用 mock 数据验证交互。
- 产品管理页使用真实 ASP.NET API 读取产品列表和详情。

产品管理页真实 API：

```text
POST /api/Product/search
GET  /api/Product/{uid}
```

前端请求 `/api/Product/search`，由 Vite 代理转发到 `https://localhost:7286/api/Product/search`，避免修改后端 CORS。

运行：

```powershell
npm.cmd install
npm.cmd run dev
```

构建检查：

```powershell
npm.cmd run build
```
