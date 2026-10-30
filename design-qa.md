# 120×35 分组训练页验证（2026-10-30）

final result: passed

## 布局与证据

开发测试视口为 120×35 字符。五列各 19 字符、总宽 95，右侧留空。顶栏保留日期/回合、总属性与 Pt、体力和干劲；卡内按属性容量、训练代价与槽预测、收益与参与者分组。教诲比较、演目状态卡和效果侧栏已删除。

历史证据保存在本地目录 `artifacts/grouped-ui-20261030`，不随仓库发布；下列路径均相对于仓库根目录。图片由实际生产 DLL 的 Terminal.Gui framebuffer 生成，每格按 9×20 像素、MS Gothic 18px 栅格化；120×35 图片为 1080×700 像素。字体仅用于呈现，不改变用户终端设置。

- Legend 同视口对照：`artifacts/grouped-ui-20261030/visual/legend-after-120x35.png`。
- 同状态前后对照：`artifacts/grouped-ui-20261030/visual/before-after-120x35.png`。
- 实包普通训练第 6 回合：`artifacts/grouped-ui-20261030/visual/after/festa-t6-default.png`。
- 实包稽古替换第 7 回合：`artifacts/grouped-ui-20261030/visual/after/festa-t7-default.png`。
- 70×18 窄窗初始：`artifacts/grouped-ui-20261030/visual/after/festa-t7-narrow.png`；横向滚动：`artifacts/grouped-ui-20261030/visual/after/festa-t7-narrow-right.png`；正文翻页：`artifacts/grouped-ui-20261030/visual/after/festa-t7-narrow-pagedown.png`。
- 长日文、多演目槽与失败率：`artifacts/grouped-ui-20261030/visual/after/festa-stress.png`；局部原图：`artifacts/grouped-ui-20261030/visual/after/festa-stress-detail.png`。
- 无提示时的布局：`artifacts/grouped-ui-20261030/visual/after/festa-no-notices.png`。

Festa 默认画面来自本机第 6、7 回合实包，探针替换育成 ID，不导出账户标识。Legend 调用实际 `CreateDefault` 构造参考画面，训练数值取同一第 6 回合，剧本槽和 buff 为合成值。本机名称库缺少支援卡 30322/30323；仅 Legend 参考夹具替换为同类型已知支援卡，Festa 保留未知名称。具体替换记录在 `artifacts/grouped-ui-20261030/visual/after/legend-fixture-note.txt`。

## 检查结果

| 检查面 | 结果 |
| --- | --- |
| 120×35 布局 | 第 6/7 回合内容分别为 95×27、95×26，五张卡完整可见，右侧 25 列留空，无滚动条。没有比较入口、演目状态卡或效果侧栏。 |
| 分组对齐 | 五列的标题线、容量分隔线、代价分隔线与末尾横线均对齐；多槽与长名折行后，收益行仍在同一高度。空参与者列表只占收益一行。 |
| 稽古替换 | 第 6 回合命令为 `[101,105,102,103,106]`，第 7 回合为 `[5101,105,102,103,106]`。速度稽古替换普通速度训练；没有第二排训练或收益相加。 |
| 提示区 | 仅保留未达成检查点缺口、合宿剩余回合和三种服务端异常；目标达成或没有提示时移除整框。 |
| 局部颜色 | 剩余容量不超过 200/400 时数值标红/黄；体力四档只作用于当前数值；最高可执行五围收益只强调数字，禁用命令不参与比较。Hint、友情和 1–99 羁绊保留各自颜色。 |
| 标题与折行 | 普通和稽古标题居中，非零失败率在括号中标红，100% 完整显示；演目名称和槽增量整组换行，中文、长日文与数字完整保留。 |
| 滚动与缩放 | 70×18 提供横纵滚动条；实际鼠标事件检查横条箭头、轨道、拖动和横向滚轮，正文纵向滚轮、点击聚焦后的 PageUp/PageDown 与 Home/End。恢复 120×35 后滚动位置归零。 |
| 历史与模态 | 上下/左右历史导航、同回合覆盖、未读与淘汰、跨育成状态隔离、局部响应、配置保存、模态对话框及释放通过 smoke；后台响应与历史按键并发检查通过。 |

## 构建与回放

- 七阶段 smoke 通过，随后使用最终 ZIP 中同字节 DLL 再次通过。测试保留协议、收益计算、静态数据完整性与快照检查，删除比较页专用测试。
- 当前抓包目录回放 411 个请求和 411 个 Festa 响应，其中 350 个响应由插件注册处理器分发。59 次成功训练（普通 22 次、稽古 37 次，含合宿 12 次、属性超过 1200 的训练 27 次）的五围、技能点和训练后体力均匹配；2 次局部转成响应保持训练快照不变。此批样本没有失败训练。
- Release ZIP 版本为 `2026.10.30.1`，仅含主 DLL、日文资源 DLL 和 manifest，`Dependencies` 为空。主 DLL SHA-256：`DEA3F1FB0568B884284F7A96CA25198F16C036B37B199F453EA00426F181160A`。ZIP、smoke 和视觉探针加载的主 DLL 字节一致。
- 生产项目通过独立 NuGet 缓存引用本地 Host feed `K:\repos\UmamusumeResponseAnalyzer\artifacts\festa-nuget-final-20261027`。所有构建关闭自动部署。
- 日志：`artifacts/grouped-ui-20261030/packaged-smoke.log`、`artifacts/grouped-ui-20261030/packaged-capture-replay.log`、`artifacts/grouped-ui-20261030/release-build.log`；视觉验证：`artifacts/grouped-ui-20261030/visual/validation.log`；包检查：`artifacts/grouped-ui-20261030/package-check.json`。

验证限于实包离线回放、合成 DTO 和本地 framebuffer；未运行真实游戏或物理终端，也未遍历第三方插件。Host、Legend、收益计算和历史发布规则未修改；未部署、提交或推送。构建保留既有 TextView 过时警告。
