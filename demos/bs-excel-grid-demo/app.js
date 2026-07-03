const columns = [
  { field: "sku", label: "SKU", editable: true, required: true },
  { field: "productName", label: "产品名称", editable: true, required: true },
  { field: "color", label: "颜色", editable: true },
  { field: "qty", label: "数量", editable: true, number: true },
  { field: "unitPrice", label: "单价", editable: true, number: true },
  { field: "amount", label: "金额", editable: false, number: true },
  { field: "deliveryDate", label: "交期", editable: true, date: true },
  { field: "remark", label: "备注", editable: true }
];

const originalRows = [
  { uid: "line-001", sku: "CP-1001", productName: "Brass Basin Faucet", color: "Chrome", qty: 120, unitPrice: 18.5, deliveryDate: "2026-07-18", remark: "客户标准包装" },
  { uid: "line-002", sku: "CP-1002", productName: "Kitchen Pull-Out Faucet", color: "Matte Black", qty: 80, unitPrice: 31.2, deliveryDate: "2026-07-20", remark: "" },
  { uid: "line-003", sku: "CP-1003", productName: "Shower Mixer", color: "Brushed Nickel", qty: 60, unitPrice: 42, deliveryDate: "2026-07-22", remark: "需确认贴标" },
  { uid: "line-004", sku: "CP-1004", productName: "Angle Valve", color: "Chrome", qty: 300, unitPrice: 4.8, deliveryDate: "2026-07-23", remark: "" },
  { uid: "line-005", sku: "CP-1005", productName: "Pop-up Waste", color: "Gold", qty: 150, unitPrice: 7.6, deliveryDate: "2026-07-26", remark: "随柜出货" }
];

let rows = originalRows.map(createRow);
let deletedRows = [];
let selected = null;
let anchor = null;
let dragging = false;
let undoStack = [];
let redoStack = [];
let newId = 1;

const tbody = document.querySelector("#orderGrid tbody");
const selectionInfo = document.querySelector("#selectionInfo");
const summaryInfo = document.querySelector("#summaryInfo");
const dirtyBadge = document.querySelector("#dirtyBadge");
const changePreview = document.querySelector("#changePreview");
const validationList = document.querySelector("#validationList");

function createRow(source) {
  const row = structuredClone(source);
  row.amount = roundMoney(Number(row.qty || 0) * Number(row.unitPrice || 0));
  row._original = structuredClone(row);
  row._state = "unchanged";
  row._checked = false;
  row._errors = {};
  return row;
}

function render() {
  tbody.innerHTML = "";
  rows.forEach((row, rowIndex) => {
    const tr = document.createElement("tr");
    tr.dataset.row = String(rowIndex);
    if (row._state === "deleted") tr.classList.add("deleted");

    const checkCell = document.createElement("td");
    checkCell.className = "check-cell";
    const check = document.createElement("input");
    check.type = "checkbox";
    check.checked = row._checked;
    check.addEventListener("change", () => {
      row._checked = check.checked;
      updateChrome();
    });
    checkCell.append(check);
    tr.append(checkCell);

    const rowNumber = document.createElement("td");
    rowNumber.className = "row-number";
    rowNumber.textContent = String(rowIndex + 1);
    tr.append(rowNumber);

    columns.forEach((column, colIndex) => {
      const td = document.createElement("td");
      td.dataset.row = String(rowIndex);
      td.dataset.col = String(colIndex);
      td.dataset.field = column.field;
      td.textContent = formatCell(row[column.field], column);
      td.className = column.editable ? "editable" : "locked";
      if (column.field === "amount") td.classList.add("amount");
      if (isCellDirty(row, column.field)) td.classList.add("dirty");
      if (row._errors[column.field]) td.classList.add("invalid");
      if (isInSelection(rowIndex, colIndex)) td.classList.add(isSelectedCell(rowIndex, colIndex) ? "selected" : "range");

      if (column.editable) {
        td.contentEditable = "true";
        td.spellcheck = false;
        td.addEventListener("focus", () => selectCell(rowIndex, colIndex, false));
        td.addEventListener("mousedown", event => startSelect(event, rowIndex, colIndex));
        td.addEventListener("mouseenter", () => extendDrag(rowIndex, colIndex));
        td.addEventListener("blur", () => commitCell(td, rowIndex, colIndex));
        td.addEventListener("keydown", event => handleCellKeydown(event, td, rowIndex, colIndex));
      } else {
        td.addEventListener("mousedown", event => startSelect(event, rowIndex, colIndex));
      }

      tr.append(td);
    });

    tbody.append(tr);
  });
  updateChrome();
}

function startSelect(event, rowIndex, colIndex) {
  if (event.button !== 0) return;
  dragging = true;
  if (event.shiftKey && anchor) {
    selected = { startRow: anchor.row, startCol: anchor.col, endRow: rowIndex, endCol: colIndex };
  } else {
    anchor = { row: rowIndex, col: colIndex };
    selected = { startRow: rowIndex, startCol: colIndex, endRow: rowIndex, endCol: colIndex };
  }
  paintSelectionOnly();
  updateChrome();
}

function extendDrag(rowIndex, colIndex) {
  if (!dragging || !anchor) return;
  selected = { startRow: anchor.row, startCol: anchor.col, endRow: rowIndex, endCol: colIndex };
  paintSelectionOnly();
  updateChrome();
}

document.addEventListener("mouseup", () => {
  dragging = false;
});

function selectCell(rowIndex, colIndex, repaint = true) {
  anchor = { row: rowIndex, col: colIndex };
  selected = { startRow: rowIndex, startCol: colIndex, endRow: rowIndex, endCol: colIndex };
  if (repaint) render();
}

function paintSelectionOnly() {
  document.querySelectorAll("td[data-row][data-col]").forEach(td => {
    const rowIndex = Number(td.dataset.row);
    const colIndex = Number(td.dataset.col);
    td.classList.remove("selected", "range");
    if (isInSelection(rowIndex, colIndex)) {
      td.classList.add(isSelectedCell(rowIndex, colIndex) ? "selected" : "range");
    }
  });
}

function commitCell(td, rowIndex, colIndex) {
  const column = columns[colIndex];
  const row = rows[rowIndex];
  const rawValue = td.textContent.trim();
  const nextValue = normalizeValue(rawValue, column);
  if (row[column.field] === nextValue) {
    td.textContent = formatCell(row[column.field], column);
    return;
  }

  pushUndo();
  row[column.field] = nextValue;
  recalcRow(row);
  markDirty(row);
  validateRows(false);
  render();
}

function handleCellKeydown(event, td, rowIndex, colIndex) {
  if (event.key === "Enter") {
    event.preventDefault();
    commitCell(td, rowIndex, colIndex);
    focusCell(Math.min(rowIndex + 1, rows.length - 1), colIndex);
  }
  if (event.key === "Tab") {
    event.preventDefault();
    commitCell(td, rowIndex, colIndex);
    const nextCol = event.shiftKey ? Math.max(colIndex - 1, 0) : Math.min(colIndex + 1, columns.length - 1);
    focusCell(rowIndex, nextCol);
  }
}

function focusCell(rowIndex, colIndex) {
  requestAnimationFrame(() => {
    const cell = document.querySelector(`td[data-row="${rowIndex}"][data-col="${colIndex}"]`);
    if (cell) {
      cell.focus();
      selectCell(rowIndex, colIndex, false);
      paintSelectionOnly();
      updateChrome();
    }
  });
}

document.addEventListener("copy", event => {
  if (!selected) return;
  const text = getSelectedMatrix()
    .map(row => row.join("\t"))
    .join("\n");
  event.clipboardData.setData("text/plain", text);
  event.preventDefault();
});

document.addEventListener("paste", event => {
  if (!selected) return;
  const text = event.clipboardData.getData("text/plain");
  if (!text) return;
  pushUndo();
  const matrix = text.replace(/\r/g, "").split("\n").filter(Boolean).map(line => line.split("\t"));
  const start = normalizedSelection();
  matrix.forEach((line, rowOffset) => {
    const row = rows[start.minRow + rowOffset];
    if (!row) return;
    line.forEach((value, colOffset) => {
      const column = columns[start.minCol + colOffset];
      if (!column || !column.editable) return;
      row[column.field] = normalizeValue(value.trim(), column);
      markDirty(row);
    });
    recalcRow(row);
  });
  validateRows(false);
  render();
  event.preventDefault();
});

document.querySelector("#addRowBtn").addEventListener("click", () => {
  pushUndo();
  rows.push(createNewRow());
  render();
  focusCell(rows.length - 1, 0);
});

document.querySelector("#deleteRowsBtn").addEventListener("click", () => {
  const checked = rows.filter(row => row._checked);
  if (checked.length === 0) return;
  pushUndo();
  const remaining = [];
  rows.forEach(row => {
    if (!row._checked) {
      remaining.push(row);
      return;
    }
    if (row._state === "created") return;
    row._state = "deleted";
    deletedRows.push(row);
  });
  rows = remaining;
  render();
});

document.querySelector("#fillDownBtn").addEventListener("click", () => {
  if (!selected) return;
  const area = normalizedSelection();
  if (area.maxRow <= area.minRow) return;
  pushUndo();
  for (let colIndex = area.minCol; colIndex <= area.maxCol; colIndex += 1) {
    const column = columns[colIndex];
    if (!column.editable) continue;
    const value = rows[area.minRow][column.field];
    for (let rowIndex = area.minRow + 1; rowIndex <= area.maxRow; rowIndex += 1) {
      rows[rowIndex][column.field] = value;
      recalcRow(rows[rowIndex]);
      markDirty(rows[rowIndex]);
    }
  }
  validateRows(false);
  render();
});

document.querySelector("#undoBtn").addEventListener("click", () => {
  if (undoStack.length === 0) return;
  redoStack.push(snapshot());
  restore(undoStack.pop());
});

document.querySelector("#redoBtn").addEventListener("click", () => {
  if (redoStack.length === 0) return;
  undoStack.push(snapshot());
  restore(redoStack.pop());
});

document.querySelector("#validateBtn").addEventListener("click", () => {
  validateRows(true);
  render();
});

document.querySelector("#savePreviewBtn").addEventListener("click", () => {
  validateRows(true);
  render();
  changePreview.textContent = JSON.stringify(buildChangeSet(), null, 2);
});

document.querySelector("#checkAll").addEventListener("change", event => {
  rows.forEach(row => {
    row._checked = event.target.checked;
  });
  render();
});

function createNewRow() {
  const row = createRow({
    uid: `new-${newId++}`,
    sku: "",
    productName: "",
    color: "Chrome",
    qty: 1,
    unitPrice: 0,
    deliveryDate: "2026-07-30",
    remark: ""
  });
  row._state = "created";
  row._original = null;
  return row;
}

function pushUndo() {
  undoStack.push(snapshot());
  if (undoStack.length > 30) undoStack.shift();
  redoStack = [];
}

function snapshot() {
  return structuredClone({ rows, deletedRows, selected, anchor, newId });
}

function restore(state) {
  rows = state.rows;
  deletedRows = state.deletedRows;
  selected = state.selected;
  anchor = state.anchor;
  newId = state.newId;
  validateRows(false);
  render();
}

function markDirty(row) {
  if (row._state !== "created") row._state = hasRowChanged(row) ? "updated" : "unchanged";
}

function hasRowChanged(row) {
  if (!row._original) return true;
  return columns.some(column => row[column.field] !== row._original[column.field]);
}

function isCellDirty(row, field) {
  if (row._state === "created") return true;
  return row._original && row[field] !== row._original[field];
}

function recalcRow(row) {
  row.amount = roundMoney(Number(row.qty || 0) * Number(row.unitPrice || 0));
}

function validateRows(showList) {
  const messages = [];
  rows.forEach((row, rowIndex) => {
    row._errors = {};
    if (!row.sku) row._errors.sku = "SKU 必填";
    if (!row.productName) row._errors.productName = "产品名称必填";
    if (!Number.isFinite(Number(row.qty)) || Number(row.qty) <= 0) row._errors.qty = "数量必须大于 0";
    if (!Number.isFinite(Number(row.unitPrice)) || Number(row.unitPrice) < 0) row._errors.unitPrice = "单价不能小于 0";
    if (!row.deliveryDate) row._errors.deliveryDate = "交期必填";

    Object.entries(row._errors).forEach(([, message]) => {
      messages.push(`第 ${rowIndex + 1} 行：${message}`);
    });
  });

  if (showList || messages.length === 0) {
    validationList.innerHTML = "";
    validationList.classList.toggle("ok", messages.length === 0);
    const items = messages.length === 0 ? ["校验通过，可以生成保存请求。"] : messages;
    items.forEach(message => {
      const li = document.createElement("li");
      li.textContent = message;
      validationList.append(li);
    });
  }
  return messages.length === 0;
}

function buildChangeSet() {
  return {
    headUid: "so-20260702-001",
    version: 7,
    lines: {
      created: rows.filter(row => row._state === "created").map(toDto),
      updated: rows.filter(row => row._state === "updated").map(toDto),
      deleted: deletedRows.map(row => ({ uid: row.uid, version: 1 }))
    },
    clientState: {
      dirtyRows: rows.filter(row => row._state !== "unchanged").length,
      validationPassed: validateRows(false)
    }
  };
}

function toDto(row) {
  return {
    uid: row._state === "created" ? null : row.uid,
    sku: row.sku,
    productName: row.productName,
    color: row.color,
    qty: Number(row.qty),
    unitPrice: Number(row.unitPrice),
    amount: Number(row.amount),
    deliveryDate: row.deliveryDate,
    remark: row.remark
  };
}

function getSelectedMatrix() {
  const area = normalizedSelection();
  const matrix = [];
  for (let rowIndex = area.minRow; rowIndex <= area.maxRow; rowIndex += 1) {
    const line = [];
    for (let colIndex = area.minCol; colIndex <= area.maxCol; colIndex += 1) {
      const column = columns[colIndex];
      line.push(formatCell(rows[rowIndex][column.field], column));
    }
    matrix.push(line);
  }
  return matrix;
}

function normalizedSelection() {
  return {
    minRow: Math.min(selected.startRow, selected.endRow),
    maxRow: Math.max(selected.startRow, selected.endRow),
    minCol: Math.min(selected.startCol, selected.endCol),
    maxCol: Math.max(selected.startCol, selected.endCol)
  };
}

function isSelectedCell(rowIndex, colIndex) {
  if (!selected) return false;
  return selected.endRow === rowIndex && selected.endCol === colIndex;
}

function isInSelection(rowIndex, colIndex) {
  if (!selected) return false;
  const area = normalizedSelection();
  return rowIndex >= area.minRow && rowIndex <= area.maxRow && colIndex >= area.minCol && colIndex <= area.maxCol;
}

function updateChrome() {
  const dirtyCount = rows.filter(row => row._state !== "unchanged").length + deletedRows.length;
  dirtyBadge.textContent = dirtyCount > 0 ? `${dirtyCount} 项变更` : "未修改";
  dirtyBadge.classList.toggle("dirty", dirtyCount > 0);
  dirtyBadge.classList.toggle("quiet", dirtyCount === 0);

  if (selected) {
    const area = normalizedSelection();
    selectionInfo.textContent = `选择：${area.maxRow - area.minRow + 1} 行 x ${area.maxCol - area.minCol + 1} 列`;
  } else {
    selectionInfo.textContent = "选择：-";
  }

  const totalQty = rows.reduce((sum, row) => sum + Number(row.qty || 0), 0);
  const totalAmount = rows.reduce((sum, row) => sum + Number(row.amount || 0), 0);
  summaryInfo.textContent = `合计：${totalQty} 件 / ${roundMoney(totalAmount).toLocaleString()} USD`;
}

function normalizeValue(value, column) {
  if (column.number) {
    const number = Number(value.replace(/,/g, ""));
    return Number.isFinite(number) ? number : value;
  }
  return value;
}

function formatCell(value, column) {
  if (column.number && Number.isFinite(Number(value))) return String(value);
  return value ?? "";
}

function roundMoney(value) {
  return Math.round((Number(value) + Number.EPSILON) * 100) / 100;
}

validateRows(false);
render();
