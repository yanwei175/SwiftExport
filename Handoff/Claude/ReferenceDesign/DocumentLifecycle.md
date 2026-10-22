# 单据生命周期与必备字段

## 1. 三类数据

| 类型 | 例 | 有状态机 | 影响库存 | 删除方式 |
|---|---|---|---|---|
| 主数据 | 产品、客户、供应商、仓库 | 否（只有启用/停用） | 否 | 被引用后只能停用 |
| 业务单据 | 采购单、销售单 | 是：草稿→确认→关闭/作废 | 否 | 草稿可删；确认后只能作废 |
| 库存单据 | 采购入库、出库、调拨、盘点 | 是：草稿→过账→冲销 | 是 | 草稿可删；过账后只能冲销 |

## 2. 状态机

### 业务单据（采购单）
```
		   Confirm()                 (下游全部收货，系统自动)
  Draft ─────────────► Confirmed ─────────────────────► Closed
	▲  ◄───────────────     │
	│   Unconfirm()         │ Close()（人工关闭，余量不再收）
	│  (无下游单据时)        ▼
	│                     Closed
	│ Void()（草稿或无下游的已确认单）
	└──────────────► Voided
  Delete()：仅 Draft，软删除
```

### 库存单据（采购入库）
```
  Draft ──Post()──► Posted ──Reverse()──► Reversed
	│
	└─Delete()：仅 Draft
```
- 过账写库存流水；**冲销不删除流水**，而是写一组数量相反的流水，并在原单上标记 Reversed。
- 冲销后如需重做，新建一张入库单（可“复制为新单”）。不允许 Reversed → Draft，避免同一单据号对应多套流水。

### 为什么“取消确认”允许回草稿，而“过账”只能冲销
- 确认只是业务承诺，未产生不可变的财务/库存事实，可以撤回。
- 过账产生库存流水，流水是审计事实，只能追加反向记录。

## 3. 必备字段

### 3.1 所有表（主数据与单据头、行）
| 字段 | 类型 | 说明 |
|---|---|---|
| `id` | uuid | 主键，`Guid.CreateVersion7()` |
| `version` | int | 乐观并发；每次 UPDATE `version = version + 1 WHERE version = @Version` |
| `created_at` / `created_by` | timestamptz / text | |
| `updated_at` / `updated_by` | timestamptz / text | |
| `is_deleted` | bool | 软删除（仅草稿或未被引用的主数据） |

### 3.2 单据头追加
| 字段 | 说明 |
|---|---|
| `document_no` | 单据号，唯一约束；创建时生成 |
| `document_date` | 业务日期（用户填写，可与创建时间不同） |
| `status` | smallint 枚举，见上状态机 |
| `remark` | 备注 |
| 状态时间戳 | 每个状态一组：`confirmed_at/by`、`posted_at/by`、`reversed_at/by`、`voided_at/by`、`closed_at/by` |
| `void_reason` / `reverse_reason` | 作废/冲销必须填原因 |
| 业务主体 | 如 `supplier_id` + `supplier_name`（**快照**：单据保存当时的名称，主数据改名不影响历史单据） |
| 合计 | 如 `total_amount`，由行汇总，保存时计算 |

### 3.3 单据行追加
| 字段 | 说明 |
|---|---|
| `header_id` | 所属单据 |
| `line_no` | 行号 |
| `product_id` + `product_code`/`product_name`/`unit` 快照 | 同上，保留历史 |
| `quantity`、`unit_price`、`amount` | numeric(18,4)/(18,2) |
| `source_line_id` | 下游单据引用上游行（入库行 → 采购行） |
| 进度字段（仅上游行） | 如采购行 `received_qty`，由下游过账/冲销更新 |

### 3.4 系统表
| 表 | 用途 |
|---|---|
| `command_receipt(command_id unique, actor_uid, command_type, request_target_uid, fingerprint_version, request_fingerprint, result_version, result_json, recorded_at_utc)`（现有表，沿用） | 命令重放 |
| `audit_log(id, entity_type, entity_id, action, user_id, at, detail_json)` | 业务审计 |
| `stock_balance(warehouse_id, product_id, quantity, version)` | 库存余额，唯一 (warehouse_id, product_id) |
| `stock_ledger(id, warehouse_id, product_id, qty_change, source_type, source_id, source_line_id, reversal_of_id, at, by)` | 库存流水，只追加 |

## 4. 每个状态允许的操作（权限 + 状态双重条件）

| 操作 | 权限码 | 允许的状态 | 额外条件 |
|---|---|---|---|
| 查看 | `X.View` | 全部 | |
| 新建/保存草稿 | `X.Edit` | Draft | |
| 删除 | `X.Edit` | Draft | |
| 确认 | `X.Confirm` | Draft | 行数>0，数量>0，主体有效 |
| 取消确认 | `X.Confirm` | Confirmed | 无有效下游单据 |
| 作废 | `X.Void` | Draft, Confirmed | 无有效下游单据 |
| 关闭 | `X.Close` | Confirmed | |
| 过账 | `X.Post` | Draft | 来源有效、数量不超可收量 |
| 冲销 | `X.Reverse` | Posted | 冲销后库存不为负 |

权限决定“这个人能不能做这类操作”（Api 层）；状态和额外条件决定“这张单现在能不能做”（Domain/Application 层）。两者互不替代。
