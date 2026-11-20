# A4–A10 验收 SUMMARY（task-7）

运行：`experiments/E2-A4-A10-run1`｜隔离副本 + 护栏启动器｜全程无窗口、单实例、日常安装零写入。

| # | 项目 | 最终状态 | revision | 关键证据 |
|---|------|----------|----------|----------|
| A4 | 批量文本 5 行 | ✅ 通过 | r0→r1 | 读回 ID 稳定、文本全匹配 |
| A5 | 撤销/重做 | ⊘ CLI 不可验（架构保证：每 MCP 写批次=一个原生 Undo 组） | — | SKILL.md + capabilities persistence |
| A6 | 保存→重启→重开 | ✅ 通过（全链路） | save r5；重开 r0 | file_version=sha256:86fa37cf…；磁盘 UTF-8 标记；重开后 A4/A8/A10 内容全在、A7 删除保持 |
| A7 | 批量删除 5 行 | ✅ 通过 | r4→r5 | 318→313；被删 ID 缺席；重开保持 |
| A8 | 多行插入 3 行 | ✅ 通过 | r3→r4 | 紧跟锚点、顺序精确；批内 ref 链可用；narration 行 speaker=null |
| A9 | 批量复制 | ✅ 通过（继承式构造）＋⚠️ 显式全量路径环境受限未验 | r1→r2 | 无 line.copy 原生 op；inherit:{from} 复制 mode/speaker/stage/place 1:1；voice 身份不继承（文档语义）；显式重述 voice → RESOURCE_NOT_FOUND（隔离副本无 mod 语音资源，688 条同类 validate 警告佐证） |
| A10 | 批量发言槽位 | ✅ 通过 | r2→r3 | slot 4→3；舞台数组逐字节未变（仅切槽位，无角色重置） |

## API 缺口（详单见 A4-A10-REPORT.md 第 3 节）
1. save_project 必须 {edit_session, expected_revision, request_id}（E1 报告缺记录）
2. apply_edits 资源赋值/继承必须带 catalog_version（在 get_context 的 **data** 里，非 context 层）
3. 无 line.copy 原生操作——复制必须 line.insert 构造；CopyVoice=true 在无完整资源库环境必被拒
4. read_project 分页 {items,next_cursor,total}，limit≤200；summary 无节点列表，需 graph 视图
5. inherit:{from} 后 voice 呈现为 set+不同资源 ID（疑似按说话人自动重绑定）——未文档化，建议专项核实
6. revision 按工作副本会话计（写 +1，save 不动，重开归零）
7. run_code detached 子进程被整树回收 → AA 启动必须走 ops_bash 后台 + 单 run_code 关键区模式（新坑，E1 未记录）

## 红线合规
- 两次启动全部经护栏 R1–R6（12 检查）✅｜单实例 ✅｜-batchmode -nographics -disableaudio ✅
- F:\AzureArchive_101 零写入（mtime 全程 22:15:43 不变）✅｜备份原件未动（sha256 校验）✅
- 失败现场全部保全（FAILURE-*.json + mcp/resp-save*.json）✅｜无 commit/stash/reset/clean ✅
