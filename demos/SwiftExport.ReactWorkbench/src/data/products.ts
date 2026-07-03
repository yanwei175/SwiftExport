import type { ProductLookupItem } from "../types";

export const products: ProductLookupItem[] = [
  {
    uid: "prod-1001",
    sku: "CP-1001",
    customerSku: "PAC-BF-01",
    name: "Brass Basin Faucet",
    color: "Chrome",
    unit: "PCS",
    defaultPrice: 18.5,
    stockQty: 1280,
    imageUrl: "/assets/products/basin-faucet.png",
    supplier: "Hongda Metal"
  },
  {
    uid: "prod-1002",
    sku: "CP-1002",
    customerSku: "PAC-KF-19",
    name: "Kitchen Pull-Out Faucet",
    color: "Matte Black",
    unit: "PCS",
    defaultPrice: 31.2,
    stockQty: 420,
    imageUrl: "/assets/products/kitchen-faucet.png",
    supplier: "Yongxin Faucet"
  },
  {
    uid: "prod-1003",
    sku: "CP-1003",
    customerSku: "PAC-SM-07",
    name: "Shower Mixer",
    color: "Brushed Nickel",
    unit: "PCS",
    defaultPrice: 42,
    stockQty: 260,
    imageUrl: "/assets/products/shower-mixer.png",
    supplier: "Blue River Sanitary"
  },
  {
    uid: "prod-1004",
    sku: "CP-1004",
    customerSku: "PAC-AV-02",
    name: "Angle Valve",
    color: "Chrome",
    unit: "PCS",
    defaultPrice: 4.8,
    stockQty: 3600,
    imageUrl: "/assets/products/angle-valve.png",
    supplier: "Kaiyuan Hardware"
  },
  {
    uid: "prod-1005",
    sku: "CP-1005",
    customerSku: "PAC-PW-11",
    name: "Pop-up Waste",
    color: "Gold",
    unit: "PCS",
    defaultPrice: 7.6,
    stockQty: 850,
    imageUrl: "/assets/products/popup-waste.png",
    supplier: "Hengli Brass"
  },
  {
    uid: "prod-1006",
    sku: "CP-1006",
    customerSku: "PAC-HD-05",
    name: "Faucet Handle Kit",
    color: "Brushed Nickel",
    unit: "SET",
    defaultPrice: 5.4,
    stockQty: 1900,
    imageUrl: "/assets/products/handle-kit.png",
    supplier: "Northstar Components"
  }
];
