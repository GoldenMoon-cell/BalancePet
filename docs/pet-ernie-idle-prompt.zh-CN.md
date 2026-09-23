# Ernie 小病书灵「青绡」首张基准图
## 参考图

```text
素材/二次元形象/Ernie.jpg
```

参考图只用于保留蓝紫色长发、蓝紫与白色配色、安静可靠的气质、书灵/护理助手的角色方向和几何饰品轮廓。必须移除参考图中的轮椅、输液袋、医疗监护设备、温度数字、书堆、可读文字、平台标志和场景。

## GPT-Image-2 复制提示词

```text
Use case: illustration-story
Asset type: BalancePet desktop companion idle sprite, canonical identity reference
Input images: Image 1 is the character reference image; use it only for hair silhouette, blue-violet and white palette, calm caretaker personality, and geometric accessory cues. Do not copy its medical equipment, readable marks, logos, or background.
Scene/backdrop: no scene, one isolated character on a genuinely transparent background
Subject: Ernie 小病书灵「青绡」, a tiny friendly book-spirit AI assistant
Style/medium: clean high-quality chibi anime illustration, readable at small desktop-pet size, crisp linework, restrained highlights, softly layered blue-violet fabric and paper-like materials
Composition/framing: one complete full-body character centered in a square 1:1 canvas, front-facing with only a very slight three-quarter angle, calm neutral idle pose, hands close to the body, feet/body bottom fixed to a clear baseline, character height about 75% to 86% of the canvas, generous safe margin, no cropping
Lighting/mood: soft even studio light, quiet, reassuring, intelligent, gentle; no cast shadow, glow, halo, or environmental light
Color palette: blue-violet hair, cool indigo, clean white and pale blue, one restrained jade-green accent for the name Qingxiao; no saturated red
Materials/textures: simplified book-spirit silhouette with a small rounded book-page motif at the chest and one short jade-blue silk ribbon looped close to the body; rounded shapes, no thin dangling parts, no complex medical hardware
Identity anchors: long blue-violet hair simplified into a compact readable silhouette, calm blue eyes, small rounded book-page chest motif, jade-blue silk ribbon, modest white-and-indigo caretaker-inspired outfit without medical symbols or uniform badges
State: idle; relaxed shoulders and limbs, natural gentle expression, only a tiny breathing motion implied, no waving, walking, jumping, working, talking, or extra props
Text (verbatim): ""
Constraints: output one 2048x2048 PNG with true RGBA transparency and alpha 0 outside the character; keep a stable bottom anchor for future state images; preserve the same face, hair silhouette, palette, materials, outfit, ribbon, and chest motif for every later state; no text, no logo, no watermark
Avoid: white/gray/black background, checkerboard baked into the image, scenery, floor, cast shadow, glow, particles, smoke, speed lines, speech bubble, UI, border, grid, contact sheet, multiple characters, medical devices, wheelchair, IV bag, monitor, numbers, readable writing, platform logo, cropped body, extra limbs, floating character, detached ribbon, thin fragile strands
```

## 首张图验收重点

1. 角色必须是完整的小型桌宠，而不是参考图中的成人立绘缩小版。
2. 必须保留“蓝紫长发 + 白蓝服装 + 书页胸饰 + 青色丝带”四个身份锚点。
3. 参考图的医疗设备和 Logo 只能被移除，不能继续作为角色道具。
4. 背景必须是真透明 RGBA，不接受白底、棋盘格截图或带阴影的抠图。
5. 这张图只作为 `idle.png` 身份基准，确认后再用同一张图生成 `loading.png` 等其他 8 个状态。

## 项目暂存位置

首张图验收前暂存为：

```text
素材/Ernie/idle.png
```

确认角色后，再复制到：

```text
versions/csharp-wpf/assets/pets/ernie/idle.png
```
