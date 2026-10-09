import { useRef } from "react";
import { constrainGridColumnWidth } from "./单元格区域";

/** 列宽拖动只改变显示，公开 separator 支持键盘，使用原生指针捕获保留拖动。 */
export function ColumnResizeHandle({
  label,
  width,
  onResize,
  onFinish,
}: {
  label: string;
  width: number;
  onResize(width: number): void;
  onFinish(width: number): void;
}) {
  const drag = useRef<{
    pointerId: number;
    startX: number;
    width: number;
    currentWidth: number;
  } | null>(null);
  return (
    <span
      className="swift-grid-resize"
      role="separator"
      tabIndex={0}
      aria-label={`调整${label}列宽`}
      aria-orientation="vertical"
      aria-valuemin={48}
      aria-valuemax={1000}
      aria-valuenow={width}
      onClick={(event) => event.stopPropagation()}
      onPointerDown={(event) => {
        if (event.button !== 0) return;
        event.preventDefault();
        event.stopPropagation();
        drag.current = {
          pointerId: event.pointerId,
          startX: event.clientX,
          width,
          currentWidth: width,
        };
        event.currentTarget.setPointerCapture(event.pointerId);
        event.currentTarget.focus();
      }}
      onPointerMove={(event) => {
        const current = drag.current;
        if (!current || current.pointerId !== event.pointerId) return;
        event.preventDefault();
        event.stopPropagation();
        current.currentWidth = constrainGridColumnWidth(
          current.width + event.clientX - current.startX,
        );
        onResize(current.currentWidth);
      }}
      onPointerUp={(event) => {
        const current = drag.current;
        if (!current || current.pointerId !== event.pointerId) return;
        event.stopPropagation();
        drag.current = null;
        onFinish(current.currentWidth);
        if (event.currentTarget.hasPointerCapture(event.pointerId))
          event.currentTarget.releasePointerCapture(event.pointerId);
      }}
      onPointerCancel={() => {
        if (drag.current) onResize(drag.current.width);
        drag.current = null;
      }}
      onLostPointerCapture={() => {
        if (drag.current) onResize(drag.current.width);
        drag.current = null;
      }}
      onKeyDown={(event) => {
        const next =
          event.key === "ArrowLeft"
            ? width - 10
            : event.key === "ArrowRight"
              ? width + 10
              : event.key === "Home"
                ? 48
                : event.key === "End"
                  ? 1000
                  : null;
        if (next === null) return;
        event.preventDefault();
        event.stopPropagation();
        const accepted = constrainGridColumnWidth(next);
        onResize(accepted);
        onFinish(accepted);
      }}
    />
  );
}
