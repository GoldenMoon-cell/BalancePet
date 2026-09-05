# C# 桌宠素材要求

C# WPF 版本从 `versions/csharp-wpf/assets/pets/<style>/<state>.png` 加载状态图。缺少某个状态时会回退到该形象的基础图。

## 角色名称

- `DeepSeek 小鲸鱼 / Whale-chan / 澜汐`
- `ChatGPT 小白龙 / ChatGPT White Dragon / 霁珑`
- `MiniMax 小海螺 / 绯音`
- `Gemini 小星猫 / 星璃`
- `Grok 小恶魔 / Grok Little Demon / 烬斧`

这些是本项目使用的非官方角色称呼，不代表 DeepSeek、OpenAI 或其他平台的官方角色。

## 文件名

每套形象可包含以下文件：

```text
idle.png
loading.png
success.png
low.png
error.png
clicked.png
codex-working.png
codex-done.png
inactive.png
```

## 交付规则

- 使用 PNG，推荐 2048x2048 的方形画布。
- 所有状态保持相同画布尺寸、角色中心、头顶位置、镜头距离和底部裁切线，避免切换时跳动。
- 只放一个角色；不要文字、气泡、场景、阴影、光晕、Logo、水印或额外角色。
- 角色轮廓要清晰，缩小到桌宠尺寸后仍能识别脸部与主要饰品。
- 必须输出真正带 Alpha 通道的 RGBA PNG；透明像素必须为 `alpha=0`。
- 在生成服务中明确选择 **Transparent（透明背景）** 与 **PNG**。白底、灰底和棋盘格预览图均不能作为素材。
- 下载后确认文件模式为 `RGBA`。如果是 `RGB`，说明背景已烘焙到图中，应重新生成，不要尝试自动抠图。

## 放入项目

将成品放入对应目录，例如：

```text
versions/csharp-wpf/assets/pets/deepseek/idle.png
versions/csharp-wpf/assets/pets/chatgpt/loading.png
versions/csharp-wpf/assets/pets/grok/codex-working.png
```

替换同名文件后重新执行发布打包脚本即可。不要将 API Key、Authorization、生成服务密钥或桌面截图提交到仓库。
