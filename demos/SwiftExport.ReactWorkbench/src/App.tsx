import {
  DeleteOutlined,
  PlusOutlined,
  SaveOutlined,
  SyncOutlined,
  WarningOutlined
} from "@ant-design/icons";
import { AgGridReact } from "ag-grid-react";
import type {
  CellValueChangedEvent,
  ColDef,
  GetRowIdParams,
  RowClassParams,
  RowSelectionOptions,
  ValueFormatterParams
} from "ag-grid-community";
import {
  Alert,
  Badge,
  Button,
  Card,
  Descriptions,
  Drawer,
  Flex,
  Form,
  Input,
  Layout,
  Menu,
  Select,
  Space,
  Statistic,
  Tag,
  Typography,
  message
} from "antd";
import { useMemo, useRef, useState } from "react";
import { ProductImageCell } from "./components/ProductImageCell";
import { ProductLookupCell } from "./components/ProductLookupCell";
import { createAmount, initialOrderLines } from "./data/orderLines";
import { ProductManagementPage } from "./pages/ProductManagementPage";
import type { GridContext, OrderLine, ProductLookupItem } from "./types";

const { Header, Sider, Content } = Layout;
const { Title, Text } = Typography;

const moneyFormatter = (params: ValueFormatterParams<OrderLine, number>) =>
  typeof params.value === "number" ? params.value.toFixed(2) : "";

const rowSelection: RowSelectionOptions = {
  mode: "multiRow",
  checkboxes: true,
  headerCheckbox: true
};

function createEmptyLine(index: number): OrderLine {
  return {
    id: `new-${Date.now()}-${index}`,
    version: 0,
    sku: "",
    customerSku: "",
    productName: "",
    productImageUrl: "",
    color: "",
    unit: "PCS",
    qty: 1,
    unitPrice: 0,
    amount: 0,
    deliveryDate: "2026-07-30",
    remark: "",
    lineState: "created",
    errors: ["请选择产品"]
  };
}

function markTouched(line: OrderLine): OrderLine {
  if (line.lineState === "created") return line;
  return { ...line, lineState: "updated" };
}

function validateLine(line: OrderLine): string[] {
  const errors: string[] = [];
  if (!line.productUid) errors.push("产品必选");
  if (!line.sku) errors.push("SKU 必填");
  if (!line.qty || line.qty <= 0) errors.push("数量必须大于 0");
  if (line.unitPrice < 0) errors.push("单价不能小于 0");
  if (!line.deliveryDate) errors.push("交期必填");
  return errors;
}

export default function App() {
  const [activePage, setActivePage] = useState<"sales-order" | "products">("sales-order");
  const [lines, setLines] = useState<OrderLine[]>(initialOrderLines);
  const [deletedLines, setDeletedLines] = useState<OrderLine[]>([]);
  const [drawerOpen, setDrawerOpen] = useState(false);
  const gridRef = useRef<AgGridReact<OrderLine>>(null);

  const updateLineProduct = (lineId: string, product: ProductLookupItem) => {
    setLines((current) =>
      current.map((line) => {
        if (line.id !== lineId) return line;
        const next: OrderLine = {
          ...markTouched(line),
          productUid: product.uid,
          sku: product.sku,
          customerSku: product.customerSku,
          productName: product.name,
          productImageUrl: product.imageUrl,
          color: product.color,
          unit: product.unit,
          unitPrice: product.defaultPrice,
          amount: createAmount(line.qty, product.defaultPrice)
        };
        return { ...next, errors: validateLine(next) };
      })
    );
  };

  const context = useMemo<GridContext>(() => ({ updateLineProduct }), []);

  const columnDefs = useMemo<ColDef<OrderLine>[]>(
    () => [
      {
        headerName: "产品图片",
        field: "productImageUrl",
        width: 92,
        editable: false,
        cellRenderer: ProductImageCell
      },
      {
        headerName: "SKU / Lookup",
        field: "sku",
        width: 220,
        editable: false,
        cellRenderer: ProductLookupCell
      },
      { headerName: "客户编码", field: "customerSku", width: 130, editable: false },
      { headerName: "产品名称", field: "productName", width: 210, editable: false },
      { headerName: "颜色", field: "color", width: 130, editable: false },
      {
        headerName: "数量",
        field: "qty",
        width: 110,
        editable: true,
        cellEditor: "agNumberCellEditor",
        valueParser: (params) => Number(params.newValue)
      },
      { headerName: "单位", field: "unit", width: 90, editable: false },
      {
        headerName: "单价",
        field: "unitPrice",
        width: 110,
        editable: true,
        cellEditor: "agNumberCellEditor",
        valueParser: (params) => Number(params.newValue),
        valueFormatter: moneyFormatter
      },
      {
        headerName: "金额",
        field: "amount",
        width: 120,
        editable: false,
        valueFormatter: moneyFormatter
      },
      {
        headerName: "交期",
        field: "deliveryDate",
        width: 136,
        editable: true
      },
      {
        headerName: "备注",
        field: "remark",
        flex: 1,
        minWidth: 180,
        editable: true
      },
      {
        headerName: "状态",
        field: "lineState",
        width: 100,
        editable: false,
        cellRenderer: (params: { value: OrderLine["lineState"] }) => {
          const map = {
            clean: <Tag>未修改</Tag>,
            created: <Tag color="blue">新增</Tag>,
            updated: <Tag color="orange">已修改</Tag>,
            deleted: <Tag color="red">删除</Tag>
          };
          return map[params.value];
        }
      }
    ],
    []
  );

  const defaultColDef = useMemo<ColDef<OrderLine>>(
    () => ({
      sortable: true,
      resizable: true,
      filter: true,
      singleClickEdit: true
    }),
    []
  );

  const totals = useMemo(() => {
    const qty = lines.reduce((sum, line) => sum + Number(line.qty || 0), 0);
    const amount = lines.reduce((sum, line) => sum + Number(line.amount || 0), 0);
    const changed = lines.filter((line) => line.lineState !== "clean").length + deletedLines.length;
    const errors = lines.flatMap((line, index) => line.errors.map((error) => `第 ${index + 1} 行：${error}`));
    return { qty, amount, changed, errors };
  }, [lines, deletedLines]);

  const changeSet = useMemo(
    () => ({
      headUid: "so-20260702-001",
      version: 7,
      lines: {
        created: lines.filter((line) => line.lineState === "created"),
        updated: lines.filter((line) => line.lineState === "updated"),
        deleted: deletedLines.map((line) => ({ uid: line.id, version: line.version }))
      }
    }),
    [lines, deletedLines]
  );

  const onCellValueChanged = (event: CellValueChangedEvent<OrderLine>) => {
    if (!event.data || event.oldValue === event.newValue) return;
    setLines((current) =>
      current.map((line) => {
        if (line.id !== event.data?.id) return line;
        const next = markTouched({
          ...line,
          [event.colDef.field ?? ""]: event.newValue,
          amount:
            event.colDef.field === "qty" || event.colDef.field === "unitPrice"
              ? createAmount(
                  event.colDef.field === "qty" ? Number(event.newValue) : line.qty,
                  event.colDef.field === "unitPrice" ? Number(event.newValue) : line.unitPrice
                )
              : line.amount
        });
        return { ...next, errors: validateLine(next) };
      })
    );
  };

  const addLine = () => {
    setLines((current) => [...current, createEmptyLine(current.length + 1)]);
    message.success("已新增一行，请在 SKU 单元格搜索产品");
  };

  const deleteSelected = () => {
    const selectedRows = gridRef.current?.api.getSelectedRows() ?? [];
    if (selectedRows.length === 0) {
      message.warning("请先勾选要删除的行");
      return;
    }
    const selectedIds = new Set(selectedRows.map((line) => line.id));
    setDeletedLines((current) => [
      ...current,
      ...selectedRows.filter((line) => line.lineState !== "created")
    ]);
    setLines((current) => current.filter((line) => !selectedIds.has(line.id)));
  };

  const validateAll = () => {
    setLines((current) => current.map((line) => ({ ...line, errors: validateLine(line) })));
    if (totals.errors.length > 0) {
      message.warning("仍有行校验未通过");
    } else {
      message.success("校验通过");
    }
  };

  const getRowClass = (params: RowClassParams<OrderLine>) => {
    if (!params.data) return "";
    if (params.data.errors.length > 0) return "row-invalid";
    if (params.data.lineState !== "clean") return "row-dirty";
    return "";
  };

  return (
    <Layout className="app-shell">
      <Sider width={208} className="sidebar">
        <div className="brand">SwiftExport</div>
        <Menu
          theme="dark"
          mode="inline"
          selectedKeys={[activePage]}
          onClick={({ key }) => {
            if (key === "sales-order" || key === "products") setActivePage(key);
          }}
          items={[
            { key: "sales-order", label: "销售订单" },
            { key: "purchase-order", label: "采购订单" },
            { key: "receive", label: "入库单" },
            { key: "packing-list", label: "Packing List" },
            { key: "products", label: "产品管理" },
            { key: "master", label: "基础资料" }
          ]}
        />
      </Sider>
      <Layout>
        <Header className="topbar">
          <div>
            <Text type="secondary">React + Ant Design + AG Grid Community 验证台</Text>
            <Title level={3}>{activePage === "products" ? "产品管理" : "销售订单 SO-20260702-001"}</Title>
          </div>
          <Space>
            <Badge count={totals.changed} offset={[6, 0]}>
              <Tag color={totals.changed > 0 ? "orange" : "default"}>变更</Tag>
            </Badge>
            <Tag color="gold">草稿</Tag>
          </Space>
        </Header>
        <Content className="workspace">
          {activePage === "products" ? (
            <ProductManagementPage />
          ) : (
            <>
          <Card className="head-card">
            <Form layout="vertical">
              <Flex gap={12} wrap>
                <Form.Item label="客户">
                  <Input value="PACIFIC HOME INC." readOnly />
                </Form.Item>
                <Form.Item label="订单日期">
                  <Input value="2026-07-02" readOnly />
                </Form.Item>
                <Form.Item label="币种">
                  <Select value="USD" options={[{ value: "USD" }, { value: "CNY" }, { value: "HKD" }]} />
                </Form.Item>
                <Form.Item label="备注" className="wide-field">
                  <Input value="验证单据行 lookup cell：搜索产品、展示图片、回填整行" readOnly />
                </Form.Item>
              </Flex>
            </Form>
          </Card>

          <Flex className="toolbar" justify="space-between" align="center">
            <Space>
              <Button icon={<PlusOutlined />} onClick={addLine}>
                新增行
              </Button>
              <Button icon={<DeleteOutlined />} onClick={deleteSelected}>
                删除勾选
              </Button>
              <Button icon={<SyncOutlined />} onClick={validateAll}>
                校验
              </Button>
              <Button type="primary" icon={<SaveOutlined />} onClick={() => setDrawerOpen(true)}>
                保存预览
              </Button>
            </Space>
            <Space size={18}>
              <Statistic title="数量" value={totals.qty} suffix="PCS" />
              <Statistic title="金额" value={totals.amount} precision={2} prefix="USD" />
            </Space>
          </Flex>

          {totals.errors.length > 0 && (
            <Alert
              className="validation-alert"
              type="warning"
              showIcon
              icon={<WarningOutlined />}
              message="当前单据仍有行校验问题"
              description={totals.errors.slice(0, 3).join("；")}
            />
          )}

          <div className="grid-card ag-theme-quartz">
            <AgGridReact<OrderLine>
              ref={gridRef}
              rowData={lines}
              columnDefs={columnDefs}
              defaultColDef={defaultColDef}
              rowHeight={62}
              headerHeight={38}
              theme="legacy"
              rowSelection={rowSelection}
              context={context}
              getRowId={(params: GetRowIdParams<OrderLine>) => params.data.id}
              getRowClass={getRowClass}
              onCellValueChanged={onCellValueChanged}
              stopEditingWhenCellsLoseFocus
              suppressDragLeaveHidesColumns
            />
          </div>
            </>
          )}
        </Content>
      </Layout>

      <Drawer
        title="批量保存变更集预览"
        width={620}
        open={drawerOpen}
        onClose={() => setDrawerOpen(false)}
      >
        <Descriptions bordered size="small" column={1}>
          <Descriptions.Item label="新增行">{changeSet.lines.created.length}</Descriptions.Item>
          <Descriptions.Item label="修改行">{changeSet.lines.updated.length}</Descriptions.Item>
          <Descriptions.Item label="删除行">{changeSet.lines.deleted.length}</Descriptions.Item>
        </Descriptions>
        <pre className="change-set">{JSON.stringify(changeSet, null, 2)}</pre>
      </Drawer>
    </Layout>
  );
}
