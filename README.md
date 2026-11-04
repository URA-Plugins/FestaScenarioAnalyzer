# FestaScenarioAnalyzer

日服「駿大祭シナリオ」（Festa，scenario 15）训练分析与内存历史浏览插件。

## 显示与操作

- 开发测试尺寸为 120×35 字符，五列各宽 19 字符、总宽 95。顶栏显示日期/回合、总属性与 Pt、当前体力和干劲。同一属性存在可执行稽古时以稽古替换普通训练，否则显示普通训练。稽古列标题为 `演目短名 Lv<演目等级>`（如 `飞悬 Lv3`），整段标题用亮青色；短名取官方演目名的前两个字（流镝马→流镝、神旗争夺战→神旗）。
- 训练卡用横线分成属性容量、训练代价与槽预测、收益与参与者三组，五列按组对齐。参与者区固定五行，每人一行，不足五人补空白；长名字按终端字符宽度截短并加省略号，保留 Hint 与牵绊数字。`当前:可获得` 下的第二个数字是距属性上限的剩余增长空间，不是上限本身；不超过 200/400 时分别标红/黄。
- 训练后体力显示为当前值/上限，当前值按 `<30`、`<50`、`<70`、其余四档标红、橙、黄、绿。非零失败率在训练标题括号中标红。体力下方显示训练等级与全部演目槽增量合计，例如 `Lv1 | +4`；零增量显示 `Lv1 | +0`。
- 标记行位于训练标题与第一条横线之间，五列共用同一行：稽古位参与者含「助力教诲」绑定演目的担当时标亮黄 `助力支撑`，服务端把该训练位标为追加稽古掷中时标亮紫 `追加稽古`，两者可同时出现。卡内宽度可容纳时并排为 `助力支撑 追加稽古`，否则各占一行（不把名称从中间折开）。本回合任何一列有标记才占用这一行，未标记的列留空以维持三组分隔线对齐。
- 五围总收益与技能点同排，可执行训练中最高的五围收益数字标青色；这不代表综合最优推荐。支援/NPC、Hint 和友情标记保留。仅为牵绊未满的支援卡显示数值：`0–79` 标红、`80–99` 标黄；非支援卡 NPC 和满牵绊支援卡隐藏数字，名字使用释放的宽度。支援卡身份由 `support_card_array` 确定。
- 提示区显示合宿剩余回合、`助力支撑 剩余 N 次`（读 `home_info.training_bonus_remain_turn_num`，为 0 时不显示），以及服务端报告的未增长、槽溢出和等级溢出。没有这些提示时不保留空框。
- 面板聚焦时，↑/↓ 浏览上一条/下一条历史，←/→ 跳到最旧/最新记录。鼠标位于当前工作区正文或空白区域时，滚轮向上/向下浏览较旧/较新记录，左键确认停留，右键返回最新。PageUp/PageDown、Home/End 和滚动条滚动正文，横向滚轮保留横向滚动。模态对话框和其他工作区保留自己的输入行为。

历史以 `(single_mode_chara_id, turn)` 为键，只保存完整训练画面；事件和比赛过程响应更新内部状态，保留最近的训练画面。同回合新的完整训练画面原位更新。历史切换默认预览，收到新记录时自动返回最新；左键确认后停留并提示未读，切换到另一条历史记录重新进入预览。连续切换在同一条提示中更新位置和状态，并刷新显示时间。正在查看的最旧记录被淘汰时返回最新。快照不可变，历史不写入磁盘。

主区右侧自页面顶部纵向显示 `教诲/转成` 面板：每种训练一行，行首为该训练的基础属性名（`速度`/`耐力`/`力量`/`根性`/`智力`，与上方五列一一对应），形如 `速度 牵绊→特化`（转成未发生时只列教诲，未分配显示 `—`），该面板由插件自身生成，不依赖 EventLogger。装有配套的 EventLogger 时，其连续事件、训练失败、事件数、继承收益和胜场接在教诲面板之后。附加区域最小宽度为 24 字符，随窗口扩大占用剩余宽度；内容自动折行并随整页滚动，空内容不占位。历史保存各自角色与回合的附加内容，同键更新不增加历史条目或未读；淘汰记录、关闭历史和卸载会释放对应内容。

## 附加内容接口

`FestaTrainingDisplay.RegisterPartProducer(sourceTitle)` 返回可释放的生产者句柄。调用 `Update(FestaTrainingDisplayId, IReadOnlyList<FestaDisplayLine>)` 提交带颜色的不可变文本行；标识由 `SingleModeCharaId` 与 `Turn` 组成。接口只写附加区域，不能修改训练数据。Festa 复制行集合，历史按键保存快照；训练画面发布前最多暂存生产者最近一次更新，避免积累没有训练画面的回合。释放句柄删除该生产者的全部内容。

EventLogger 对 Festa 单向软依赖，Festa 不引用 EventLogger。此接口对应两个插件的 `2026.11.1` 配套版本，须成对更新；单独安装任一插件可使用其自身功能。

配置通过 Host 的插件配置入口修改，点击“保存”才写入 `PluginData/FestaScenarioAnalyzer/settings.json`：

```json
{
  "historyLimit": 100
}
```

范围为 0–1000；0 关闭历史与方向键、鼠标历史导航，保留最新画面。缩小上限时释放被淘汰的快照；重新开启历史不回填旧输出，下一份输出替换并释放未进入历史的画面快照。插件按需创建 workspace，卸载时释放已创建的面板及历史资源。

## 数据与收益口径

普通训练 `101/105/102/103/106`、合宿训练 `601–605`、演目稽古 `5101–5105` 按速度、耐力、力量、毅力、智力对应。响应可同时返回普通与稽古命令，显示时按可执行稽古替换对应普通训练；两条命令的收益不相加。home 与 Festa 增量按完整命令 ID 关联，同一类训练存在多个可用候选时报告错误。实包中的合宿使用 `5101–5105`，`601–605` 仅经过合成测试。

五围、技能点和体力合并 home 参数与同命令 ID 的 Festa 参数；失败率取 home。训练后体力在合并后限制到 0 与最大体力之间。五围显示沿用 Legend/Ramen 的参考折算：`Revise(x) = x > 1200 ? 2*x - 1200 : x`，增量为 `Revise(当前值 + 参数增量) - Revise(当前值)`。抓包校准验证协议字段的变化，不证明游戏 HUD 使用相同折算。

槽增量、等级上限和合宿剩余回合直接读取服务端字段。未增长/溢出标志表示服务端报告的事件，不换算成预计浪费数量。

Load、普通响应和 AttachAdvice 分别按实际 DTO 结构取值。身份明确的响应先更新内部状态，再判断 `playing_state == 1`、无待处理事件、无进行中比赛且具备五种属性的训练命令，满足后才发布训练画面。FusionAdvice 缺少育成身份与 home，因此等待下一份身份明确的完整响应，不拼接旧训练数据。新育成不继承上一育成的当前状态，历史记录仍按各自身份浏览。

运行时不读取游戏 master 数据库。演目、10 种教诲的 10 级说明、5 种初始教诲转成附效、演目等级效果和检查点编入插件。默认界面和说明为简体中文，日文说明资源保存游戏原文。来源表、文本类别及 master SHA-256 记录在 [Data/source.json](Data/source.json)，使用到的主表行保存在 [Data/master-data.json](Data/master-data.json)。

更新数据需要 Python 3.11+（仅标准库）和日服 master：

```powershell
python .\scripts\generate_festa_data.py --master 'G:\DMM\Umamusume\umamusume_Data\Persistent\master\master.mdb'
python .\scripts\generate_festa_data.py --master 'G:\DMM\Umamusume\umamusume_Data\Persistent\master\master.mdb' --check
```

生成器以只读方式打开数据库，核对 ID、等级、效果类型、数值和关键原文，再生成 C# 表、资源及来源文件。新增效果或文案变化需要核实后修改脚本的中文映射；`--check` 只验证，不写文件。

## 本地构建与验证

要求 .NET 10 SDK。生产项目只通过 `UmamusumeResponseAnalyzer Version="*" PrivateAssets="all"` 引用 Host，不依赖其他插件；跨插件入口限于上述附加内容接口。实际运行的 Host 必须包含 Festa 协议类型。底部滚动条的鼠标输入修复位于 Host 的任务栏事件处理，需使用包含该修复的 Host 构建；单独替换插件 ZIP 不包含此修复。

公开 SDK `2026.10.31.1` 包含 Festa DTO。仓库 `NuGet.Config` 将 Host 包映射到 `https://ura.shuise.net/nuget/index.json`，其它包映射到 nuget.org。在插件目录执行：

```powershell
$festaBuildArgs = @(
  '-p:GenerateUraPluginManifestOnBuild=false'
  '-p:PackageUraPluginOnBuild=false'
  '-p:DeployUraPluginToLocalAppDataOnBuild=false'
  '-m:1'
  '-nr:false'
)
dotnet restore .\FestaScenarioAnalyzer.csproj --force --no-http-cache
if ($LASTEXITCODE) { throw '依赖还原失败。' }
dotnet build .\FestaScenarioAnalyzer.csproj -c Release @festaBuildArgs
if ($LASTEXITCODE) { throw '插件构建失败。' }
```

生产插件的 `obj/project.assets.json` 记录解析到的 Host 包版本；该缓存包的 `.nupkg.metadata` 中 `source` 必须是公开 URA NuGet 源。

2026-10-29 校准使用同一轮育成、回合 1–78 的 427 份 Festa 响应。55 次成功训练（普通 21 次、稽古 34 次，其中合宿 11 次）中，五围、技能点及合并后的体力预测全部与执行结果一致；19 次训练需要计入 Festa 体力增量。两次局部转成均由后续完整装备响应承接。未覆盖失败训练、多轮真实育成切换或所有教诲组合；这些边界仍需另外取样。

2026-10-30 分组布局验收回放 411 份 Festa 响应，核对 59 次成功训练（普通 22 次、稽古 37 次，含合宿 12 次、属性超过 1200 的训练 27 次）和 2 次局部转成响应；五围、技能点、训练后体力与快照隔离检查通过。实包第 6、7 回合用于 120×35 的普通五列、速度稽古替换及 Legend 同视口对照。视觉证据与验证记录见 [design-qa.md](design-qa.md)。

2026-11-01 回放包含 423 份 Festa 响应，55 次成功训练（普通 19 次、稽古 36 次）的五围、技能点和体力预测全部吻合，预测公式保持不变；EventLogger 的对应训练记录也与面板一致。本次名字库包含 `30322`、友人卡 `30323`。名称与支援类型依赖 Host 数据库，原始包和账户数据不编入插件。

2026-11-01 面板离屏自检覆盖槽增量合计与零增量、0–5 人固定高度、长中文/日文名字截短、Hint 和姓名颜色、支援卡牵绊 0/79/80/99 的颜色、满牵绊隐藏，以及 NPC 零值和非零值隐藏；隐藏数字后名字恢复可用宽度且没有多余空格。120×35、160×40 与 70×18 下的 Extra 折行、缩放和滚动通过。逐回合附加内容切换、同键刷新和生产者释放通过。本地证据位于 `artifacts/panel-check-20261101`，使用最终 Release DLL；ZIP 内 DLL 字节一致，Host 包校验通过。此次验证不包含真实在线游戏或物理终端。

生成 Release ZIP 并自动部署到本机已有的 `%LOCALAPPDATA%\UmamusumeResponseAnalyzer\Plugins` 目录：

```powershell
dotnet build .\FestaScenarioAnalyzer.csproj -c Release -r win-x64 --self-contained false -p:PlatformTarget=AnyCPU -m:1 -nr:false `
  -p:GenerateUraPluginManifestOnBuild=true -p:PackageUraPluginOnBuild=true
if ($LASTEXITCODE) { throw '插件打包失败。' }
```

产物为 `bin/Release/FestaScenarioAnalyzer.zip`，仅包含 manifest、插件 DLL 和日文资源 DLL；Host 和其他插件 DLL 不进入包。`Dependencies` 为空。

SDK 默认启用构建后自动部署；仅构建验证时显式传入 `-p:DeployUraPluginToLocalAppDataOnBuild=false`。部署后重启 Host 加载新包。

GitHub Actions 调用固定提交的 [URA plugin workflows](https://github.com/URA-Plugins/.github)，执行 Release 构建和 ZIP 校验。普通 push、pull request 和手动运行执行验证；推送 `v` 开头且数字部分与项目版本一致的 tag 后，通过验证才创建 GitHub Release。

上述校准使用实际抓包离线回放、合成 DTO 和本地终端 framebuffer；覆盖范围以所列样本为限。不包含最优教诲推荐、未来回合模拟或自动游戏操作。
