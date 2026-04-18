# CustomerManagerView 快速参考

## 📊 两个 DataGrid 的结构

### Tab 1: 客户列表 DataGrid
```
┌─────────────────────────────────────────────────────────────────────────────────┐
│ ✓ │ 客户代码 │ 客户名称 │ 单据号 │ 联系人 │ 电话 │ 地址 │ 贸易条款 │ 贸易地点 │ ...│
├─────────────────────────────────────────────────────────────────────────────────┤
│ ☑ │ CUS001 │ ABC Co.  │ DOC01  │ 张三   │ 123  │ 深圳  │ FOB    │ Shenzhen│ ...│
│ ☐ │ CUS002 │ XYZ Ltd  │ DOC02  │ 李四   │ 456  │ 北京  │ CIF    │ Shanghai│ ...│
└─────────────────────────────────────────────────────────────────────────────────┘
               ↓ 选择一行后 ↓
            SelectedCustomer 更新
               ↓
         HasSelectedCustomer = true
               ↓
      客户产品管理按钮自动启用
```

### Tab 2: 客户产品管理 DataGrid
```
前置条件: 必须从 Tab 1 选择一个客户
┌──────────────────────────────────────────────────────────────────────────────┐
│ ✓ │ 图片 │ 客户编号 │ 物料编号 │ 客户描述 │ 客户颜色 │ 专用单价 │ 条码1 │ ...│
├──────────────────────────────────────────────────────────────────────────────┤
│ ☑ │ 📷   │ SKU001  │ P001    │ 描述1   │ 红色    │ 1.5 USD │ BAR001 │ ...│
│ ☐ │ 📷   │ SKU002  │ P002    │ 描述2   │ 蓝色    │ 2.0 USD │ BAR002 │ ...│
└──────────────────────────────────────────────────────────────────────────────┘
```

---

## 🔄 交互流程

### 正常流程
```
1. 用户打开 CustomerManagerView
   ↓
2. 默认进入"客户列表"Tab（ActiveTab = 0）
   ↓
3. 加载所有客户数据到 CurrentCustomerTable
   ↓
4. 用户在客户表中选择一行
   ↓
5. SelectedCustomer 属性更新 → RaisePropertyChanged(nameof(HasSelectedCustomer))
   ↓
6. 客户产品管理标签页的按钮 IsEnabled 自动变为 true ✅
   ↓
7. 用户点击"客户产品管理"标签
   ↓
8. ActiveTab setter 检测到 value == CustomerManagerTabs.CustomerProducts
   ↓
9. 触发 CheckAndLoadProduct()
   ↓
10. 校验 HasSelectedCustomer && SelectedCustomer.Id > 0
   ↓
11. 调用 LoadCustomerProductAsync() 加载该客户的产品
   ↓
12. 显示客户产品表数据
```

### 异常流程
```
用户未选择客户直接切换到"客户产品管理"Tab
   ↓
ActiveTab setter 调用 CheckAndLoadProduct()
   ↓
SelectedCustomer == null
   ↓
StatusMessage = "请先选择一个客户以查看其产品信息。"
   ↓
不加载产品数据，表为空
```

---

## 🎛️ ViewModel 属性映射

### 客户表相关
| ViewModel 属性 | 类型 | 绑定目标 | 说明 |
|---------------|------|--------|------|
| CurrentCustomerTable | CustomerList | DataGrid.ItemsSource | 客户集合容器 |
| SelectedCustomer | CustomerModel | DataGrid.SelectedItem | 当前选中客户 |
| HasSelectedCustomer | bool | Button.IsEnabled | 是否选中了有效客户 |
| SearchCustomerText | string | TextBox.Text | 客户搜索关键字 |
| PageCustomerTableIndex | int | PaginationControl.PageIndex | 客户表当前页码 |
| PageCustomerTableSize | int | PaginationControl.PageSize | 客户表每页行数 |
| TotalCustomerTableCount | int | PaginationControl.TotalCount | 客户表总记录数 |

### 产品表相关
| ViewModel 属性 | 类型 | 绑定目标 | 说明 |
|---------------|------|--------|------|
| CurrentProductTable | CustomerProductList | DataGrid.ItemsSource | 产品集合容器 |
| SelectedProduct | CustomerProductModel | DataGrid.SelectedItem | 当前选中产品 |
| SearchProductText | string | TextBox.Text | 产品搜索关键字 |
| PageProductTableIndex | int | PaginationControl.PageIndex | 产品表当前页码 |
| PageProductTableSize | int | PaginationControl.PageSize | 产品表每页行数 |
| TotalProductTableCount | int | PaginationControl.TotalCount | 产品表总记录数 |

### 控制相关
| ViewModel 属性 | 类型 | 绑定目标 | 说明 |
|---------------|------|--------|------|
| ActiveTab | CustomerManagerTabs | TabControl.SelectedIndex | 当前标签页（0=客户, 1=产品） |
| StatusMessage | string | TextBlock.Text | 底部状态提示信息 |

---

## 🔗 关键命令绑定

### 客户表命令
| 按钮 | 命令 | 前置条件 | 作用 |
|------|------|--------|------|
| 新增 | AddCustomerCommand | 无 | 添加新客户行（未保存） |
| 保存 | BatchSaveCustomerAsyncCommand | 有脏数据 | 批量保存客户数据 |
| 删除 | DeleteCustomerAsyncCommand | 有选中行 | 删除选中的客户 |
| 刷新 | LoadCustomerAsyncCommand | 无 | 重新加载客户数据 |
| 反选 | InvertCheckCommand | 无 | 反转所有行的复选框 |

### 产品表命令
| 按钮 | 命令 | 前置条件 | 作用 |
|------|------|--------|------|
| 新增 | AddProductCommand | **HasSelectedCustomer** ✅ | 为选中客户添加新产品行 |
| 保存 | BatchSaveProductAsyncCommand | **HasSelectedCustomer** ✅ | 批量保存产品数据 |
| 删除 | DeleteProductAsyncCommand | **HasSelectedCustomer** ✅ | 删除选中的产品 |
| 刷新 | CheckAndLoadProductCommand | **HasSelectedCustomer** ✅ | 重新加载产品数据 |
| 反选 | InvertCheckProductCommand | **HasSelectedCustomer** ✅ | 反转所有行的复选框 |

---

## 📋 客户表新增字段说明

| 字段名 | Header | 类型 | 宽度 | 用途 | 示例 |
|--------|--------|------|------|------|------|
| Incoterm | 贸易条款 | string | 90 | 国际贸易术语 | FOB / CIF / EXW |
| IncotermLocation | 贸易地点 | string | 100 | 贸易术语的具体地点 | Shenzhen / Shanghai |
| DepositRate | 定金比例 | decimal | 80 | 定金占比 | 30 (%) 或 0.3 |
| LeadTimeDays | 交货周期(天) | int | 100 | 交货或生产周期 | 30 / 45 / 60 |
| PaymentMethod | 付款方式 | string | 90 | 结算方式 | TT / LC / OA |
| Currency | 币种 | string | 70 | 交易货币 | USD / RMB / EUR |
| ShippedBy | 承运方式 | string | 90 | 运输方式 | DHL / UPS / 海运 |
| EtdSortRemark | 交期备注 | string | 120 | 预计出港时间文字描述 | Mid August / Q3 |
| IncotermSortRemark | 贸易条款备注 | string | 150 | 完整交易条件说明 | FOB Shenzhen, 30% deposit |

---

## ✅ 按钮状态矩阵

### 客户表按钮状态（始终可用）

| 场景 | 新增 | 保存 | 删除 | 刷新 | 反选 |
|------|------|------|------|------|------|
| 加载中 | ✅ | ✅ | ✅ | ✅ | ✅ |
| 无数据 | ✅ | ✅ | ❌ | ✅ | ✅ |
| 有脏数据 | ✅ | ✅ | ✅ | ✅ | ✅ |
| 有选中行 | ✅ | ✅ | ✅ | ✅ | ✅ |

### 产品表按钮状态（取决于 HasSelectedCustomer）

| 场景 | 新增 | 保存 | 删除 | 刷新 | 反选 |
|------|------|------|------|------|------|
| 未选客户 | ❌ | ❌ | ❌ | ❌ | ❌ |
| 已选客户，加载中 | ✅ | ✅ | ✅ | ✅ | ✅ |
| 已选客户，无数据 | ✅ | ✅ | ❌ | ✅ | ✅ |
| 已选客户，有脏数据 | ✅ | ✅ | ✅ | ✅ | ✅ |
| 已选客户，有选中行 | ✅ | ✅ | ✅ | ✅ | ✅ |

**说明**：
- ✅ = 按钮启用（可点击）
- ❌ = 按钮禁用（灰显，不可点击）

---

## 🐛 常见问题排查

### Q1: 点击"客户产品管理"按钮时提示"未选择客户"

**原因**: `SelectedCustomer == null`

**解决**:
1. 返回"客户列表"标签
2. 在表中点击选择一个客户行
3. 等待 `HasSelectedCustomer` 属性更新
4. 再次切换到"客户产品管理"标签

---

### Q2: 产品表显示的数据不对

**可能原因**:
1. ❌ 分页控件绑定错误 → ✅ 已修正为 `TotalProductTableCount`
2. ❌ 选中的客户不正确
3. ❌ API 返回错误数据

**排查**:
```csharp
// 在 LoadCustomerProductAsync() 中添加调试输出
Debug.WriteLine($"CustomerId: {SelectedCustomer.Id}");
Debug.WriteLine($"API Result: {result.Data?.Items.Count} products");
```

---

### Q3: 为什么产品表的按钮总是禁用？

**原因**: `HasSelectedCustomer` 为 false

**检查清单**:
1. ☑ 从"客户列表"标签选中了客户吗？
2. ☑ 选中的客户是否已保存（`Id > 0`）？
3. ☑ 是否看到 `StatusMessage` 显示错误信息？

---

## 💾 数据持久化流程

### 客户表保存流程
```
点击"保存"按钮
   ↓
GetChangeSet() 收集脏数据（Added/Updated/Deleted）
   ↓
验证: IsAllValid 检查所有行
   ↓
映射: Adapt<BatchRequestGeneric<CustomerDto>>()
   ↓
API: _customerApi.BatchSaveAsync(req)
   ↓
成功 → 显示"保存成功" → 重新加载数据
失败 → 显示"保存失败: {错误信息}" → 保持编辑状态
```

### 产品表保存流程
```
点击"保存"按钮
   ↓
校验: HasSelectedCustomer && SelectedCustomer.Id > 0
   ↓
GetChangeSet() 收集脏数据
   ↓
验证: IsAllValid 检查所有行
   ↓
映射: Adapt<BatchRequestGeneric<CustomerProductDto>>()
   ↓
API: _customerProductApi.BatchSaveAsync(req)
   ↓
成功 → 显示"保存成功" → CheckAndLoadProduct() 重新加载
失败 → 显示"保存失败: {错误信息}" → 保持编辑状态
```

---

## 📦 文件清单

| 文件 | 修改项 | 行数 |
|------|--------|------|
| CustomerManagerView.xaml | +9 DataGrid 列，修正分页绑定，+按钮 IsEnabled | ~50 |
| CustomerManagerViewModel.cs | +HasSelectedCustomer 属性，修改 SelectedCustomer setter | ~10 |

---

## 🚀 使用建议

### ✅ 最佳实践

1. **总是先选客户再操作产品**
   ```
   打开视图 → 选择客户 → 进入产品标签 → 操作产品
   ```

2. **批量操作时验证选中状态**
   ```
   删除前 → 确认选中行数 → 显示确认对话 → 执行删除
   ```

3. **异步操作期间禁用交互**
   ```
   使用 BusyScope() 管理 IsBusy 状态 → 按钮自动禁用
   ```

### ⚠️ 避免

- ❌ 不要在未选客户的情况下点击产品表按钮
- ❌ 不要手动修改 `SelectedCustomer` 属性
- ❌ 不要绕过 TabItem 的 ActiveTab 机制直接加载产品

---

