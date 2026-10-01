/**
 * BalancePet DSH bridge
 *
 * Reports DeepSeek Harness turn boundaries to the BalancePet desktop pet over
 * the local named pipe that BalancePet already listens on for third-party AI
 * clients (`\\.\pipe\BalancePet.Task.v1`).
 *
 * Design rules (a Cordis plugin runs inside the harness host process, so this
 * code must never be able to disturb it):
 *
 *   - every callback is wrapped in try/catch and never rethrows;
 *   - `apply()` never throws, whatever the context looks like;
 *   - the pipe connection is made per event and is entirely best-effort:
 *     BalancePet simply not running is a normal condition, not an error;
 *   - the plugin holds no timers and no long-lived handles.
 *
 * The plugin is intentionally dependency-free so it can be installed from a
 * local directory with `pnpm add file:` while offline.
 */

import { appendFileSync, mkdirSync, readFileSync, statSync, truncateSync } from 'node:fs';
import { connect } from 'node:net';
import { join } from 'node:path';

const PIPE_NAME = '\\\\.\\pipe\\BalancePet.Task.v1';
const PROVIDER = 'DeepSeek Harness';
const MAX_MESSAGE_BYTES = 4096;
const MAX_LOG_BYTES = 256 * 1024;
const CONNECT_TIMEOUT_MS = 1500;
const SEND_ATTEMPTS = 3;
const SEND_RETRY_DELAY_MS = 350;
const DEBUG = process.env.DSH_BRIDGE_DEBUG === '1';
/**
 * When set, reports are logged instead of sent. The self-test uses this so
 * running it never writes synthetic usage into the user's real history.
 */
const DRY_RUN = process.env.DSH_BRIDGE_DRY_RUN === '1';

function debug(line) {
  if (DEBUG) log(`debug: ${line}`);
}

/** Current turn key per session, so a stop reuses the id its start announced. */
const openTurns = new Map();

function logDirectory() {
  const base = process.env.LOCALAPPDATA || process.env.TEMP || '.';
  return join(base, 'BalancePet');
}

function log(line) {
  try {
    const directory = logDirectory();
    mkdirSync(directory, { recursive: true });
    const file = join(directory, 'dsh-bridge.log');
    try {
      if (statSync(file).size > MAX_LOG_BYTES) truncateSync(file, 0);
    } catch {
      // Missing file is the common case.
    }
    appendFileSync(file, `${new Date().toISOString()} ${line}\n`, 'utf8');
  } catch {
    // Diagnostics must never affect the host.
  }
}

function sessionIdOf(session) {
  if (session === null || session === undefined) return '';
  for (const candidate of [session.id, session.sessionId, session.session?.id]) {
    if (typeof candidate === 'string' && candidate.length > 0) return candidate;
  }
  return '';
}

function dshHome() {
  const configured = process.env.DSH_HOME;
  if (typeof configured === 'string' && configured.length > 0) return configured;
  return join(process.env.USERPROFILE || process.env.HOME || '.', '.dsh');
}

function toCount(value) {
  return typeof value === 'number' && Number.isFinite(value) && value >= 0 ? value : 0;
}

/**
 * Reads the token and model figures the harness keeps for a session.
 *
 * The harness measures tokens only — it has no price table and no currency — so
 * this reports usage and never a cost. BalancePet's own relay backfill supplies
 * a cost when the traffic went through a relay; for an official account there is
 * no per-request charge to report, and inventing one would be worse than
 * leaving it unreported.
 *
 * Returns null when the cache is missing or unreadable, which is normal for a
 * brand new or already-disposed session: the caller then reports no usage rather
 * than a wrong number.
 */
function readUsageSnapshot(sessionId) {
  try {
    const file = join(dshHome(), 'storages', 'session_projcache', 'sessions', `${sessionId}.json`);
    const rows = JSON.parse(readFileSync(file, 'utf8'))?.record?.rows;
    if (rows === null || rows === undefined) return null;

    const totals = rows.tokenUsage?.val?.totals ?? {};
    const lastUsed = rows.modelSelection?.val?.lastUsed ?? {};
    const stats = rows.sessionStats?.val ?? {};

    return {
      input: toCount(totals.uncachedInputTokens),
      output: toCount(totals.outputTokens),
      cacheRead: toCount(totals.cacheReadTokens),
      cacheWrite: toCount(totals.cacheWriteTokens),
      steps: toCount(stats.steps),
      model: typeof lastUsed.model === 'string' ? lastUsed.model : '',
      reasoning: typeof lastUsed.reasoningEffort === 'string' ? lastUsed.reasoningEffort : '',
    };
  } catch {
    return null;
  }
}

/**
 * Builds the usage block for one finished turn, or null when nothing is worth
 * reporting.
 *
 * Token counts are per-turn deltas of a figure that is cumulative for the whole
 * session, so a turn whose start was never observed — the plugin loaded
 * mid-turn, or the harness restarted — reports no counts instead of the entire
 * session's total. Model and reasoning effort are not cumulative and are
 * reported whenever known. A cost is never included: see readUsageSnapshot.
 */
function buildUsage(baseline, sessionId, startedMs) {
  const usage = {};
  const now = readUsageSnapshot(sessionId);

  if (now !== null) {
    if (now.model.length > 0) usage.model = now.model;
    if (now.reasoning.length > 0) usage.reasoning_effort = now.reasoning;
    if (baseline !== null) {
      usage.input_tokens = Math.max(0, now.input - baseline.input);
      usage.output_tokens = Math.max(0, now.output - baseline.output);
      usage.cache_read_tokens = Math.max(0, now.cacheRead - baseline.cacheRead);
      usage.cache_write_tokens = Math.max(0, now.cacheWrite - baseline.cacheWrite);
      usage.steps = Math.max(0, now.steps - baseline.steps);
    }
  }

  if (startedMs > 0) usage.duration_ms = Math.max(0, Date.now() - startedMs);

  return Object.keys(usage).length > 0 ? usage : null;
}

/**
 * One connection attempt. Always resolves; never rejects.
 */
function attemptSend(text) {
  return new Promise((resolve) => {
    let settled = false;
    const finish = (reason) => {
      debug(`finish(${reason}) settled=${settled}`);
      if (settled) return;
      settled = true;
      try {
        socket.destroy();
      } catch {
        // Ignore.
      }
      resolve(reason);
    };

    const socket = connect(PIPE_NAME);
    socket.setTimeout(CONNECT_TIMEOUT_MS, () => finish('timeout'));
    socket.on('error', (error) => finish(`error:${error?.code ?? 'unknown'}`));
    socket.on('close', () => finish('closed'));
    socket.on('connect', () => {
      try {
        socket.end(text, () => finish('sent'));
      } catch {
        finish('write-failed');
      }
    });
  });
}

/**
 * Sends one newline-terminated JSON message to BalancePet, retrying briefly.
 *
 * The retry matters: BalancePet serves the pipe from a pool that is recreated
 * between connections, so a message sent immediately after a previous one can
 * transiently fail with ENOENT even though the pet is running.
 *
 * Resolves either way: the pet not being installed or not running is an
 * expected outcome, not an error.
 */
async function sendToPet(payload) {
  let text;
  try {
    text = `${JSON.stringify(payload)}\n`;
  } catch {
    return 'encode-failed';
  }
  if (Buffer.byteLength(text, 'utf8') > MAX_MESSAGE_BYTES) return 'too-large';
  if (DRY_RUN) {
    debug(`dry-run payload ${text.trim()}`);
    return 'dry-run';
  }

  let outcome = '';
  for (let attempt = 1; attempt <= SEND_ATTEMPTS; attempt += 1) {
    outcome = await attemptSend(text);
    if (outcome === 'sent') return 'sent';
    debug(`send attempt ${attempt} of ${SEND_ATTEMPTS}: ${outcome}`);
    if (attempt < SEND_ATTEMPTS) {
      await new Promise((resolve) => setTimeout(resolve, SEND_RETRY_DELAY_MS));
    }
  }
  return outcome;
}

export default {
  name: 'balancepet-dsh-bridge',

  apply(ctx) {
    try {
      if (ctx === null || ctx === undefined || typeof ctx.on !== 'function') {
        log('apply: ctx.on is unavailable, bridge disabled');
        return;
      }

      ctx.on('session/event', (session, event) => {
        try {
          const type = event?.type;
          if (type !== 'turn/start' && type !== 'turn/end') return;

          const sessionId = sessionIdOf(session);
          if (sessionId.length === 0) {
            log(`${type}: no session id, skipped`);
            return;
          }

          const key = `dsh:${sessionId}`;
          let turnId;
          let baseline = null;
          let startedMs = 0;

          if (type === 'turn/start') {
            turnId = `turn:${event?.seq ?? Date.now()}`;
            // The cached usage totals are cumulative for the whole session, so the
            // figure reported when the turn ends is this reading subtracted from
            // the later one.
            baseline = readUsageSnapshot(sessionId);
            startedMs = Date.now();
            openTurns.set(key, { turnId, baseline, startedMs });
          } else {
            const open = openTurns.get(key);
            turnId = open?.turnId ?? `turn:${event?.seq ?? Date.now()}`;
            baseline = open?.baseline ?? null;
            startedMs = open?.startedMs ?? 0;
            openTurns.delete(key);
          }

          const payload = {
            state: type === 'turn/start' ? 'start' : 'stop',
            sessionId: key,
            turnId,
            provider: PROVIDER,
          };

          log(`${type} session=${sessionId} turn=${turnId}`);

          if (type === 'turn/end') {
            const usage = buildUsage(baseline, sessionId, startedMs);
            if (usage !== null) {
              payload.usage = usage;
              log(`  usage ${JSON.stringify(usage)}`);
            } else {
              log('  usage unavailable');
            }
          }

          sendToPet(payload).then(
            (outcome) => {
              if (outcome !== 'sent') log(`${type}: pipe ${outcome}`);
            },
            (error) => {
              // sendToPet resolves on every path it controls; a rejection here
              // means an unexpected failure, so record it rather than hide it.
              log(`${type}: pipe threw ${error?.message ?? String(error)}`);
            },
          );
        } catch (error) {
          log(`handler failed: ${error?.message ?? String(error)}`);
        }
      });

      log('bridge active');
    } catch (error) {
      log(`apply failed: ${error?.message ?? String(error)}`);
    }
  },
};
