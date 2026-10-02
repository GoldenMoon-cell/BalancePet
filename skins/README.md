# BalancePet Appearances

Appearance packages for [BalancePet](https://github.com/GoldenMoon-cell/BalancePet).

An appearance is the character BalancePet draws. Installing one adds it to the
appearance selector in Settings; it can then be switched, disabled and removed
without touching the application itself.

## Installing

1. Download the ZIP for the appearance you want from
   [Releases](../../releases).
2. In BalancePet, open **Settings → Extensions**.
3. Drag the ZIP onto the local extension area.

No restart is needed. The appearance appears in the selector immediately.

Removing an appearance works the same way, from the same page. The built-in
placeholder cannot be removed: the character *is* the window, so an installation
with nothing left to draw would show a blank window with no way back through the
interface. Removing every appearance you installed is fine — the placeholder is
what remains, and it is what the window shows before anything is installed.

## Available appearances

| Package | Appearance | Artwork |
| --- | --- | --- |
| `pet.deepseek` | DeepSeek 小鲸鱼「澜汐」 | DeepSeek Whale "Lanxi" |
| `pet.chatgpt` | ChatGPT 小白龙「霁珑」 | ChatGPT White Dragon "Jilong" |
| `pet.minimax` | MiniMax 小海螺「绯音」 | MiniMax Shell "Feiyin" |
| `pet.gemini` | Gemini 小星猫「星璃」 | Gemini Star Cat "Xingli" |
| `pet.grok` | Grok 小恶魔「烬斧」 | Grok Little Demon "Jinfu" |
| `pet.claude` | Claude 小书灵「丹笺」 | Claude Little Book Spirit "Danqian" |
| `pet.kimi` | Kimi 小棱镜「虹谱」 | Kimi Little Prism "Hongpu" |
| `pet.qwen` | Qwen 小折扇「绀华」 | Qwen Folding Fan "Ganhua" |
| `pet.ernie` | Ernie 小病书灵「青绡」 | Ernie Little Book Spirit "Qingxiao" |
| `pet.glm` | GLM 小方灵「青棱」 | GLM Little Square Spirit "Qingleng" |
| `pet.gpt-image2` | GPT Image 2 小墨龙「玄珏」 | GPT Image 2 Ink Dragon "Xuanjue" |
| `pet.llama` | Llama 小羊驼「绒眠」 | Llama Alpaca "Rongmian" |
| `pet.mimo` | MiMo 小兔码师「橙析」 | MiMo Bunny Coder "Chengxi" |
| `pet.seedance` | Seedance 小星晶「澄芽」 | Seedance Little Star Crystal "Chengya" |

Every appearance BalancePet draws is published here, including DeepSeek 小鲸鱼「澜汐」
and ChatGPT 小白龙「霁珑」, which the application carried inside its own installer
until v1.4.3. The only appearance that is not published is the built-in placeholder:
it is the shape the window draws when nothing is installed, so it has to be the one
appearance that cannot be missing.

Appearances the application used to bundle are published unchanged so that an
installation which upgrades keeps the character it was using: the `style` value in
each package is the same identifier the application has always stored, which is what
lets the setting keep resolving.

## Package format

The format is defined by the
[Resource Extension Specification v1](https://github.com/GoldenMoon-cell/BalancePet/blob/main/docs/extension-spec/v1/README.md).
In short, a package is a ZIP containing:

```text
manifest.json
assets/pets/<style>/idle.png
assets/pets/<style>/loading.png
assets/pets/<style>/success.png
assets/pets/<style>/low.png
assets/pets/<style>/error.png
assets/pets/<style>/clicked.png
assets/pets/<style>/codex-working.png
assets/pets/<style>/codex-done.png
assets/pets/<style>/inactive.png
```

All nine state images are required. They must be transparent RGBA PNGs sharing one
canvas; 238 × 238 matches the built-in appearances.

A resource package is never executed. The host reads PNG files and a manifest and
nothing else, so a package cannot contain code, and no art style, palette or
character direction is required — only the file contract above.

## Building a package

The packer lives in the main repository:

```powershell
.\tools\package-pet-extension.ps1 -SourceDirectory .\pet.example -OutputPath .\dist\pet.example-1.0.0.zip
```

## Contributing an appearance

Open an issue with a preview before investing in all nine states. The states are
not interchangeable: `low` and `error` are read at a glance and need to be
distinguishable without reading the bubble, and `inactive` is drawn dimmed, so it
should still read as the same character.

Do not submit artwork whose copyright or licence does not permit redistribution.
BalancePet's own character references are attributed in its
`THIRD_PARTY_NOTICES.md`; an appearance contributed here must be yours to license
or carry a licence that allows it.
