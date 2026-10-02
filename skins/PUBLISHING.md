# Appearance artwork

`dist/pets/` in the main repository is where the packages are built from. This
folder holds the material that gets published to the separate appearance
repository, so that publishing is a copy rather than a second authoring step.

## What is here

| Path | Purpose |
| --- | --- |
| `README.md` | The appearance repository's front page. Written to be read there, so its links point at the main repository rather than at files beside it. |
| `catalog.json` | Generated. The index the online extension library reads, so an appearance can be installed from Settings instead of downloaded by hand. |
| `lines.json` | Generated. Every appearance's lines in one document, so what a character says can be corrected without republishing its artwork. The application prefers it when it can reach the network and falls back to the copy inside the package when it cannot. |

## Animating an appearance

A state may publish extra frames beside it: `idle.png` is frame one, `idle-2.png`
is frame two, and so on up to `idle-8.png`. A gap ends the sequence, so frames
are read in order and the loop stops at the first missing number. Nothing is
required here — a state with no extra frames is drawn as a still image — and the
contract is written out in
[`docs/extension-spec/v1/README.md`](../docs/extension-spec/v1/README.md).

Movement has to be drawn into the artwork. The host cycles the frames in place
and applies no transform of its own, so an appearance cannot express a bounce or
a tilt by publishing one frame and a motion. `tools/generate-placeholder-pet.py`
is a worked example: it draws `idle` like a slow breath and `inactive` like a
sinking doze, both as ordinary frames.

## Regenerating the catalog

`catalog.json` is generated, not authored:

```powershell
.\tools\build-skin-catalog.ps1 -Repository OWNER/REPO
```
It reads each package's own manifest for the id, name and version, and computes the
SHA-256 from the file, so an entry cannot disagree with the artifact it points at.
The repository is a parameter because it is the one thing that cannot be derived
from the packages.

The release tag is derived per package as `skins-<version>`, so the packages that
did not change keep pointing at the release that already holds them. Pass
`-ReleaseTag` to force one tag for every entry, which is only right when they were
all rebuilt together.

Regenerate it after rebuilding any package. A stale hash is worse than a missing
entry: the install downloads the whole archive and then fails.

## Changing what an appearance says

Fixing a line does not need a new package. The lines are collected into one document
and served from this repository:

```powershell
# 1. Edit versions\csharp-wpf\assets\pets\<style>\lines.json

# 2. Rebuild the document and publish it to the default branch
.\tools\build-appearance-lines.ps1
```

That is the whole cycle. Installed copies pick the change up on their next refresh,
and nobody downloads artwork to receive it.

The file inside the package is still written, and is what an installation that has
never reached the network reads, so it must stay correct rather than being treated as
dead weight. The script rebuilds it from those files, which also means the document
cannot drift from them.

Do not reach for a new package version to change a line. A package is mostly artwork:
republishing one to deliver a few hundred bytes pushes every installation through a
download of megabytes. Measured on the published set, giving all fourteen appearances
their lines that way cost 147 MB of transfer for 36 KB of text.

## Updating one appearance

Redrawing a single state does not need a new release of the program, because the
artwork is not in the program any more. The whole cycle is:

```powershell
# 1. Replace the artwork in versions\csharp-wpf\assets\pets\<style>\<state>.png

# 2. Bump the version. Installed copies are offered an update by comparing this
#    with the version in their manifest, so leaving it alone means nobody is told.
.\tools\package-shipped-pets.ps1 -Version 1.0.1 -Style seedance

# 3. Delete the package this one replaces, then regenerate the catalog. The
#    generator refuses two packages with the same id rather than publishing a
#    catalog that cannot be acted on.
Remove-Item .\dist\pets\pet.seedance-1.0.0.zip
.\tools\build-skin-catalog.ps1 -Repository GoldenMoon-cell/BalancePet-Pets

# 4. Publish: a release tagged skins-1.0.1 carrying the new ZIP, and the updated
#    catalog.json on the default branch.
```

`-Style` takes one or more ids. Without it every appearance that is not kept is
rebuilt, which is what a first publication wants and what a redraw of one does not:
a new version number on unchanged artwork pushes an update to everyone who already
installed it.

## Publishing

Copy `README.md`, `catalog.json` and `lines.json` to the appearance repository's
default branch, then attach the ZIPs from `dist/pets/` to a release.

`catalog.json` is the one file the application reads from that repository, so it has
to be published rather than left here. It carries an identity of its own —
`"catalog": "balancepet.appearances"`, with the entries under `appearances` — because
the main repository's `plugin-catalog.json` is otherwise the same shape, and a
reader holding one of the two documents has no way to tell which it has. The two
catalogs are never merged: plugins and themes come from the main repository, and
appearances come from this one.

Nothing else in this folder is read by the application.

The release tag should match the package versions it carries, so that a package
downloaded from a release can be identified later. The catalog points at the
release, so republishing a catalog whose packages were rebuilt means republishing
the release too, or the hashes will disagree with the assets.
