import { describe, expect, it } from "vitest";
import {
  acceptEditableRow,
  createEditableRow,
  discardEditableRows,
  isRowDirty,
  updateEditableRow,
} from "./编辑集合";

describe("编辑集合的基线与当前值", () => {
  it("修改再还原恢复 clean，基线不会被当前值污染", () => {
    const row = createEditableRow("a", "uid-a", 1, {
      name: "原值",
      quantity: 1,
    });
    const changed = updateEditableRow([row], "a", { quantity: 2 });
    expect(changed[0]?.baseline.quantity).toBe(1);
    expect(changed[0] && isRowDirty(changed[0])).toBe(true);
    const restored = updateEditableRow(changed, "a", { quantity: 1 });
    expect(restored[0] && isRowDirty(restored[0])).toBe(false);
    expect(row.current.quantity).toBe(1);
  });

  it("部分接纳只提高确认成功行版本，失败行修改仍然保留", () => {
    const rows = [
      createEditableRow("a", "uid-a", 1, { name: "A", quantity: 1 }),
      createEditableRow("b", "uid-b", 1, { name: "B", quantity: 1 }),
    ];
    const changed = updateEditableRow(
      updateEditableRow(rows, "a", { quantity: 2 }),
      "b",
      { quantity: 3 },
    );
    const accepted = acceptEditableRow(changed, "a", "uid-a", 2, {
      name: "A",
      quantity: 2,
    });
    expect(accepted[0]?.version).toBe(2);
    expect(accepted[0] && isRowDirty(accepted[0])).toBe(false);
    expect(accepted[1]?.version).toBe(1);
    expect(accepted[1] && isRowDirty(accepted[1])).toBe(true);
    expect(discardEditableRows(accepted)[1]?.current.quantity).toBe(1);
  });
});
