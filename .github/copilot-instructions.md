# Copilot Instructions

## 项目指南
- ERP product domain rule: `MasterSku` is a non-transactional parent identifier (e.g., AA010011) carrying shared attributes, while business operations are on child `Sku` variants (e.g., AA010011CH/MB) with variant attributes like price/finish; `Sku` may be created without linking to `MasterSku` (no required FK), and each `Sku` owns BOM data.
- 在代码中添加中文注释，以提高可读性和维护性，尤其是每个方法/区域，以确保注释直观且完整。用户强调不要删除现有注释，注释很重要；修改代码时应保留并补充中文注释。
- The project is a WPF application targeting .NET Framework 4.8 and C# 7.3, eventually running in VSTO. Be cautious with newer Microsoft MVVM Toolkit packages as they may be incompatible。
- 在销售订单明细删除逻辑中：已持久化的行应仅从 UI 隐藏并保留在内部集合中标记为 Deleted，以便后端提交删除；未保存的新行才从内部集合和 UI 一并移除。
- 在销售订单明细中，行级 subtotal 字段（如 NwSubTotal/GwSubTotal/CbmSubTotal/金额小计）需要允许用户手动修改；只有集合汇总字段应作为只读展示。

## Architecture Guidelines
- Implement global dictionary loading via a singleton cache service initialized at application startup.
- Continue to use generic RESTful APIs for data access。

## DataGrid Guidelines
- Keep potentially expensive visual DataGrid triggers (like tracking dirty states) optional or attach them via properties, rather than hardcoding them into the default DataGrid style。