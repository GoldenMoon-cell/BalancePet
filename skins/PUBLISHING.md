# Appearance artwork

`dist/pets/` in the main repository is where the packages are built from. This
folder holds the material that gets published to the separate appearance
repository, so that publishing is a copy rather than a second authoring step.

## What is here

| Path | Purpose |
| --- | --- |
| `README.md` | The appearance repository's front page. Written to be read there, so its links point at the main repository rather than at files beside it. |
| `catalog.json` | Generated. The index the online extension library reads, so an appearance can be installed from Settings instead of downloaded by hand. |

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
.\tools\build-skin-catalog.ps1 -Repository OWNER/REPO -ReleaseTag TAG
```

It reads each package's own manifest for the id, name and version, and computes the
SHA-256 from the file, so an entry cannot disagree with the artifact it points at.
The repository name and release tag are parameters because they are the only two
things that cannot be derived from the packages.

Regenerate it after rebuilding any package. A stale hash is worse than a missing
entry: the install downloads the whole archive and then fails.

## Publishing

Copy `README.md` to the appearance repository's default branch, then attach the
ZIPs from `dist/pets/` to a release. Nothing in this folder is read by the
application.

The release tag should match the package versions it carries, so that a package
downloaded from a release can be identified later.
