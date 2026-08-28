# C# 宠物动作素材

为某个形象添加状态图时，按下面的目录和文件名放置透明 PNG：

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

ChatGPT 形象使用同样的文件名，将目录名换成 `chatgpt`。图片必须是透明背景 PNG；建议所有状态使用相同的画布尺寸和角色锚点。未提供的状态会回退到 `assets/pet.png` 或 `assets/chatgpt-dragon.png`。
