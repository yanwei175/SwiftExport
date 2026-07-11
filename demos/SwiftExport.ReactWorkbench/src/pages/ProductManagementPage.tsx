import {
  EyeOutlined,
  ReloadOutlined,
  SearchOutlined,
  WarningOutlined
} from "@ant-design/icons";
import { AgGridReact } from "ag-grid-react";
import type { ColDef, GetRowIdParams, ICellRendererParams } from "ag-grid-community";
import {
  Alert,
  Button,
  Card,
  Descriptions,
  Drawer,
  Empty,
  Flex,
  Form,
  Image,
  Input,
  InputNumber,
  Space,
  Tag,
  Typography,
  message
} from "antd";
import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { getPagedItems, getProductById, getTotalCount, searchProducts } from "../api/products";
import type { PagedResultDto, ProductDto } from "../types";
import {
  productBomNo,
  productBomVersion,
  productCategoryName,
  productCbmOfCarton,
  productCnyPrice,
  productDefaultCustomerCode,
  productDefaultCustomerSku,
  productDefaultSupplierCode,
  productDefaultSupplierSku,
  productDescriptionEn,
  productDescriptionZh,
  productFinish,
  productGwOfCarton,
  productMainImage,
  productMasterSku,
  productMaterialType,
  productNote,
  productNwOfCarton,
  productPcsOfCarton,
  productSku,
  productSupplierName,
  productSupplyType,
  productUid,
  productUsdPrice,
  productVersion
} from "../utils/productFields";

const { Title, Text } = Typography;

function ProductImageRenderer(params: ICellRendererParams<ProductDto>) {
  const url = params.data ? productMainImage(params.data) : "";
  if (!url) return <span className="muted">无图</span>;
  return <Image src={url} width={42} height={42} preview={false} fallback="/assets/products/basin-faucet.png" />;
}

function PriceRenderer(params: ICellRendererParams<ProductDto, number>) {
  if (typeof params.value !== "number") return "";
  return params.value.toFixed(2);
}

export function ProductManagementPage() {
  const [keyword, setKeyword] = useState("");
  const [categoryName, setCategoryName] = useState("");
  const [materialType, setMaterialType] = useState("");
  const [pageIndex, setPageIndex] = useState(1);
  const [pageSize, setPageSize] = useState(50);
  const [page, setPage] = useState<PagedResultDto<ProductDto> | null>(null);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState("");
  const [selectedProduct, setSelectedProduct] = useState<ProductDto | null>(null);
  const [drawerOpen, setDrawerOpen] = useState(false);
  const gridRef = useRef<AgGridReact<ProductDto>>(null);

  const products = page ? getPagedItems(page) : [];
  const totalCount = page ? getTotalCount(page) : 0;

  const loadProducts = useCallback(async () => {
    setLoading(true);
    setError("");
    try {
      const result = await searchProducts({
        Keyword: keyword || undefined,
        PageIndex: pageIndex,
        PageSize: pageSize,
        CategoryName: categoryName || undefined,
        MaterialType: materialType || undefined
      });
      setPage(result);
    } catch (err) {
      const messageText = err instanceof Error ? err.message : "产品 API 请求失败";
      setError(messageText);
      setPage(null);
    } finally {
      setLoading(false);
    }
  }, [categoryName, keyword, materialType, pageIndex, pageSize]);

  useEffect(() => {
    void loadProducts();
  }, [loadProducts]);

  const openDetail = async (row: ProductDto) => {
    const uid = productUid(row);
    if (!uid) return;
    try {
      setLoading(true);
      const detail = await getProductById(uid);
      setSelectedProduct(detail);
      setDrawerOpen(true);
    } catch (err) {
      message.error(err instanceof Error ? err.message : "读取产品详情失败");
    } finally {
      setLoading(false);
    }
  };

  const columnDefs = useMemo<ColDef<ProductDto>[]>(
    () => [
      {
        headerName: "图片",
        width: 90,
        cellRenderer: ProductImageRenderer
      },
      {
        headerName: "SKU",
        width: 150,
        valueGetter: ({ data }) => (data ? productSku(data) : "")
      },
      {
        headerName: "Master SKU",
        width: 150,
        valueGetter: ({ data }) => (data ? productMasterSku(data) : "")
      },
      {
        headerName: "英文描述",
        minWidth: 220,
        flex: 1,
        valueGetter: ({ data }) => (data ? productDescriptionEn(data) : "")
      },
      {
        headerName: "中文描述",
        minWidth: 180,
        flex: 1,
        valueGetter: ({ data }) => (data ? productDescriptionZh(data) : "")
      },
      {
        headerName: "表面",
        width: 130,
        valueGetter: ({ data }) => (data ? productFinish(data) : "")
      },
      {
        headerName: "物料类型",
        width: 120,
        valueGetter: ({ data }) => (data ? productMaterialType(data) : "")
      },
      {
        headerName: "供应方式",
        width: 120,
        valueGetter: ({ data }) => (data ? productSupplyType(data) : "")
      },
      {
        headerName: "分类",
        width: 150,
        valueGetter: ({ data }) => (data ? productCategoryName(data) : "")
      },
      {
        headerName: "供应商",
        width: 160,
        valueGetter: ({ data }) => (data ? productSupplierName(data) : "")
      },
      {
        headerName: "USD",
        width: 100,
        valueGetter: ({ data }) => (data ? productUsdPrice(data) : 0),
        cellRenderer: PriceRenderer
      },
      {
        headerName: "CNY",
        width: 100,
        valueGetter: ({ data }) => (data ? productCnyPrice(data) : 0),
        cellRenderer: PriceRenderer
      },
      {
        headerName: "BOM",
        width: 95,
        cellRenderer: (params: ICellRendererParams<ProductDto>) => {
          const version = params.data ? productBomVersion(params.data) : 0;
          return version > 0 ? <Tag color="blue">V{version}</Tag> : <Tag>无</Tag>;
        }
      },
      {
        headerName: "操作",
        width: 110,
        pinned: "right",
        cellRenderer: (params: ICellRendererParams<ProductDto>) => (
          <Button size="small" icon={<EyeOutlined />} onClick={() => params.data && openDetail(params.data)}>
            详情
          </Button>
        )
      }
    ],
    []
  );

  const defaultColDef = useMemo<ColDef<ProductDto>>(
    () => ({
      sortable: true,
      resizable: true,
      filter: true
    }),
    []
  );

  const canPrev = pageIndex > 1;
  const canNext = pageIndex * pageSize < totalCount;

  return (
    <>
      <Card className="page-title-card">
        <Flex justify="space-between" gap={16} align="center" wrap>
          <div>
            <Text type="secondary">真实 API：POST /api/Product/search</Text>
            <Title level={3}>产品管理</Title>
          </div>
          <Space>
            <Tag color={error ? "red" : "green"}>{error ? "API 未连接" : "API 已连接"}</Tag>
            <Button icon={<ReloadOutlined />} loading={loading} onClick={loadProducts}>
              刷新
            </Button>
          </Space>
        </Flex>
      </Card>

      <Card className="filter-card">
        <Form layout="vertical">
          <Flex gap={12} wrap align="end">
            <Form.Item label="关键词">
              <Input
                allowClear
                prefix={<SearchOutlined />}
                placeholder="SKU / 描述 / 客户编码"
                value={keyword}
                onChange={(event) => setKeyword(event.target.value)}
                onPressEnter={() => {
                  setPageIndex(1);
                  void loadProducts();
                }}
              />
            </Form.Item>
            <Form.Item label="分类名称">
              <Input allowClear value={categoryName} onChange={(event) => setCategoryName(event.target.value)} />
            </Form.Item>
            <Form.Item label="物料类型">
              <Input allowClear value={materialType} onChange={(event) => setMaterialType(event.target.value)} />
            </Form.Item>
            <Form.Item label="每页">
              <InputNumber min={10} max={500} value={pageSize} onChange={(value) => setPageSize(Number(value ?? 50))} />
            </Form.Item>
            <Button
              type="primary"
              icon={<SearchOutlined />}
              loading={loading}
              onClick={() => {
                setPageIndex(1);
                void loadProducts();
              }}
            >
              查询
            </Button>
          </Flex>
        </Form>
      </Card>

      {error && (
        <Alert
          className="validation-alert"
          type="warning"
          showIcon
          icon={<WarningOutlined />}
          message="无法连接真实产品 API"
          description={`请确认后端已运行在 https://localhost:7286，且 Vite 代理已生效。错误：${error}`}
        />
      )}

      <Flex className="toolbar" justify="space-between" align="center">
        <Space>
          <Text>总数：{totalCount}</Text>
          <Text>当前页：{pageIndex}</Text>
        </Space>
        <Space>
          <Button disabled={!canPrev || loading} onClick={() => setPageIndex((value) => Math.max(value - 1, 1))}>
            上一页
          </Button>
          <Button disabled={!canNext || loading} onClick={() => setPageIndex((value) => value + 1)}>
            下一页
          </Button>
        </Space>
      </Flex>

      <div className="grid-card product-grid-card ag-theme-quartz">
        {error && products.length === 0 ? (
          <div className="empty-state">
            <Empty description="后端连接成功后这里会显示真实产品数据" />
          </div>
        ) : (
          <AgGridReact<ProductDto>
            ref={gridRef}
            rowData={products}
            columnDefs={columnDefs}
            defaultColDef={defaultColDef}
            rowHeight={60}
            headerHeight={38}
            theme="legacy"
            loading={loading}
            getRowId={(params: GetRowIdParams<ProductDto>) => productUid(params.data)}
            suppressDragLeaveHidesColumns
          />
        )}
      </div>

      <Drawer title="产品详情" width={680} open={drawerOpen} onClose={() => setDrawerOpen(false)}>
        {selectedProduct && (
          <Descriptions bordered size="small" column={1}>
            <Descriptions.Item label="Uid">{productUid(selectedProduct)}</Descriptions.Item>
            <Descriptions.Item label="Version">{productVersion(selectedProduct)}</Descriptions.Item>
            <Descriptions.Item label="SKU">{productSku(selectedProduct)}</Descriptions.Item>
            <Descriptions.Item label="Master SKU">{productMasterSku(selectedProduct)}</Descriptions.Item>
            <Descriptions.Item label="英文描述">{productDescriptionEn(selectedProduct)}</Descriptions.Item>
            <Descriptions.Item label="中文描述">{productDescriptionZh(selectedProduct)}</Descriptions.Item>
            <Descriptions.Item label="表面">{productFinish(selectedProduct)}</Descriptions.Item>
            <Descriptions.Item label="物料类型">{productMaterialType(selectedProduct)}</Descriptions.Item>
            <Descriptions.Item label="供应方式">{productSupplyType(selectedProduct)}</Descriptions.Item>
            <Descriptions.Item label="分类">{productCategoryName(selectedProduct)}</Descriptions.Item>
            <Descriptions.Item label="供应商">{productSupplierName(selectedProduct)}</Descriptions.Item>
            <Descriptions.Item label="客户代码">{productDefaultCustomerCode(selectedProduct)}</Descriptions.Item>
            <Descriptions.Item label="客户 SKU">{productDefaultCustomerSku(selectedProduct)}</Descriptions.Item>
            <Descriptions.Item label="供应商代码">{productDefaultSupplierCode(selectedProduct)}</Descriptions.Item>
            <Descriptions.Item label="供应商 SKU">{productDefaultSupplierSku(selectedProduct)}</Descriptions.Item>
            <Descriptions.Item label="USD">{productUsdPrice(selectedProduct)}</Descriptions.Item>
            <Descriptions.Item label="CNY">{productCnyPrice(selectedProduct)}</Descriptions.Item>
            <Descriptions.Item label="装箱 PCS">{productPcsOfCarton(selectedProduct) ?? ""}</Descriptions.Item>
            <Descriptions.Item label="NW/GW/CBM">
              {productNwOfCarton(selectedProduct) ?? ""} / {productGwOfCarton(selectedProduct) ?? ""} /{" "}
              {productCbmOfCarton(selectedProduct) ?? ""}
            </Descriptions.Item>
            <Descriptions.Item label="BOM">
              {productBomVersion(selectedProduct) > 0
                ? `${productBomNo(selectedProduct)} / V${productBomVersion(selectedProduct)}`
                : "无"}
            </Descriptions.Item>
            <Descriptions.Item label="备注">{productNote(selectedProduct)}</Descriptions.Item>
          </Descriptions>
        )}
      </Drawer>
    </>
  );
}
