# BalancePet 素材规格 v2（校正版）

## 为什么有这份文件

实测 `versions/csharp-wpf/assets/pets/` 下 12 套已交付素材后，发现早期文档写的规格和实际交付的画法**有三处不一致**。三份旧文档（`pet-prompt-library.zh-CN.md`、`pet-generation-prompts-1.0.0.zh-CN.md`、`csharp-art-pipeline.md`）里被改动的部分以此文件为准。

| 维度 | 旧文档写的 | 实际交付的 | 处置 |
| --- | --- | --- | --- |
| 画布 | 推荐 `2048x2048` | 9 套 `1024x1024`，3 套 `2048x2048` | **统一到 1024**，见下 |
| 构图 | 完整全身、脚底固定基线、角色占高 75%～86%、四周 8% 安全边距 | 贴边半身胸像，alpha 包围盒占画布 96%～100%，无脚无基线 | **以实际交付为准**，新图一律半身胸像贴边 |
| 角色方向 | 动物／器物吉祥物（小羊驼、小方灵、小墨龙、小乌鸦……） | 人类少女 Q 版（猫耳少女、女仆少女、折扇少女……） | **锚点全部重写**，见 `pet-generation-prompts-remaining-6.zh-CN.md` |

## 校正后的规格

| 项 | 值 |
| --- | --- |
| 画布 | `1024 x 1024`（1:1 正方形） |
| 色彩模式 | `RGBA`，透明像素 `Alpha = 0` |
| 背景 | 真透明。不接受白底、灰底、棋盘格，也不接受把棋盘格画进图里再抠 |
| 取景 | **贴边半身胸像**：从头顶到腰部或双手，角色充满画布，上下左右贴边，不留大面积空白 |
| 镜头 | 正面或轻微三分之二视角，角色居中 |
| 跨状态稳定性 | 九个状态的画布尺寸、头顶位置、镜头距离、身体下缘必须一致，避免切换时跳动 |
| 角色方向 | 由该角色的**参考立绘**驱动身份与配色，转成 Q 版人类少女半身像 |
| 状态文件 | `idle` / `loading` / `success` / `error` / `low` / `inactive` / `clicked` / `codex-working` / `codex-done`，共 9 个 |

## 为什么统一画布尺寸：是体积问题，不是观感问题

主程序的显示链路：

```xml
<!-- MainWindow.xaml -->
<Grid x:Name="PetVisualHost" Width="238" Height="238">
  <Image x:Name="PetImage" Stretch="Uniform" .../>
```

`Stretch="Uniform"` 把任意尺寸的方形画布**等比缩放**进固定的 238×238 宿主，所以 `2048x2048` 和 `1024x1024` 在屏幕上渲染结果完全相同，**切换形象不会跳变**。

那为什么还要统一？因为发布包体积：

| 画布 | 套数 | 单张均值 | 合计 |
| --- | --- | --- | --- |
| `2048x2048` | 3 套（chatgpt / deepseek / minimax） | 2.6 – 4.1 MB | 91.1 MB |
| `1024x1024` | 9 套 | 1.4 – 1.8 MB | 137.6 MB |
| | **12 套** | | **228.7 MB** |

把 3 套 2048 的降到 1024，可省下约 70 MB 发布包体积。缩放一律用 Lanczos，并采用**预乘 alpha** 再缩放——直接对 RGBA 做缩放会把透明像素下方的颜色渗到边缘，产生黑边。

> 另注：`MainWindow.xaml.cs` 的 `LoadPetVisual` 创建 `BitmapImage` 时没有设置 `DecodePixelWidth`，WPF 会按原始像素解码。显示尺寸只有 238 像素，解码 2048×2048 是 74 倍的浪费。若后续要再压内存与加载耗时，可在那里加一行 `source.DecodePixelWidth = 512;`，比继续缩小素材文件更有效。

## 生成参数（与现有素材一致）

| 项 | 值 |
| --- | --- |
| 模型 | `gpt-image-2-official` |
| 分辨率 | `1k` |
| 宽高比 | `1:1` |
| 输出格式 | `png` |
| 背景 | `transparent` |
| 审核强度 | `auto` |
| 图像数量 | `1` |
| 质量 | `medium` |

**全套必须一致**：中途换模型、把 `质量` 从 `medium` 改成 `high`、或改动主提示词措辞，都会让新图与已有 12 套接不上。

## 命名规范

形象 ID 一律小写，且与 `assets/pets/<id>/` 目录名严格一致：

```text
chatgpt  claude   deepseek  ernie    gemini   glm
gpt-image2  grok  kimi      llama    mimo     minimax
mistral  opencode perplexity qwen    rwkv     seedance
```

参考立绘放在 `素材/二次元形象/<id>/参考图.png`。已修正的历史不一致：

| 曾用名 | 规范名 | 出现在 |
| --- | --- | --- |
| `seedence` | `seedance` | `pet-generation-prompts-1.0.0.zh-CN.md` |
| `preplexity.avif` / `preplexity.png` | `perplexity.*` | `素材/二次元形象/` |
| `Ernie` / `GPTimage2` / `Llama` / `RWKV` | `ernie` / `gpt-image2` / `llama` / `rwkv` | `素材/二次元形象/` |

## 素材工具

```powershell
# 验收：RGBA、Alpha=0、画布一致性、贴边比例、白底与棋盘格检测
python tools/prepare-pet-assets.py --check

# 统一画布到 1024（先干跑，确认后加 --apply；会自动备份到 assets/pets/_backup-1024x1024/）
python tools/prepare-pet-assets.py --normalize 1024
python tools/prepare-pet-assets.py --normalize 1024 --apply

# 清除被烘焙进图片的棋盘格背景
python tools/prepare-pet-assets.py --remove-checkerboard --apply
```

## 相关文件

- 剩余 6 个形象的九状态提示词：`docs/pet-generation-prompts-remaining-6.zh-CN.md`
- 已交付 12 套的九状态清单：`docs/csharp-art-pipeline.md`
- 状态图存放目录：`versions/csharp-wpf/assets/pets/<id>/`
