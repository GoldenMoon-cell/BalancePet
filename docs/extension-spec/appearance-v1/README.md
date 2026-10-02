# Appearance repository documents v1

Two documents are published from the appearance repository, and the application reads
them from a URL rather than from disk. This specification describes both. It is separate
from [Resource Extension Specification v1](../v1/README.md) because a package and a
repository index are different things: the package is what a user installs, and these
are how the application finds out what exists.

| Document | Read when |
| --- | --- |
| [`catalog.json`](#catalogjson) | The online library is opened. Lists installable appearances. |
| [`lines.json`](#linesjson) | Once per launch. Supplies what each appearance says. |

Both are fetched over the network and are therefore treated as untrusted: a wrong
document has to be recognisable as wrong rather than silently believed.

## `catalog.json`

```json
{
  "catalog": "balancepet.appearances",
  "schema_version": 1,
  "updated_at": "2026-10-02T06:07:20Z",
  "appearances": [
    {
      "id": "pet.deepseek",
      "type": "pet",
      "name": "DeepSeek 小鲸鱼「澜汐」",
      "name_en": "DeepSeek Whale \"Lanxi\"",
      "version": "1.1.0",
      "min_core_version": "0.5.0",
      "download_url": "https://github.com/OWNER/REPO/releases/download/skins-1.1.0/pet.deepseek-1.1.0.zip",
      "sha256": "dc39ed16e323e4e6ebab9a77ad556e903a5c25310892a9a581065410aabe6e65",
      "repository_url": "https://github.com/OWNER/REPO",
      "release_url": "https://github.com/OWNER/REPO/releases/tag/skins-1.1.0",
      "categories": ["appearance"]
    }
  ]
}
```

Rules:

- `catalog` is the document's identity and the reason the field exists. The main
  repository publishes a plugin catalog of otherwise the same shape — a version, an
  array, entries with names and hashes — so without an identity a document fetched from
  the wrong address would parse and produce a list that cannot be installed. A reader
  must reject a document whose `catalog` is not `balancepet.appearances`.
- `schema_version` must be `1`. A document declaring another version is rejected rather
  than guessed at.
- `sha256` is the digest of the artifact at `download_url`, lower-case hexadecimal. The
  host downloads the whole archive and verifies it before installing, so a stale digest
  turns an install into a failure after the download rather than into a wrong install.
  It is the reason this document is generated rather than written by hand.
- `version` is the package's own version and is what an installed copy is compared
  against to decide whether an update is offered. Publishing unchanged bytes under a new
  version therefore pushes an update that changes nothing.
- `id` must begin with `pet.`. The rest of the value is the package id, and the package's
  `style` — not its `id` — is the appearance identifier a saved setting stores.
- An entry whose `min_core_version` is newer than the running program is listed but not
  installable. It is shown rather than hidden so the reason is visible.
- At most 100 entries. A longer list is rejected as not being this document.
- Entries with a duplicate `id` are ignored after the first. A duplicate is a publisher's
  mistake, and the host cannot choose between them on the user's behalf.

The application also reads `name`, `name_en`, `description`, `description_en`, `author`
and `update_url` when present; see
[the catalog schema](catalog.schema.json) for their bounds. `update_url` is not used for
appearances — an installed appearance is updated from this catalog, because the catalog
already carries a newer version and its digest.

## `lines.json`

```json
{
  "schema_version": 1,
  "lines": {
    "deepseek": {
      "inactive": [ { "label": "澜汐在等你", "amount": "慢慢来", "hint": "需要时点我一下就好" } ],
      "bubble":   [ { "label": "澜汐在看着", "amount": "放心吧", "hint": "余额变动会告诉你" } ],
      "streak":   [ { "label": "被发现了", "amount": "眨眨眼", "hint": "连续互动彩蛋" } ],
      "touch": {
        "hair":  [ { "label": "被摸头了", "amount": "唔", "hint": "轻轻蹭了一下" } ],
        "mouth": [ { "label": "脸颊被碰到", "amount": "有点痒", "hint": "躲了一下" } ],
        "body":  [ { "label": "被戳到了", "amount": "在呢", "hint": "点击可以刷新余额" } ]
      }
    }
  }
}
```

Rules:

- Each value under `lines` has the shape defined by
  [Resource Extension Specification v1 → Optional lines](../v1/README.md#optional-lines),
  including its neutral fallback, its per-category independence, its 24-line ceiling and
  its rule that an unknown `schema_version` means the file is not read.
- The key is the appearance id — the same value a package declares as `style`, not the
  package `id`. A key that does not match an installed appearance is ignored.
- The document describes only the appearances its publisher ships. That is what keeps it
  from being a way for one publisher to speak for another's character.
- A document declaring an unknown `schema_version` replaces nothing, rather than
  replacing the lines with an empty set. The copies inside installed packages then stay
  in force, so the outcome is words that are merely older rather than an appearance that
  has lost its voice.
- This layer is optional and a host may have none. It exists because a package is almost
  entirely artwork, so correcting one word by republishing the package costs a download
  of megabytes to deliver a few hundred bytes.

### Precedence

1. The served document, when it names the appearance and can be read.
2. The `lines.json` inside the installed package.
3. The neutral set, which names nobody.

The package's copy is therefore not dead weight: it is what an installation that has
never reached the network reads.

## Publishing

Neither document is authored by hand. `tools/build-skin-catalog.ps1` derives the catalog
from the built packages, so an entry cannot disagree with the artifact it points at, and
`tools/build-appearance-lines.ps1` collects the lines from the per-appearance files. See
[skins/PUBLISHING.md](../../../skins/PUBLISHING.md) for the release cycle.
