import { Image } from "antd";
import type { ICellRendererParams } from "ag-grid-community";
import type { OrderLine } from "../types";

export function ProductImageCell(props: ICellRendererParams<OrderLine, string>) {
  if (!props.value) return <span className="muted">未选择</span>;

  return (
    <div className="image-cell">
      <Image src={props.value} width={42} height={42} preview={false} />
    </div>
  );
}
