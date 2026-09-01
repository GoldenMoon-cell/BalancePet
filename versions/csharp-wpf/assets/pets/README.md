# C# 宠物动作素材

为某个形象添加状态图时，按下面的目录和文件名放置透明 PNG：

形象名称采用统一格式：

- `DeepSeek 小鲸鱼 / Whale-chan / 澜汐`
- `ChatGPT 小龙 / ChatGPT Dragon / 霁珑`
- `MiniMax 小海螺 / 绯音`
- `Gemini 小星猫 / 星璃`

这里的英文名和二字文化名是 BalancePet 的非官方角色称呼。

```text
assets/pets/deepseek/idle.png
assets/pets/deepseek/loading.png
assets/pets/deepseek/success.png
assets/pets/deepseek/low.png
assets/pets/deepseek/error.png
assets/pets/deepseek/clicked.png
assets/pets/deepseek/codex-working.png
assets/pets/deepseek/codex-done.png
assets/pets/deepseek/inactive.png
```

ChatGPT 形象使用同样的文件名，将目录名换成 `chatgpt`；MiniMax 和 Gemini 分别使用 `minimax`、`gemini` 目录。图片必须是透明背景 PNG；建议所有状态使用相同的画布尺寸和角色锚点。未提供的状态会优先回退到该形象的 `idle.png`，再回退到 `assets/pet.png` 或 `assets/chatgpt-dragon.png`。
