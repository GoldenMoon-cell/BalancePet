# BalancePet 形象名册

共 **18** 个形象。本文件由项目实际文件生成，名字取自各形象的 `素材/二次元形象/<id>/提示词.md`。

## 18 个形象

| ID | 名字 | 九状态素材 | 参考图 | 命名说明 |
| --- | --- | --- | --- | --- |
| `chatgpt` | ChatGPT 小白龙「霁珑」 | **9/9** | 有 | 原文 |
| `claude` | Claude 小书灵「丹笺」 | **9/9** | 有 | 原文 |
| `deepseek` | DeepSeek 小鲸鱼「澜汐」 | **9/9** | 有 | 原文 |
| `ernie` | Ernie 小病书灵「青绡」 | **9/9** | 有 | 原文 |
| `gemini` | Gemini 小星猫「星璃」 | **9/9** | 有 | 原文 |
| `glm` | GLM「青棱」 | **9/9** | 有 | 删去与实际画面不符的「小方灵」 |
| `gpt-image2` | GPT Image 2「玄珏」 | **9/9** | 有 | 删去与实际画面不符的「小墨龙」 |
| `grok` | Grok 小恶魔「烬斧」 | **9/9** | 有 | 原文 |
| `kimi` | Kimi 小棱镜「虹谱」 | **9/9** | 有 | 原文 |
| `llama` | Llama「绒眠」 | **9/9** | 有 | 删去与实际画面不符的「小羊驼」 |
| `mimo` | MiMo 小兔助手「橙析」 | **9/9** | 有 | 文档作「小兔码师」，替换后确认沿用；折耳造型 |
| `minimax` | MiniMax 小海螺「绯音」 | **9/9** | 有 | 原文 |
| `mistral` | Mistral 小猫骑士「麦霜」 | 尚未制作 | 有 | 原文 |
| `opencode` | OpenCode 小码灵「墨枢」 | 尚未制作 | 有 | 原文 |
| `perplexity` | Perplexity 小向导「青鉴」 | 尚未制作 | 有 | 文档作「小探灯」，替换后确认沿用 |
| `qwen` | Qwen 小折扇「绀华」 | **9/9** | 有 | 原文 |
| `rwkv` | RWKV 小夜鸦「夜翎」 | 尚未制作 | 有 | 文档作「小乌鸦」，替换后确认沿用 |
| `seedance` | Seedance 小星芽「澄芽」 | **9/9** | 有 | 文档作「小星晶」，替换后确认沿用；文档 ID 曾误写 seedence |

## 命名说明

18 个名字里有 **11 个**与早期文档完全一致：

```text
chatgpt   ChatGPT 小白龙「霁珑」      deepseek  DeepSeek 小鲸鱼「澜汐」
claude    Claude 小书灵「丹笺」        ernie     Ernie 小病书灵「青绡」
gemini    Gemini 小星猫「星璃」        grok      Grok 小恶魔「烬斧」
kimi      Kimi 小棱镜「虹谱」          minimax   MiniMax 小海螺「绯音」
mistral   Mistral 小猫骑士「麦霜」     opencode  OpenCode 小码灵「墨枢」
qwen      Qwen 小折扇「绀华」
```

**3 个删去了词缀**（`glm`、`gpt-image2`、`llama`）：早期文档写作「小方灵（几何精灵）」「小墨龙」「小羊驼」，但实际交付的画是**人类少女**，保留动物／器物名会与画面直接矛盾，因此只留「名字」。
如需补一个词缀，请按实际形象另取（三者分别是黑发狐耳少女、龙角龙尾少女、白发羊驼耳少女）。

**4 个替换了用词**（`mimo`、`perplexity`、`rwkv`、`seedance`）：文档原作「小兔码师」「小探灯」「小乌鸦」「小星晶」，在撰写提示词锚点时改为现名，已确认沿用。

## ID 规范

ID 一律小写，且与 `versions/csharp-wpf/assets/pets/<id>/` 目录名严格一致：

```text
chatgpt  claude   deepseek  ernie    gemini   glm
gpt-image2  grok  kimi      llama    mimo     minimax
mistral  opencode perplexity qwen    rwkv     seedance
```

## 已知的历史拼写问题

| 曾用写法 | 规范写法 | 出现在 |
| --- | --- | --- |
| `seedence` | `seedance` | `pet-generation-prompts-1.0.0.zh-CN.md`（已修正） |
| `preplexity.avif` / `preplexity.png` | `perplexity.*` | `素材/二次元形象/`（已修正） |
| `Ernie` / `GPTimage2` / `Llama` / `RWKV` | `ernie` / `gpt-image2` / `llama` / `rwkv` | `素材/二次元形象/`（已修正） |

## 文件位置

```text
参考立绘   素材/二次元形象/<id>/参考图.png
提示词     素材/二次元形象/<id>/提示词.md
九状态素材 versions/csharp-wpf/assets/pets/<id>/<state>.png
```

九状态文件名固定为：

```text
idle  loading  success  error  low  inactive  clicked  codex-working  codex-done
```

<!-- 生成于 2026-02-14，mimo 完成时更新 -->
