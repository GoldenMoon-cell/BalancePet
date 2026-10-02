# C# 宠物动作素材

为某个形象添加状态图时，按下面的目录和文件名放置透明 PNG。

> **注意**：主程序只自带 `_placeholder`（内置占位形象），其余形象一律以资源扩展包分发，
> 不随安装包发布。在这里新增目录只对**本机开发**有效；要让用上装到别人的机器上，必须按
> `docs/extension-spec/v1/` 打包并发布。`_placeholder` 是唯一保证存在的一套，窗口在没装
> 任何形象时画的就是它。

形象名称采用统一格式：

- `DeepSeek 小鲸鱼 / Whale-chan / 澜汐`
- `ChatGPT 小白龙 / ChatGPT White Dragon / 霁珑`
- `MiniMax 小海螺 / 绯音`
- `Gemini 小星猫 / 星璃`
- `Grok 小恶魔 / Grok Little Demon / 烬斧`
- `Claude 小书灵 / Claude Little Book Spirit / 丹笺`
- `Kimi 小棱镜 / Kimi Little Prism / 虹谱`
- `Qwen 小折扇 / Qwen Folding Fan / 绀华`
- `Ernie 小病书灵 / Ernie Little Book Spirit / 青绡`
- `GLM 小方灵 / GLM Little Square Spirit / 青棱`
- `GPT Image 2 小墨龙 / GPT Image 2 Ink Dragon / 玄珏`
- `Llama 小羊驼 / Llama Alpaca / 绒眠`

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

其他形象使用同样的文件名，目录名分别为 `chatgpt`、`minimax`、`gemini`、`grok`、`claude`、`kimi`、`qwen`、`ernie`、`glm`、`gpt-image2`、`llama`、`mimo` 和 `seedance`。图片必须是透明背景 PNG；建议所有状态使用相同的画布尺寸和角色锚点。

未提供的状态会优先回退到该形象的 `idle.png`，再回退到内置占位形象 `_placeholder`。占位形象按同一套规则补全，所以即使某个包只画了一半，窗口也不会空着。

每个目录下可以放一个可选的 `lines.json`，为该形象提供彩蛋文案；格式见 `docs/extension-spec/v1/README.md` 的「Optional lines」。没有这个文件时使用程序内置的中性文案。

需要动效时，在同一目录追加 `idle-2.png`、`idle-3.png`……最多到 `idle-8.png`，其余状态同理。序号必须连着，中间缺一个就到此为止。
