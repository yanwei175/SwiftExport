import { Button, Result } from "antd";
import { Link } from "react-router";

/** 未知应用地址使用独立错误入口，不注册伪业务页面。 */
export default function NotFoundRoute() {
  return (
    <Result
      status="404"
      title="页面不存在"
      extra={
        <Link to="/workspace">
          <Button type="primary">返回工作区</Button>
        </Link>
      }
    />
  );
}
