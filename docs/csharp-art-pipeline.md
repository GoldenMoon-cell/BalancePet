> **⚠️ 已修正（校正于 2026-09）**
>
> 下文第 44 行"推荐 2048x2048 的方形画布"已不适用：实际交付为 `1024x1024` 的贴边半身胸像，
> 统一画布尺寸的目的是控制发布包体积，而非观感（`PetImage` 使用 `Stretch="Uniform"`）。
> 详见 [`pet-art-spec-v2.zh-CN.md`](./pet-art-spec-v2.zh-CN.md)。该行已在 2026-10 一并改写，
> 保留这段说明是为了让旧记录仍可追溯。

---

# C# 桌宠素材要求

C# WPF 版本从 `versions/csharp-wpf/assets/pets/<style>/<state>.png` 加载状态图。缺少某个状态时先回退到该形象的 `idle.png`，再回退到内置占位形象 `_placeholder`——它是唯一保证存在的一套，所以窗口不会空着。

> **注意**：主程序**只自带 `_placeholder`**。在这里新增目录只对**本机开发**有效；要让别人也拿得到，必须按 [资源扩展规范 v1](./extension-spec/v1/README.md) 打包并发布。当前名册与命名说明见 [`pet-roster.zh-CN.md`](./pet-roster.zh-CN.md)。

## 角色名称

完整的 18 个形象名册（含 ID、素材状态、命名说明）见 [`pet-roster.zh-CN.md`](./pet-roster.zh-CN.md)。本节曾只列出其中 8 个，容易让人以为其余的不存在，因此不再在这里重复维护——**两处名单必然会分叉**，名册是唯一的一份。

这些是本项目使用的非官方角色称呼，不代表 DeepSeek、OpenAI 或其他平台的官方称呼或角色。

## 文件名

每套形象可包含以下文件：

```text
idle.png          各状态首帧，九张必备
loading.png
success.png
low.png
error.png
clicked.png
codex-working.png
codex-done.png
inactive.png
```

此外还有两类**可选**文件：

```text
<state>-2.png … <state>-8.png   额外动画帧，序号从 2 起，断档即结束，最多八帧
lines.json                      该形象的台词，格式见资源扩展规范 v1 的「Optional lines」
```

`lines.json` 的修改**不走发布打包流程**：它由 `tools/build-appearance-lines.ps1` 汇总到形象仓库的在线文档，一次小提交即可生效，不需要重新发包，也不要为此提升形象包版本。改美术才需要重新打包，见 [skins/PUBLISHING.md](../skins/PUBLISHING.md)。

## 交付规则

- 使用 PNG，画布为 `1024x1024`。
- 所有状态保持相同画布尺寸、角色中心、头顶位置、镜头距离和底部裁切线，避免切换时跳动。
- 只放一个角色；不要文字、气泡、场景、阴影、光晕、Logo、水印或额外角色。
- 角色轮廓要清晰，缩小到桌宠尺寸后仍能识别脸部与主要饰品。
- 必须输出真正带 Alpha 通道的 RGBA PNG；透明像素必须为 `alpha=0`。
- 在生成服务中明确选择 **Transparent（透明背景）** 与 **PNG**。白底、灰底和棋盘格预览图均不能作为素材。
- 下载后确认文件模式为 `RGBA`。如果是 `RGB`，说明背景已烘焙到图中，应重新生成，不要尝试自动抠图。
- 动画帧要把动作画进画面本身：宿主只按固定间隔轮播，不施加任何位移或倾斜，因此想做出起伏就得逐帧画出来。

## 放入项目

将成品放入对应目录，例如：

```text
versions/csharp-wpf/assets/pets/deepseek/idle.png
versions/csharp-wpf/assets/pets/chatgpt/loading.png
versions/csharp-wpf/assets/pets/grok/codex-working.png
```

替换同名文件后重新执行发布打包脚本即可——**但仅限美术**。台词文件不走这条路，见上文。发布周期见 [skins/PUBLISHING.md](../skins/PUBLISHING.md)。

不要将 API Key、Authorization、生成服务密钥或桌面截图提交到仓库。
