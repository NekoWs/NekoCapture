# 当前状态

版本：0.9.11。动作链契约见 [docs/PLAN-ORTHOGONAL-CHAINS.md](docs/PLAN-ORTHOGONAL-CHAINS.md)。

- 选区、截图、录制、滚动、OCR 与输出动作按同一条链顺序执行，异步步骤等待完成。
- 设置迁移具有版本、备份和幂等保护；完成动作存储在链尾。
- 快捷键页提供动作增删、箭头与拖动改序、前置值提示。
- 开发文档只保存当前契约、验证状态与待处理事项。

| 验证 | 结果 | 证据 |
|---|---|---|
| SelfTest | 914 通过，0 失败；下限 914 | `test-out/selftest-last-run.txt` |
| 静态护栏与安装器探针 | 全部通过；注释零警告 | `test-out/chain-guardrail-final.txt` |
| 中英文排版 | 54 个组合通过 | `test-out/chain-guardrail-final.txt` |
| 启动、控制器与端到端 | 7 项端到端通过 | `test-out/chain-full-pass.txt` |

OS 键盘注入的焦点安全项未启用。人工检查范围见 [UIAUDIT.md](UIAUDIT.md)。
发布产物与 SHA256 见 `release/chain-release-manifest.json`。
