# FestaScenarioAnalyzer

日服「駿大祭シナリオ」（Festa，scenario 15）训练分析与内存历史浏览插件。

## 显示与操作

- 开发测试尺寸为 120×35 字符，五列各宽 19 字符、总宽 95，右侧留空。顶栏显示日期/回合、总属性与 Pt、当前体力和干劲。同一属性存在可执行稽古时以稽古替换普通训练，否则显示普通训练；只提供禁用命令时保留不可执行标记。
- 训练卡用横线分成属性容量、训练代价与槽预测、收益与参与者三组。五列按组对齐，长名字或多项槽预测折行后，后续收益行仍在同一高度。`当前:可获得` 下的第二个数字是距属性上限的剩余增长空间，不是上限本身；不超过 200/400 时分别标红/黄。
- 训练后体力显示为当前值/上限，当前值按 `<30`、`<50`、`<70`、其余四档标红、橙、黄、绿。非零失败率在训练标题括号中标红。训练等级与演目名称/槽增量列在体力下方，名称和增量整组换行。
- 五围总收益与技能点同排，可执行训练中最高的五围收益数字标青色；这不代表综合最优推荐。支援/NPC、Hint 和友情标记保留，羁绊数字仅显示 1–99。长名字完整折行，保留各段颜色。
- 提示区仅显示未达成的下一检查点缺口、合宿剩余回合，以及服务端报告的未增长、槽溢出和等级溢出。没有这些提示时不保留空框。
- 面板聚焦时，↑/↓ 浏览上一条/下一条历史，←/→ 跳到最旧/最新记录。PageUp/PageDown、Home/End 和滚轮滚动整页；窄窗口提供滚动条，点击正文聚焦所属区域。模态对话框和其他工作区保留自己的按键行为。

历史以 `(single_mode_chara_id, turn)` 为键，只保存完整训练画面；事件和比赛过程响应更新内部状态，保留最近的训练画面。同回合新的完整训练画面原位更新，浏览旧记录时收到新回合只标记未读；正在查看的最旧记录被淘汰时跳到最新记录。快照不可变，历史不写入磁盘。

配置通过 Host 的插件配置入口修改，点击“保存”才写入 `PluginData/FestaScenarioAnalyzer/settings.json`：

```json
{
  "historyLimit": 100
}
```

范围为 0–1000；0 关闭历史与方向键导航，保留最新画面。缩小上限时释放被淘汰的快照；重新开启历史不回填旧输出，下一份输出替换并释放未进入历史的画面快照。插件按需创建 workspace，卸载时释放已创建的面板及历史资源。

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

要求 .NET 10 SDK。生产项目只通过 `UmamusumeResponseAnalyzer Version="*" PrivateAssets="all"` 引用 Host，不依赖 Legend/Ramen，也不导出跨插件显示或快照 API。实际运行的 Host 必须包含 Festa 协议类型。底部滚动条的鼠标输入修复位于 Host 的任务栏事件处理，需使用包含该修复的 Host 构建；单独替换插件 ZIP 不包含此修复。

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

此次回放使用的本机名字库缺少支援卡 `30322`、`30323`。名称与支援类型依赖 Host 数据库；名称缺项时显示未知名称。原始包和账户数据不编入插件。

生成 Release ZIP（关闭自动部署）：

```powershell
dotnet build .\FestaScenarioAnalyzer.csproj -c Release -r win-x64 --self-contained false -p:PlatformTarget=AnyCPU -m:1 -nr:false `
  -p:GenerateUraPluginManifestOnBuild=true -p:PackageUraPluginOnBuild=true `
  -p:DeployUraPluginToLocalAppDataOnBuild=false
if ($LASTEXITCODE) { throw '插件打包失败。' }
```

产物为 `bin/Release/FestaScenarioAnalyzer.zip`，仅包含 manifest、插件 DLL 和日文资源 DLL；Host 和其他插件 DLL 不进入包。`Dependencies` 为空。

GitHub Actions 调用固定提交的 [URA plugin workflows](https://github.com/URA-Plugins/.github)，执行 Release 构建和 ZIP 校验。普通 push、pull request 和手动运行执行验证；推送 `v` 开头且数字部分与项目版本一致的 tag 后，通过验证才创建 GitHub Release。

上述校准使用实际抓包离线回放、合成 DTO 和本地终端 framebuffer；覆盖范围以所列样本为限。不包含最优教诲推荐、未来回合模拟或自动游戏操作。
