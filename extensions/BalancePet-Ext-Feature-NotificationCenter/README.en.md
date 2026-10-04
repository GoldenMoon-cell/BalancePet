# BalancePet Notification Center

A read-only feature extension for BalancePet: it collects the scrubbed summaries from the
pet's bubbles and shows them by category -- balance, refresh, tasks, accounts, system and
interaction. The window uses the same Fluent visual language as BalancePet and follows the
Windows light or dark app mode.

## Installing and running

Installing the extension enables it and starts it in the background. To read the history,
open **Notification Center** from BalancePet's context menu. (The full split needs
BalancePet 1.0.0-dev.30 or later.) The extension stores scrubbed messages only and never
raises a notification of its own.

## The ring

- Holding `Shift` with the pointer over the pet shows four fixed slots: balance, the current
  sign-in, the task state, and the running version.
- The balance slot reads the newest valid value of each of "account balance / previous
  balance / this run's spend" and shows them together rather than choosing one; the sign-in
  slot merges official account, official API and CC Switch sign-in events; the task slot
  switches between working and stopped; the version slot reads the version that is running.
- The four states sit close to the pet as text with no background and fade in around the
  orbit, evenly spaced along a visible arc, each with a short rule beneath it as a divider.
- With the pet at the centre of the screen the ring is an even four-way split; at a corner it
  is a single half-ellipse facing into the screen; other edges use evenly spaced sectors, and
  a change of position slides rather than jumps. The blocks are compact and the rule follows
  the detail text closely.
- Before each showing, the extension samples the desktop behind each block on this machine
  and picks light or dark text, an opposing outline and divider colours by relative
  brightness. It does not keep the sampled pixels and does not keep sampling while shown.
- Releasing `Shift` or moving the pointer away fades the blocks out in order.

## What the application still does

- Refresh progress, refresh failures, cooldown notices and the pet's easter eggs stay as the
  application's own bubbles; this run's spend is merged into the balance slot, and the
  scrubbed history is kept.
- Closing the panel with its close button only hides it: the extension keeps listening in the
  background. Disabling, removing or quitting the extension restores the application's own
  bubbles.

## Data and privacy

The extension reads two files, and only their scrubbed contents:
`%LOCALAPPDATA%\BalancePet\notification-events.ndjson`, its rotated files, and the current
state snapshot `notification-state.v1.json` written by the application. Records contain no
API token, no prompts, no model responses, no raw requests and no raw responses.