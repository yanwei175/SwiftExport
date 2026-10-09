import type { Config } from "@react-router/dev/config";

export default {
  // 只生成静态 SPA；根渲染不读取浏览器身份或业务接口。
  ssr: false,
} satisfies Config;
