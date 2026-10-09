import type { WorkspaceTabProps } from "../../../shared/workspace/页签契约";
import { useDictionaryEditor } from "./字典编辑会话";
import { DictionaryManagementView } from "./components/字典管理展示";

/** 单例字典页签以本页 tabId 保留编辑会话，Activity 仅包裹组件展示。 */
export default function DictionaryManagementTab(props: WorkspaceTabProps) {
  const editor = useDictionaryEditor(props);
  return props.renderDisplay(<DictionaryManagementView editor={editor} />);
}
