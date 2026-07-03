import { SearchOutlined } from "@ant-design/icons";
import { Avatar, Empty, Input, List, Popover, Space, Tag, Typography } from "antd";
import type { InputRef } from "antd";
import type { ICellRendererParams } from "ag-grid-community";
import { useMemo, useRef, useState } from "react";
import { products } from "../data/products";
import type { GridContext, OrderLine, ProductLookupItem } from "../types";

const { Text } = Typography;

type Props = ICellRendererParams<OrderLine, string> & {
  context: GridContext;
};

export function ProductLookupCell(props: Props) {
  const row = props.data;
  const [open, setOpen] = useState(false);
  const [keyword, setKeyword] = useState(props.value ?? "");
  const [activeIndex, setActiveIndex] = useState(0);
  const inputRef = useRef<InputRef>(null);

  const matches = useMemo(() => {
    const query = keyword.trim().toLowerCase();
    if (!query) return products;
    return products.filter((item) =>
      [item.sku, item.customerSku, item.name, item.color, item.supplier]
        .join(" ")
        .toLowerCase()
        .includes(query)
    );
  }, [keyword]);

  const commit = (item: ProductLookupItem) => {
    if (!row) return;
    props.context.updateLineProduct(row.id, item);
    setKeyword(item.sku);
    setOpen(false);
  };

  const handleKeyDown = (event: React.KeyboardEvent<HTMLInputElement>) => {
    if (!open && ["ArrowDown", "Enter"].includes(event.key)) {
      setOpen(true);
      return;
    }
    if (event.key === "ArrowDown") {
      event.preventDefault();
      setActiveIndex((value) => Math.min(value + 1, matches.length - 1));
    }
    if (event.key === "ArrowUp") {
      event.preventDefault();
      setActiveIndex((value) => Math.max(value - 1, 0));
    }
    if (event.key === "Enter" && matches[activeIndex]) {
      event.preventDefault();
      commit(matches[activeIndex]);
    }
    if (event.key === "Escape") {
      setOpen(false);
    }
  };

  const popup = (
    <div className="lookup-popup">
      <div className="lookup-hint">输入 SKU、客户编码、名称或供应商，按 Enter 回填整行</div>
      {matches.length === 0 ? (
        <Empty image={Empty.PRESENTED_IMAGE_SIMPLE} description="没有匹配产品" />
      ) : (
        <List
          dataSource={matches}
          renderItem={(item, index) => (
            <List.Item
              className={index === activeIndex ? "lookup-item active" : "lookup-item"}
              onMouseEnter={() => setActiveIndex(index)}
              onMouseDown={(event) => {
                event.preventDefault();
                commit(item);
              }}
            >
              <List.Item.Meta
                avatar={<Avatar shape="square" size={54} src={item.imageUrl} />}
                title={
                  <Space size={8}>
                    <Text strong>{item.sku}</Text>
                    <Tag>{item.customerSku}</Tag>
                    <Tag color={item.stockQty > 500 ? "green" : "orange"}>库存 {item.stockQty}</Tag>
                  </Space>
                }
                description={
                  <div className="lookup-description">
                    <span>{item.name}</span>
                    <span>{item.color}</span>
                    <span>{item.supplier}</span>
                    <span>USD {item.defaultPrice}</span>
                  </div>
                }
              />
            </List.Item>
          )}
        />
      )}
    </div>
  );

  return (
    <Popover
      content={popup}
      open={open}
      trigger="click"
      placement="bottomLeft"
      overlayClassName="lookup-popover"
      onOpenChange={(nextOpen) => {
        setOpen(nextOpen);
        if (nextOpen) requestAnimationFrame(() => inputRef.current?.focus());
      }}
    >
      <Input
        ref={inputRef}
        className="lookup-input"
        value={keyword}
        prefix={<SearchOutlined />}
        onFocus={() => setOpen(true)}
        onChange={(event) => {
          setKeyword(event.target.value);
          setOpen(true);
          setActiveIndex(0);
        }}
        onKeyDown={handleKeyDown}
      />
    </Popover>
  );
}
