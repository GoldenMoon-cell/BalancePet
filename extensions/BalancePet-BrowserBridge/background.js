const PAIRING_KEY = "balancePetPairing";
const ALARM_NAME = "balancePetUsageSync";
const SYNC_INTERVAL_MINUTES = 0.5;

chrome.runtime.onInstalled.addListener(() => {
  chrome.alarms.create(ALARM_NAME, { periodInMinutes: SYNC_INTERVAL_MINUTES });
});
chrome.runtime.onStartup.addListener(() => {
  chrome.alarms.create(ALARM_NAME, { periodInMinutes: SYNC_INTERVAL_MINUTES });
});
chrome.alarms.onAlarm.addListener((alarm) => {
  if (alarm.name === ALARM_NAME) syncStoredPairing();
});

async function syncStoredPairing() {
  const stored = await chrome.storage.local.get(PAIRING_KEY);
  const pairing = stored?.[PAIRING_KEY];
  if (!pairing?.code || !pairing.host || !pairing.expiresAt || pairing.expiresAt <= Date.now()) {
    if (pairing) await chrome.storage.local.remove(PAIRING_KEY);
    return;
  }

  const patterns = [`http://${pairing.host}/*`, `https://${pairing.host}/*`];
  let tabs = [];
  try { tabs = await chrome.tabs.query({ url: patterns }); } catch { return; }
  if (pairing.tabId) {
    const preferred = tabs.find(tab => tab.id === pairing.tabId);
    if (preferred) tabs = [preferred, ...tabs.filter(tab => tab.id !== pairing.tabId)];
  }

  for (const tab of tabs.slice(0, 4)) {
    if (!tab.id || !tab.url || !/^https?:\/\//i.test(tab.url)) continue;
    try {
      const payload = await collectPageSession(tab.id, tab.url);
      if (!payload) continue;
      const response = await fetch("http://127.0.0.1:28571/v1/session", {
        method: "POST",
        headers: { "content-type": "application/json" },
        body: JSON.stringify({ ...payload, code: pairing.code, host: new URL(tab.url).hostname })
      });
      if (response.status === 403) {
        await chrome.storage.local.remove(PAIRING_KEY);
        return;
      }
      if (response.ok) {
        await chrome.storage.local.set({
          [PAIRING_KEY]: { ...pairing, tabId: tab.id, lastSyncAt: Date.now(), expiresAt: Date.now() + 10 * 60 * 1000 }
        });
        return;
      }
    } catch { }
  }
}

async function collectPageSession(tabId, pageUrl) {
  const url = new URL(pageUrl);
  let cookies = [];
  try { cookies = await chrome.cookies.getAll({ url: url.href }); } catch { }

  let page = { storage: [], authorization: "", userId: "", usageResponses: [], captureInstalled: false };
  try {
    const result = await chrome.scripting.executeScript({
      target: { tabId },
      world: "MAIN",
      func: () => {
        const entries = [];
        for (const storage of [window.localStorage, window.sessionStorage]) {
          for (let index = 0; index < storage.length; index += 1) {
            const key = storage.key(index);
            if (!key) continue;
            const value = storage.getItem(key) || "";
            if (/session|token|auth|credential/i.test(key) && value.length > 0 && value.length <= 256 * 1024)
              entries.push({ key, value });
          }
        }
        let accessToken = "";
        for (const entry of entries) {
          if (/^(access[_-]?token|user[_-]?token|auth[_-]?token|token)$/i.test(entry.key) && entry.value.length > 10) {
            accessToken = entry.value.replace(/^Bearer\s+/i, "").trim();
            break;
          }
          try {
            const parsed = JSON.parse(entry.value);
            const stack = [parsed];
            while (stack.length && !accessToken) {
              const current = stack.pop();
              if (!current || typeof current !== "object") continue;
              for (const [key, value] of Object.entries(current)) {
                if (typeof value === "string" && /^(access[_-]?token|token|user[_-]?token)$/i.test(key) && value.length > 10) {
                  accessToken = value.replace(/^Bearer\s+/i, "").trim();
                  break;
                }
                if (value && typeof value === "object") stack.push(value);
              }
            }
          } catch { }
          if (accessToken) break;
        }
        let userId = "";
        if (accessToken) {
          try {
            const segment = accessToken.split(".")[1];
            const payload = JSON.parse(atob(segment.replace(/-/g, "+").replace(/_/g, "/")));
            const candidate = payload.user_id ?? payload.userId ?? payload.uid ?? payload.sub ?? payload.id;
            if (candidate !== undefined && /^\d+$/.test(String(candidate))) userId = String(candidate);
          } catch { }
        }
        const captured = Array.isArray(window.__balancePetUsageResponses)
          ? window.__balancePetUsageResponses.filter(item => item && typeof item.path === "string" && typeof item.body === "string").slice(-8)
          : [];
        return {
          storage: entries.slice(0, 32),
          authorization: accessToken ? `Bearer ${accessToken}` : "",
          userId,
          usageResponses: captured,
          captureInstalled: Boolean(window.__balancePetUsageCaptureInstalled)
        };
      }
    });
    page = result?.[0]?.result || page;
  } catch { }

  if (cookies.length === 0 && page.storage.length === 0 && page.usageResponses.length === 0 && !page.authorization)
    return null;
  return {
    cookies: cookies.map(cookie => ({ name: cookie.name, value: cookie.value })),
    storage: page.storage,
    usageResponses: page.usageResponses,
    authorization: page.authorization,
    userId: page.userId,
    captureInstalled: page.captureInstalled
  };
}