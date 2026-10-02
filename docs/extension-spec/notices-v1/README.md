# Changelog notices v1

`notices.json` is published from the main repository and tells the application what
changed **outside a release** — a specification gaining a section, a document being
corrected, published content being rewritten online.

It exists because everything else the program says travels in a release, and a release is
a version number, two artifacts, a changelog and a download. That price only makes sense
for a change to the program. A specification that gained a section is not a new version of
anything, so it went unannounced and the only way to learn about it was to happen to look
at the repository.

An entry here says *a document changed*. It never says *a new version exists* — that is
what the update check is for, and the two must not be confused, or the same news arrives
twice by two routes.

```json
{
  "schema_version": 1,
  "notices": [
    {
      "seq": 3,
      "date": "2026-10-02",
      "area": "规范",
      "title": "补充形象仓库文档规范",
      "summary": "形象目录 catalog.json 与在线台词 lines.json 此前只有实现、没有规范，现已补齐两份 schema。",
      "url": "https://github.com/GoldenMoon-cell/BalancePet/blob/main/docs/extension-spec/appearance-v1/README.md"
    }
  ]
}
```

## Fields

| Field | Required | Notes |
| --- | --- | --- |
| `seq` | yes | Positive integer, unique across the document, **strictly increasing with time**. This is the watermark: the reader records the highest number it has shown, and everything above it is new. |
| `date` | yes | `YYYY-MM-DD`. For display only — nothing is compared against it. |
| `area` | yes | A short category, shown beside the date: `规范`, `文档`, `在线内容`. Free text, kept short. |
| `title` | yes | One line, the thing that changed. |
| `summary` | no | A sentence or two of detail. This is where "what changed" is answered; a title alone rarely is. |
| `url` | yes | Absolute `https` address to the change itself — a commit, a file, a document. Opened by the shell when the user asks for it. |

## Rules

- `schema_version` must be `1`. A document declaring another version is not read at all,
  and the copy cached from the last good fetch stays in force: the outcome is notes that
  are merely older, never a window that has silently emptied itself.
- `seq` is what makes the ordering unambiguous. A date cannot: two notices published the
  same day would hide each other behind a comparison with day resolution, and an entry
  removed from the document would break a watermark recorded as "everything up to this
  id". A number that only ever goes up survives both. It is assigned by
  `tools/add-notice.ps1`, never by hand.
- A duplicate or non-positive `seq`, a missing required field, or a `url` that is not an
  absolute `https` address causes **that entry** to be dropped. The rest of the document
  is still read: one malformed entry is a publisher's typo, and discarding the whole
  changelog over it would hide every other note.
- At most 400 entries. A longer document is rejected as not being this file.
- Entries are written by hand. A file hash can say that something changed; it cannot say
  what, and "what" is the entire message. `tools/add-notice.ps1` drafts an entry from the
  commits since the last one so that writing it is a matter of editing one sentence.

## How a reader uses it

1. Load the cached copy, then fetch and replace it.
2. New entries are those with `seq` greater than the recorded watermark.
3. On a **first run** the watermark is empty, and it is set to the highest published `seq`
   without showing anything. Everything published before the program was installed has, by
   definition, already been read; announcing it would greet a new installation with months
   of history.
4. When there are new entries and the user has left notifications on, the pet mentions
   them once and the watermark advances to the highest `seq` seen.

The watermark lives in the application's settings, not in a file of its own, so it is
carried by the same export and reset by the same uninstall as everything else the user has
chosen.
