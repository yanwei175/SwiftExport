import { products } from "./products";
import type { OrderLine } from "../types";

const pick = (index: number) => products[index];

export const createAmount = (qty: number, unitPrice: number) =>
  Math.round((qty * unitPrice + Number.EPSILON) * 100) / 100;

export const initialOrderLines: OrderLine[] = [
  {
    id: "line-001",
    version: 3,
    productUid: pick(0).uid,
    sku: pick(0).sku,
    customerSku: pick(0).customerSku,
    productName: pick(0).name,
    productImageUrl: pick(0).imageUrl,
    color: pick(0).color,
    unit: pick(0).unit,
    qty: 120,
    unitPrice: pick(0).defaultPrice,
    amount: createAmount(120, pick(0).defaultPrice),
    deliveryDate: "2026-07-18",
    remark: "客户标准包装",
    lineState: "clean",
    errors: []
  },
  {
    id: "line-002",
    version: 1,
    productUid: pick(1).uid,
    sku: pick(1).sku,
    customerSku: pick(1).customerSku,
    productName: pick(1).name,
    productImageUrl: pick(1).imageUrl,
    color: pick(1).color,
    unit: pick(1).unit,
    qty: 80,
    unitPrice: pick(1).defaultPrice,
    amount: createAmount(80, pick(1).defaultPrice),
    deliveryDate: "2026-07-20",
    remark: "",
    lineState: "clean",
    errors: []
  },
  {
    id: "line-003",
    version: 2,
    productUid: pick(2).uid,
    sku: pick(2).sku,
    customerSku: pick(2).customerSku,
    productName: pick(2).name,
    productImageUrl: pick(2).imageUrl,
    color: pick(2).color,
    unit: pick(2).unit,
    qty: 60,
    unitPrice: pick(2).defaultPrice,
    amount: createAmount(60, pick(2).defaultPrice),
    deliveryDate: "2026-07-22",
    remark: "需确认贴标",
    lineState: "clean",
    errors: []
  }
];
