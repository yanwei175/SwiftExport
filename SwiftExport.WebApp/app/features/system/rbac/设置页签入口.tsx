import { useState } from "react";
import {
  Alert,
  App,
  Button,
  Card,
  Descriptions,
  Flex,
  Input,
  Select,
  Space,
  Tag,
  Typography,
} from "antd";
import { ReloadOutlined, PlusOutlined } from "@ant-design/icons";
import { PreferenceDataGrid } from "../../../shared/ui/偏好表格";
import type {
  SwiftDataGridColumn,
  SwiftDataGridRowKey,
} from "../../../shared/ui/swift-data-grid";
import { useSession } from "../../../shared/session/会话上下文";
import { hasPermission } from "../../../shared/authorization/权限检查";
import { useWorkspaceTab } from "../../../shared/workspace/useWorkspaceTab";
import type { WorkspaceTabProps } from "../../../shared/workspace/页签契约";
import {
  fetchUsers,
  fetchRoles,
  fetchPermissions,
  useSettingsQuery,
} from "./queries";
import type { ManagedUser, ManagedRole, ManagedPermission } from "./queries";
import { describeSettingsReadError } from "./提交契约";
import { settingsPages } from "./页面定义";
import "./设置布局.css";

/** 个人概览复用已核实 me，不调用管理员目录；改密进入可保护的独立页签。 */
function PersonalSettings() {
  const session = useSession();
  const host = useWorkspaceTab();
  const { message } = App.useApp();
  const [refreshing, setRefreshing] = useState(false);
  return (
    <Card title="个人设置">
      <Descriptions
        column={1}
        items={[
          { key: "name", label: "登录名", children: session.user?.userName },
          { key: "nick", label: "显示名称", children: session.user?.nickName },
          {
            key: "roles",
            label: "角色",
            children: session.user?.roles?.join("、") || "未分配角色",
          },
        ]}
      />
      <Space wrap>
        <Button onClick={() => host.open({ pageKind: "password-change" })}>
          修改我的密码
        </Button>
        <Button
          loading={refreshing}
          onClick={() => {
            setRefreshing(true);
            void session
              .refreshPermissions()
              .then(() => message.success("权限已从服务器刷新。"))
              .catch(() => message.error("权限刷新失败，请重新认证后重试。"))
              .finally(() => setRefreshing(false));
          }}
        >
          刷新我的权限
        </Button>
      </Space>
      <Typography.Title level={5}>当前有效权限</Typography.Title>
      <Space wrap>
        {session.user?.permissions?.map((key) => (
          <Tag key={key}>{key}</Tag>
        ))}
      </Space>
    </Card>
  );
}

/** 用户管理只持有查询条件和行选择；表单当前值始终由另一个稳定编辑页签拥有。 */
function UsersPage({ isActive, renderDisplay }: WorkspaceTabProps) {
  const host = useWorkspaceTab();
  const session = useSession();
  const [keyword, setKeyword] = useState("");
  const [request, setRequest] = useState({
    keyword: "",
    pageIndex: 1,
    pageSize: 20,
    isActive: null as boolean | null,
  });
  const [selectedKeys, onSelectionChange] = useState<SwiftDataGridRowKey[]>([]);
  const query = useSettingsQuery(
    "getManagedUsersPage",
    request,
    isActive,
    "System.Users.View",
    (signal) => fetchUsers(request, signal),
  );
  const canWrite =
    session.status === "authenticated" &&
    hasPermission(session.user, "System.Users.Manage");
  const open = (pageKind: string, id: string | undefined) => {
    if (id) host.open({ pageKind, params: { id } });
  };
  const columns: SwiftDataGridColumn<ManagedUser>[] = [
    {
      key: "userName",
      title: "登录名",
      width: 160,
      render: (row) => row.userName,
    },
    {
      key: "nickName",
      title: "显示名称",
      width: 160,
      render: (row) => row.nickName,
    },
    { key: "email", title: "邮箱", width: 220, render: (row) => row.email },
    {
      key: "phoneNumber",
      title: "联系电话",
      width: 150,
      render: (row) => row.phoneNumber,
    },
    {
      key: "status",
      title: "状态",
      width: 100,
      render: (row) => (
        <Tag color={row.isActive ? "success" : "default"}>
          {row.isActive ? "启用" : "停用"}
        </Tag>
      ),
    },
    {
      key: "isAdmin",
      title: "管理员",
      width: 100,
      render: (row) => (row.isAdmin ? "是" : "否"),
    },
    {
      key: "operations",
      title: "操作",
      width: 370,
      render: (row) => (
        <Space size={4} wrap>
          <Button size="small" onClick={() => open("user-profile", row.uid)}>
            资料
          </Button>
          <Button size="small" onClick={() => open("user-roles", row.uid)}>
            角色与有效权限
          </Button>
          <Button
            size="small"
            disabled={!canWrite}
            onClick={() => open("user-status", row.uid)}
          >
            {row.isActive ? "停用" : "启用"}
          </Button>
          <Button
            size="small"
            disabled={!canWrite || row.isAdmin || !row.isActive}
            onClick={() => open("password-reset", row.uid)}
          >
            重置密码
          </Button>
        </Space>
      ),
    },
  ];
  return renderDisplay(
    <Flex
      vertical
      gap={8}
      className="settings-page"
      data-testid="user-management"
    >
      <Typography.Title level={4}>用户管理</Typography.Title>
      <Space wrap>
        <Input.Search
          aria-label="用户搜索"
          placeholder="登录名、显示名称"
          value={keyword}
          onChange={(event) => setKeyword(event.target.value)}
          onSearch={() =>
            setRequest({ ...request, keyword: keyword.trim(), pageIndex: 1 })
          }
          allowClear
        />
        <Select
          aria-label="用户状态筛选"
          value={request.isActive === null ? "all" : String(request.isActive)}
          options={[
            { value: "all", label: "全部状态" },
            { value: "true", label: "启用" },
            { value: "false", label: "停用" },
          ]}
          onChange={(value) =>
            setRequest({
              ...request,
              pageIndex: 1,
              isActive: value === "all" ? null : value === "true",
            })
          }
        />
        <Button
          icon={<PlusOutlined />}
          aria-label="创建用户"
          disabled={!canWrite}
          onClick={() =>
            host.open({
              pageKind: "user-create",
              params: { draft: crypto.randomUUID() },
            })
          }
        >
          创建用户
        </Button>
        <Button
          aria-label="刷新用户"
          icon={<ReloadOutlined />}
          loading={query.isFetching}
          onClick={() => void query.refetch()}
        />
      </Space>
      {query.error && (
        <Alert
          type="error"
          showIcon
          title={describeSettingsReadError(query.error)}
        />
      )}
      <Card className="settings-table-card">
        <PreferenceDataGrid
          preferenceId="system-users"
          rows={query.data?.items ?? []}
          getRowKey={(row) => row.uid!}
          columns={columns}
          readOnly
          selectedKeys={selectedKeys}
          onSelectionChange={onSelectionChange}
          loading={query.isFetching}
          fillHeight
          pagination={{
            ...request,
            total: query.data?.totalCount ?? 0,
            onChange: (pageIndex, pageSize) => {
              onSelectionChange([]);
              setRequest({ ...request, pageIndex, pageSize });
            },
          }}
        />
      </Card>
    </Flex>,
  );
}

/** 普通角色的资料、启停、删除及完整赋权分别进入原实体对应的受保护会话。 */
function RolesPage({ isActive, renderDisplay }: WorkspaceTabProps) {
  const host = useWorkspaceTab();
  const session = useSession();
  const [keyword, setKeyword] = useState("");
  const [request, setRequest] = useState({
    keyword: "",
    pageIndex: 1,
    pageSize: 20,
    isActive: null as boolean | null,
  });
  const [selectedKeys, onSelectionChange] = useState<SwiftDataGridRowKey[]>([]);
  const query = useSettingsQuery(
    "getManagedRolesPage",
    request,
    isActive,
    "System.Roles.View",
    (signal) => fetchRoles(request, signal),
  );
  const canWrite =
    session.status === "authenticated" &&
    hasPermission(session.user, "System.Roles.Manage");
  const open = (pageKind: string, id: string | undefined) => {
    if (id) host.open({ pageKind, params: { id } });
  };
  const columns: SwiftDataGridColumn<ManagedRole>[] = [
    {
      key: "roleCode",
      title: "角色编码",
      width: 210,
      render: (row) => row.roleCode,
    },
    {
      key: "roleName",
      title: "角色名称",
      width: 180,
      render: (row) => row.roleName,
    },
    {
      key: "description",
      title: "描述",
      width: 260,
      render: (row) => row.description,
    },
    {
      key: "status",
      title: "状态",
      width: 100,
      render: (row) => (
        <Tag color={row.isActive ? "success" : "default"}>
          {row.isActive ? "启用" : "停用"}
        </Tag>
      ),
    },
    {
      key: "system",
      title: "内置角色",
      width: 100,
      render: (row) => (row.isSystem ? "是" : "否"),
    },
    {
      key: "operations",
      title: "操作",
      width: 320,
      render: (row) => (
        <Space size={4} wrap>
          <Button size="small" onClick={() => open("role-profile", row.uid)}>
            资料
          </Button>
          <Button
            size="small"
            onClick={() => open("role-permissions", row.uid)}
          >
            权限
          </Button>
          <Button
            size="small"
            disabled={!canWrite || row.isSystem}
            onClick={() => open("role-status", row.uid)}
          >
            {row.isActive ? "停用" : "启用"}
          </Button>
          <Button
            size="small"
            danger
            disabled={!canWrite || row.isSystem}
            onClick={() => open("role-delete", row.uid)}
          >
            删除
          </Button>
        </Space>
      ),
    },
  ];
  return renderDisplay(
    <Flex
      vertical
      gap={8}
      className="settings-page"
      data-testid="role-management"
    >
      <Typography.Title level={4}>角色管理</Typography.Title>
      <Space wrap>
        <Input.Search
          aria-label="角色搜索"
          placeholder="角色编码、名称"
          value={keyword}
          onChange={(event) => setKeyword(event.target.value)}
          onSearch={() =>
            setRequest({ ...request, keyword: keyword.trim(), pageIndex: 1 })
          }
          allowClear
        />
        <Select
          aria-label="角色状态筛选"
          value={request.isActive === null ? "all" : String(request.isActive)}
          options={[
            { value: "all", label: "全部状态" },
            { value: "true", label: "启用" },
            { value: "false", label: "停用" },
          ]}
          onChange={(value) =>
            setRequest({
              ...request,
              pageIndex: 1,
              isActive: value === "all" ? null : value === "true",
            })
          }
        />
        <Button
          icon={<PlusOutlined />}
          aria-label="创建角色"
          disabled={!canWrite}
          onClick={() =>
            host.open({
              pageKind: "role-create",
              params: { draft: crypto.randomUUID() },
            })
          }
        >
          创建角色
        </Button>
        <Button
          aria-label="刷新角色"
          icon={<ReloadOutlined />}
          loading={query.isFetching}
          onClick={() => void query.refetch()}
        />
      </Space>
      {query.error && (
        <Alert
          type="error"
          showIcon
          title={describeSettingsReadError(query.error)}
        />
      )}
      <Card className="settings-table-card">
        <PreferenceDataGrid
          preferenceId="system-roles"
          rows={query.data?.items ?? []}
          getRowKey={(row) => row.uid!}
          columns={columns}
          readOnly
          selectedKeys={selectedKeys}
          onSelectionChange={onSelectionChange}
          loading={query.isFetching}
          fillHeight
          pagination={{
            ...request,
            total: query.data?.totalCount ?? 0,
            onChange: (pageIndex, pageSize) => {
              onSelectionChange([]);
              setRequest({ ...request, pageIndex, pageSize });
            },
          }}
        />
      </Card>
    </Flex>,
  );
}

/** 权限定义提供分组、搜索和只读可分配标记，不新增在线修改或目录同步写入口。 */
function PermissionsPage({ isActive, renderDisplay }: WorkspaceTabProps) {
  const [keyword, setKeyword] = useState("");
  const [group, setGroup] = useState<string>();
  const [selectedKeys, onSelectionChange] = useState<SwiftDataGridRowKey[]>([]);
  const query = useSettingsQuery(
    "getManagedPermissions",
    null,
    isActive,
    "System.Permissions.View",
    (signal) => fetchPermissions(signal),
  );
  const rows = (query.data ?? []).filter(
    (row) =>
      (!group || row.groupKey === group) &&
      `${row.permissionKey} ${row.displayName}`
        .toLowerCase()
        .includes(keyword.toLowerCase()),
  );
  const columns: SwiftDataGridColumn<ManagedPermission>[] = [
    {
      key: "groupKey",
      title: "分组",
      width: 160,
      render: (row) => row.groupKey,
    },
    {
      key: "displayName",
      title: "名称",
      width: 210,
      render: (row) => row.displayName,
    },
    {
      key: "permissionKey",
      title: "权限键",
      width: 380,
      render: (row) => row.permissionKey,
    },
    {
      key: "sortOrder",
      title: "排序",
      width: 90,
      render: (row) => row.sortOrder,
    },
    {
      key: "active",
      title: "启用",
      width: 90,
      render: (row) => (row.isActive ? "是" : "否"),
    },
    {
      key: "assignable",
      title: "普通角色可分配",
      width: 150,
      render: (row) => (row.isAssignable ? "是" : "保留权限"),
    },
  ];
  return renderDisplay(
    <Flex
      vertical
      gap={8}
      className="settings-page"
      data-testid="permission-catalog"
    >
      <Typography.Title level={4}>权限定义</Typography.Title>
      <Space wrap>
        <Input
          aria-label="权限搜索"
          placeholder="权限键、名称"
          value={keyword}
          onChange={(event) => setKeyword(event.target.value)}
          allowClear
        />
        <Select
          aria-label="权限分组"
          placeholder="全部分组"
          allowClear
          value={group}
          options={[
            ...new Set(query.data?.map((row) => row.groupKey) ?? []),
          ].map((value) => ({ value, label: value }))}
          onChange={setGroup}
        />
        <Button
          aria-label="刷新权限定义"
          icon={<ReloadOutlined />}
          loading={query.isFetching}
          onClick={() => void query.refetch()}
        />
      </Space>
      <Typography.Text type="secondary">
        权限定义由后端目录管理，当前页面只读。数据库目录不一致时需通过现有显式同步工具处理。
      </Typography.Text>
      {query.error && (
        <Alert
          type="error"
          showIcon
          title={describeSettingsReadError(query.error)}
        />
      )}
      <Card className="settings-table-card">
        <PreferenceDataGrid
          preferenceId="system-permissions"
          rows={rows}
          getRowKey={(row) => row.uid!}
          columns={columns}
          readOnly
          selectedKeys={selectedKeys}
          onSelectionChange={onSelectionChange}
          loading={query.isFetching}
          fillHeight
        />
      </Card>
    </Flex>,
  );
}

/** 设置菜单在应用层注册，此入口只分发本 feature 的已实现页面。 */
export default function SettingsTab(props: WorkspaceTabProps) {
  if (props.descriptor.pageKind === "personal-settings")
    return props.renderDisplay(<PersonalSettings />);
  if (props.descriptor.pageKind === "user-management")
    return <UsersPage {...props} />;
  if (props.descriptor.pageKind === "role-management")
    return <RolesPage {...props} />;
  if (props.descriptor.pageKind === "permission-catalog")
    return <PermissionsPage {...props} />;
  throw new Error(`未注册设置页面：${Object.keys(settingsPages).join("、")}`);
}
