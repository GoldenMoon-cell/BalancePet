/**
 * Reads a DeepSeek Harness session log (zstd-compressed JSONL) and prints the
 * shape of its turn/start and turn/end records, so the bridge plugin can be
 * checked against real recorded events instead of inferred types.
 *
 * Prints only envelope fields — never message, prompt, or reply content.
 *
 * Run: node inspect-dsh-turn-events.mjs <session.v4.jsonl.zstd> [maxRecords]
 */

import { readFileSync } from 'node:fs';
import { zstdDecompressSync } from 'node:zlib';

const file = process.argv[2];
const maxRecords = Number(process.argv[3] ?? 3);
if (!file) {
  console.error('usage: node inspect-dsh-turn-events.mjs <session.v4.jsonl.zstd> [maxRecords]');
  process.exit(2);
}

// The log is a run of concatenated zstd frames. Node's decompressor stops at
// the first frame boundary, so split on the frame magic and decompress each
// frame on its own; a frame that fails is skipped rather than fatal, because a
// magic-byte match inside compressed data is possible.
const ZSTD_MAGIC = Buffer.from([0x28, 0xb5, 0x2f, 0xfd]);
function decompressAllFrames(buffer) {
  const starts = [];
  let index = buffer.indexOf(ZSTD_MAGIC, 0);
  while (index !== -1) {
    starts.push(index);
    index = buffer.indexOf(ZSTD_MAGIC, index + ZSTD_MAGIC.length);
  }
  if (starts.length === 0) return '';

  const parts = [];
  let skipped = 0;
  for (let i = 0; i < starts.length; i++) {
    const end = i + 1 < starts.length ? starts[i + 1] : buffer.length;
    try {
      parts.push(zstdDecompressSync(buffer.subarray(starts[i], end)).toString('utf8'));
    } catch {
      skipped += 1;
    }
  }
  if (skipped > 0) console.error(`(skipped ${skipped} non-frame magic match(es))`);
  return parts.join('');
}

const raw = readFileSync(file);
const text = decompressAllFrames(raw);
console.log(`file: ${raw.length} bytes -> ${text.length} chars after decompression`);

const lines = text.split('\n').filter((line) => line.trim().length > 0);
console.log(`records in log: ${lines.length}`);

const counts = new Map();
const samples = [];
for (const line of lines) {
  let record;
  try {
    record = JSON.parse(line);
  } catch {
    continue;
  }
  // The type may sit at the top level or inside an envelope.
  const type = record?.type ?? record?.event?.type ?? record?.data?.type;
  if (typeof type !== 'string') continue;
  counts.set(type, (counts.get(type) ?? 0) + 1);

  if ((type === 'turn/start' || type === 'turn/end') && samples.length < maxRecords * 2) {
    samples.push({ line, record, type });
  }
}

console.log('');
console.log('turn-related event types present:');
for (const [type, count] of [...counts].filter(([t]) => t.startsWith('turn/') || t.startsWith('step/'))) {
  console.log(`  ${type.padEnd(14)} ${count}`);
}

console.log('');
console.log(`--- ${samples.length} sample envelope(s), content fields redacted ---`);
for (const { record, type } of samples) {
  const top = record?.type !== undefined ? record : (record?.event ?? record?.data);
  const shape = {};
  for (const [key, value] of Object.entries(top)) {
    if (key === 'message' || key === 'text' || key === 'content') {
      shape[key] = `<${typeof value}>`;
    } else if (value !== null && typeof value === 'object') {
      shape[key] = Array.isArray(value) ? `[${value.length}]` : `{${Object.keys(value).join(',')}}`;
    } else {
      shape[key] = value;
    }
  }
  console.log(`  ${type}: ${JSON.stringify(shape)}`);
}
