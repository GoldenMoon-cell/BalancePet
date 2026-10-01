/**
 * Local harness for tools/dsh-bridge — verifies the plugin contract and the
 * turn/start + turn/end handling without loading anything into DeepSeek Harness.
 *
 * Run: node <this file>
 */

import { pathToFileURL } from 'node:url';
import { mkdirSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { join } from 'node:path';

// Never send anything: a live BalancePet would record the synthetic turns as
// real usage and pollute the user's history. Dry-run still exercises the whole
// handler, the payload construction and the retry bookkeeping.
process.env.DSH_BRIDGE_DRY_RUN = '1';
import { createRequire } from 'node:module';

const require = createRequire(import.meta.url);
const pluginPath = process.argv[2];
if (!pluginPath) {
  console.error('usage: node dsh-bridge-selftest.mjs <path-to-lib/index.js>');
  process.exit(2);
}

const logFile = join(process.env.LOCALAPPDATA ?? process.env.TEMP, 'BalancePet', 'dsh-bridge.log');
try {
  rmSync(logFile);
} catch {
  // Not present.
}

const failures = [];
const check = (name, condition, detail) => {
  if (condition) {
    console.log(`  PASS  ${name}`);
  } else {
    failures.push(name);
    console.log(`  FAIL  ${name}${detail ? ` — ${detail}` : ''}`);
  }
};

console.log('1) module contract');
const module = await import(pathToFileURL(pluginPath).href);
const plugin = module.default;
check('default export exists', plugin !== undefined && plugin !== null);
check('default.apply is a function', typeof plugin?.apply === 'function');
check('name is balancepet-dsh-bridge', plugin?.name === 'balancepet-dsh-bridge', `got ${plugin?.name}`);

console.log('2) apply() registers a session/event listener');
let listener;
let eventName;
const fakeCtx = {
  on(name, handler) {
    eventName = name;
    listener = handler;
    return () => {};
  },
};
plugin.apply(fakeCtx);
check("subscribed to 'session/event'", eventName === 'session/event', `got ${eventName}`);
check('handler is callable', typeof listener === 'function');

console.log('3) apply() tolerates a hostile context');
for (const [label, ctx] of [
  ['undefined', undefined],
  ['null', null],
  ['no on()', {}],
  ['on() is not a function', { on: 42 }],
]) {
  let threw = false;
  try {
    plugin.apply(ctx);
  } catch {
    threw = true;
  }
  check(`apply(${label}) does not throw`, !threw);
}

console.log('4) listener tolerates hostile events');
const session = { id: 'session-selftest' };
for (const [label, s, e] of [
  ['undefined event', session, undefined],
  ['null event', session, null],
  ['no type', session, {}],
  ['unknown type', session, { type: 'step/start' }],
  ['null session', null, { type: 'turn/start' }],
  ['session without id', {}, { type: 'turn/start' }],
]) {
  let threw = false;
  try {
    listener(s, e);
  } catch {
    threw = true;
  }
  check(`listener(${label}) does not throw`, !threw);
}

console.log('5) turn/start then turn/end');
listener({ id: 'session-selftest' }, { type: 'turn/start', seq: 5 });
listener({ id: 'session-selftest' }, { type: 'turn/end', seq: 42 });
await new Promise((resolve) => setTimeout(resolve, 2500));

console.log('6) per-turn usage is a delta of the session totals');

// A throwaway DSH home lets the usage reader be exercised against a real
// session cache shape without touching the installed harness.
const fakeHome = join(process.env.TEMP ?? '.', `bp-dsh-home-${Date.now()}`);
const cacheDir = join(fakeHome, 'storages', 'session_projcache', 'sessions');
mkdirSync(cacheDir, { recursive: true });
const cacheFile = join(cacheDir, 'session-usage.json');

function writeCache(input, output, cacheRead, cacheWrite, steps, model, effort) {
  writeFileSync(
    cacheFile,
    JSON.stringify({
      version: 7,
      record: {
        rows: {
          tokenUsage: { val: { totals: { uncachedInputTokens: input, outputTokens: output, cacheReadTokens: cacheRead, cacheWriteTokens: cacheWrite } } },
          modelSelection: { val: { lastUsed: { model, reasoningEffort: effort } } },
          sessionStats: { val: { steps } },
        },
      },
    }),
  );
}

process.env.DSH_HOME = fakeHome;
writeCache(1000, 100, 5000, 0, 3, 'deepseek-flash', 'max');
listener({ id: 'session-usage' }, { type: 'turn/start', seq: 100 });
writeCache(1500, 250, 9000, 40, 7, 'deepseek-flash', 'max');
listener({ id: 'session-usage' }, { type: 'turn/end', seq: 140 });
delete process.env.DSH_HOME;
await new Promise((resolve) => setTimeout(resolve, 500));

const usageLine = readFileSync(logFile, 'utf8')
  .split('\n')
  .reverse()
  .find((line) => line.includes('usage {'));

let usage = null;
try {
  usage = JSON.parse(usageLine.slice(usageLine.indexOf('usage ') + 'usage '.length));
} catch {
  // Left null; the assertions below report it.
}

check('a usage block was reported', usage !== null, usageLine ?? '(no usage line)');
if (usage !== null) {
  check('input tokens are the per-turn delta', usage.input_tokens === 500, String(usage.input_tokens));
  check('output tokens are the per-turn delta', usage.output_tokens === 150, String(usage.output_tokens));
  check('cache read is the per-turn delta', usage.cache_read_tokens === 4000, String(usage.cache_read_tokens));
  check('cache write is the per-turn delta', usage.cache_write_tokens === 40, String(usage.cache_write_tokens));
  check('steps are the per-turn delta', usage.steps === 4, String(usage.steps));
  check('model is reported', usage.model === 'deepseek-flash', usage.model);
  check('reasoning effort is reported', usage.reasoning_effort === 'max', usage.reasoning_effort);
  check('a duration was measured', typeof usage.duration_ms === 'number' && usage.duration_ms >= 0, String(usage.duration_ms));
  check('no cost is invented', usage.cost === undefined && usage.currency === undefined, JSON.stringify(usage));
}

const logText = (() => {
  try {
    return readFileSync(logFile, 'utf8');
  } catch {
    return '';
  }
})();

check('log file written', logText.length > 0, logFile);
check('logged bridge active', logText.includes('bridge active'));
check('logged turn/start', logText.includes('turn/start session=session-selftest'));
check('logged turn/end', logText.includes('turn/end session=session-selftest'));

const startLine = logText.split('\n').find((line) => line.includes('turn/start session='));
const endLine = logText.split('\n').find((line) => line.includes('turn/end session='));
const turnOf = (line) => line?.match(/turn=(turn:\S+)/)?.[1];
check(
  'turn/end reuses the turn id announced by turn/start',
  turnOf(startLine) !== undefined && turnOf(startLine) === turnOf(endLine),
  `start=${turnOf(startLine)} end=${turnOf(endLine)}`,
);

console.log('');
console.log('--- log file ---');
console.log(logText.trim() || '(empty)');
console.log('--- end log ---');
console.log('');
console.log(failures.length === 0 ? 'ALL CHECKS PASSED' : `FAILURES (${failures.length}): ${failures.join(', ')}`);
process.exit(failures.length === 0 ? 0 : 1);
