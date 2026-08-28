# C# 桌宠美术交付流程

C# 版本直接读取 `versions/csharp-wpf/assets/pets/<style>/<state>.png`。Python 和 Electron 目录只作为历史参考，不参与新素材加载。

## 你需要提供什么

可以在 APIMart 使用 `gpt-image-2` 生成图片，然后把生成结果直接发到当前对话，或放在桌面/Downloads 后告诉我文件路径。不要把 APIMart API Key、Authorization 或其他令牌发到对话中，也不要把密钥写进工作区。

推荐输出：

- PNG，透明背景；如果生成器只能输出纯色背景，也可以先发给我处理。
- 方形 1:1，优先 2048x2048；1024x1024 也可以用于测试。
- 单个角色、居中，不要文字、气泡、场景、阴影、光晕或其他角色。
- DeepSeek 小鲸鱼沿用当前上半身构图：头顶呆毛不能裁切，底部裁切线固定在胸前蝴蝶结以下。
- 所有状态保持相同画布尺寸、底部裁切线、身体中心和镜头距离，避免动画时角色漂移或跳动。

## 当前首选：DeepSeek 小鲸鱼

现有 DeepSeek 小鲸鱼已经与 C# 版本的轮廓、裁切和配色匹配，优先为它生成状态图。参考图使用工作区里的 [pet.png](../versions/csharp-wpf/assets/pet.png)，不要使用聊天截图；这张图带透明通道，适合作为 APIMart 的参考图。

上传 `pet.png` 后，先用下面的提示词生成高清基础图。如果生成结果改变了发型、蝴蝶结、眼睛或耳朵，就不要采用，继续以原图为参考重试。

```text
请以上传的 DeepSeek Q 版小鲸鱼图片为唯一角色参考，生成同一角色的高清桌宠基础图。必须保留：蓝紫色短发和长发边缘、白色女仆头饰、侧面小鲸鱼耳鳍、蓝色蝴蝶结、深蓝蝴蝶结领饰、浅蓝大眼睛、圆润脸型、深蓝描边和柔和赛璐璐上色。
构图：只显示角色上半身，正面略微朝向镜头，角色居中，头顶呆毛完整可见，底部自然裁切在胸前蝴蝶结以下；方形 1:1 画布，角色中心和底部裁切线固定，四周保留透明边距。
画风：干净的日系 Q 版动漫插画，清晰深色线稿，柔和蓝紫配色，边缘锐利，高分辨率，缩小到约 230 像素仍能看清眼睛和蝴蝶结。
输出：2048×2048 PNG，透明背景。
严格限制：只出现一个 DeepSeek 小鲸鱼角色；不要改变角色身份、发型、眼睛、耳鳍、头饰、蝴蝶结、服装和配色；不要文字、气泡、商标、Logo、场景、桌面背景、阴影、光晕、水印、额外角色、漂浮装饰或纯色背景；不要模糊、锯齿、过度锐化或裁切头顶。
```

这张高清结果可以作为新的 `idle.png`；如果现有 `pet.png` 的角色一致性更好，也可以继续把现有 `pet.png` 作为全部状态图的参考图。

## 基础图提示词（GPT 可选角色）

```text
Use case: stylized-concept
Asset type: Windows desktop pet base character
Primary request: 生成一个原创的 ChatGPT 风格 Q 版桌宠角色，作为余额监控桌宠的统一角色参考图。角色是小型、完整全身、居中的二次元 chibi coding companion，头部偏大，身体简洁，表情温和专注，适合缩小到约 230 像素显示。
Subject: 单个原创 Q 版助手角色，圆润轮廓，大眼睛，简洁的象牙白服装，深灰或炭黑发色，一个低饱和薄荷色小配饰；不要官方 OpenAI 标志、文字或可识别商标。
Style/medium: clean anime chibi illustration, crisp cel shading, clean dark outline, high-resolution raster art
Composition/framing: square 1:1 canvas, full body, centered, feet visible, generous transparent margin, stable vertical baseline
Lighting/mood: soft even studio lighting, calm, attentive, friendly
Constraints: transparent background, no scenery, no floor shadow, no text, no speech bubble, no watermark, no detached effects, preserve clear silhouette and readable face at small size
Avoid: cropped limbs, extra characters, busy costume details, blur, glow, neon cyberpunk, blue-haired maid styling, logos, wordmarks, letters GPT
```

## 状态图提示词

以接受的 DeepSeek 基础图作为唯一角色参考，每个状态单独生成一张，保持角色身份、画布尺寸、底部裁切线和镜头距离完全一致。DeepSeek 图片是上半身构图，因此不要要求完整全身或脚底。

| 文件名 | 状态要求 |
| --- | --- |
| `idle.png` | 安静站立或轻微呼吸，轻微眨眼，表情放松，动作幅度很小。 |
| `loading.png` | 正在认真处理任务，身体微微前倾，眼神专注，可以有轻微抬手动作，不要速度线。 |
| `success.png` | 查询成功，露出温和满意的笑容，姿态舒展，保持脚底位置不变。 |
| `low.png` | 余额偏低，表情担心但可爱，肩膀略收，使用姿态表达情绪，不要红色叉号或文字。 |
| `error.png` | 查询失败，困惑或轻微沮丧的表情，可以有贴近角色的细小泪光，不要错误图标和文字。 |
| `clicked.png` | 被点击后的 Q 弹反馈，身体轻微压扁、脸颊或发梢有细小回弹感，不要夸张变形。 |
| `codex-working.png` | 陪伴 Codex 工作，专注思考或轻轻点头，姿态稳定，不要出现代码、界面或文字。 |
| `codex-done.png` | Codex 任务完成，放松、安心、带一点庆祝感，但不添加彩带、文字或漂浮图标。 |
| `inactive.png` | 长时间未操作，姿态稍微慵懒，眼睛半闭，整体低能量但仍保持清晰轮廓。 |

每条状态提示词末尾都加：

```text
同一角色身份与基础图完全一致；透明背景；上半身构图；方形 1:1；头顶呆毛不裁切；底部裁切线、身体中心和镜头距离与基础图完全对齐；无文字、无场景、无阴影、无光晕、无水印、无额外角色。
```

## 交给我之后

我会按以下顺序处理，不直接覆盖现有可用图片：

1. 检查图片尺寸、透明通道、边缘杂色和是否有裁切。
2. 统一画布尺寸、角色比例和脚底锚点。
3. 以版本化文件放入 `assets/pets/deepseek/` 或 `assets/pets/chatgpt/`。
4. 用 C# Release 构建确认素材会复制到 exe 旁边。
5. 启动测试，确认缺少的状态仍能回退到基础图。

如果只生成了一张图，先告诉我它是 `idle` 还是基础图；其余状态会继续使用现有基础图，不会影响程序运行。
