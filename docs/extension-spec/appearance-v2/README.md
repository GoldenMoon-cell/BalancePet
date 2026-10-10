# Appearance lines v2

This version changes only the optional `lines.json` document. It does not change the
appearance package manifest or catalog format; a v1 appearance package can contain a
`lines.json` with `schema_version: 2`.

Schema v1 remains supported for third-party packages and older served catalogs. In v2,
each line may include an `en` object with `label`, `amount`, and `hint`. Those strings are
a character-voiced English adaptation for the English UI, not a literal translation. When
`en` is absent or incomplete, the host keeps the original line text.

`label` and `amount` are each limited to 40 characters; `hint` is limited to 120. The
category and touch-zone rules, line-count limits, and fallback behavior are otherwise
unchanged from [appearance lines v1](../appearance-v1/README.md). The machine-readable
contract is [`lines.schema.json`](lines.schema.json).
