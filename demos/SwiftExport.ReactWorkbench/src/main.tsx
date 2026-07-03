import React from "react";
import ReactDOM from "react-dom/client";
import { ConfigProvider } from "antd";
import zhCN from "antd/locale/zh_CN";
import { AllCommunityModule, ModuleRegistry } from "ag-grid-community";
import App from "./App";
import "./styles.css";
import "ag-grid-community/styles/ag-grid.css";
import "ag-grid-community/styles/ag-theme-quartz.css";

ModuleRegistry.registerModules([AllCommunityModule]);

ReactDOM.createRoot(document.getElementById("root")!).render(
  <React.StrictMode>
    <ConfigProvider
      locale={zhCN}
      theme={{
        token: {
          borderRadius: 6,
          colorPrimary: "#1665d8",
          fontFamily: "Segoe UI, Microsoft YaHei, Arial, sans-serif"
        },
        components: {
          Card: { borderRadiusLG: 8 },
          Button: { borderRadius: 6 }
        }
      }}
    >
      <App />
    </ConfigProvider>
  </React.StrictMode>
);
