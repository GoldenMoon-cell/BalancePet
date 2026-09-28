const codeBox = document.getElementById("code");
const syncButton = document.getElementById("sync");
const statusBox = document.getElementById("status");

function setStatus(message, kind = "") {
  statusBox.textContent = message;
  statusBox.className = kind;
}

function requestPermission(url) {
  return new Promise((resolve) => {
    const patterns = [`${url.protocol}//${url.hostname}/*`];
    if (url.port) patterns.push(`${url.protocol}//${url.hostname}:${url.port}/*`);
    chrome.permissions.contains({ origins: patterns }, (alreadyGranted) => {
      if (alreadyGranted) return resolve(true);
      chrome.permissions.request({ origins: patterns }, (granted) => resolve(Boolean(granted)));
    });
  });
}

async function readCookies(url) {
  const queries = [
    { url: url.href },
    { domain: url.hostname }
  ];
  // Some Chromium profiles store first-party cookies in a partition. The
  // normal query remains the primary path; this is only a compatibility
  // fallback and is ignored by browsers that do not support partitionKey.
  try { queries.push({ url: url.href, partitionKey: { topLevelSite: url.origin } }); } catch { }

  const cookies = new Map();
  for (const query of queries) {
    try {
      const result = await chrome.cookies.getAll(query);
      for (const cookie of result || []) {
        const key = `${cookie.name}\u0000${cookie.domain}\u0000${cookie.path}`;
        cookies.set(key, cookie);
      }
    } catch { }
  }
  return [...cookies.values()];
}

async function readWebSession(tabId) {
  try {
    const result = await chrome.scripting.executeScript({
      target: { tabId },
      func: () => {
        const collect = (storage) => {
          const entries = [];
          for (let index = 0; index < storage.length; index += 1) {
            const key = storage.key(index);
            if (!key) continue;
            const value = storage.getItem(key);
            if (value) entries.push({ key, value });
          }
          return entries;
        };
        return { local: collect(window.localStorage), session: collect(window.sessionStorage) };
      }
    });
    return result?.[0]?.result || { local: [], session: [] };
  } catch {
    return { local: [], session: [] };
  }
}

async function readUsageResponses(tabId) {
  try {
    const result = await chrome.scripting.executeScript({
      target: { tabId },
      // The page capture hook is installed in the MAIN world so it can see
      // the site's fetch/XHR traffic. Read the shared capture buffer from
      // that same world; the extension's isolated world has a separate
      // window object and would otherwise always report zero responses.
      world: "MAIN",
      func: async () => {
        const readAccessToken = () => {
          const entries = [];
          for (const storage of [window.localStorage, window.sessionStorage]) {
            for (let index = 0; index < storage.length; index += 1) {
              const key = storage.key(index);
              if (!key) continue;
              entries.push({ key, value: storage.getItem(key) || "" });
            }
          }
          const direct = entries.find(entry =>
            /^(access[_-]?token|user[_-]?token|auth[_-]?token|token)$/i.test(entry.key)
            && entry.value.length > 10 && entry.value.length <= 64 * 1024);
          if (direct) return direct.value.replace(/^Bearer\s+/i, "").trim();
          for (const entry of entries) {
            if (!/auth|session|user|token/i.test(entry.key) || entry.value.length > 256 * 1024) continue;
            try {
              const parsed = JSON.parse(entry.value);
              const stack = [parsed];
              while (stack.length) {
                const current = stack.pop();
                if (!current || typeof current !== "object") continue;
                for (const [key, value] of Object.entries(current)) {
                  if (typeof value === "string" && /^(access[_-]?token|token|user[_-]?token)$/i.test(key)
                    && value.length > 10 && value.length <= 64 * 1024)
                    return value.replace(/^Bearer\s+/i, "").trim();
                  if (value && typeof value === "object") stack.push(value);
                }
              }
            } catch { }
          }
          return "";
        };
        let accessToken = readAccessToken();
        let userId = "";
        if (!accessToken) {
          for (const refreshPath of ["/api/user/auth/refresh", "/api/auth/refresh"]) {
            try {
              const refresh = await fetch(new URL(refreshPath, location.origin), {
                method: "POST",
                credentials: "include",
                headers: { "accept": "application/json", "content-type": "application/json", "x-requested-with": "XMLHttpRequest" },
                body: "{}",
                cache: "no-store"
              });
              if (!refresh.ok) continue;
              const payload = await refresh.json();
              const data = payload?.data || payload;
              if (typeof data?.access_token === "string") accessToken = data.access_token;
              if (data?.user?.id !== undefined) userId = String(data.user.id);
              if (data?.session?.sid) userId = userId || String(data.session.sid);
              if (accessToken) break;
            } catch { }
          }
        }
        if (accessToken && !userId) {
          try {
            const segment = accessToken.split(".")[1];
            const payload = JSON.parse(atob(segment.replace(/-/g, "+").replace(/_/g, "/")));
            const candidate = payload.user_id ?? payload.userId ?? payload.uid ?? payload.sub ?? payload.id;
            if (candidate !== undefined && /^\d+$/.test(String(candidate))) userId = String(candidate);
          } catch { }
        }
        const paths = [
          "/api/log/self?p=0&page_size=500&type=2",
          "/api/log/self?p=1&page_size=500&type=2",
          "/api/log/self?page_size=500&type=2",
          "/api/log/self?p=0&page_size=500&type=0",
          "/api/log/token?limit=500",
          "/api/usage?limit=500",
          "/api/usage/log?limit=500",
          "/api/usage/records?limit=500",
          "/usage?format=json"
        ];
        const captured = Array.isArray(window.__balancePetUsageResponses)
          ? window.__balancePetUsageResponses.filter(item => item && typeof item.path === "string" && typeof item.body === "string")
          : [];
        const responses = captured.slice(-8);
        let total = responses.reduce((sum, item) => sum + item.body.length, 0);
        for (const path of paths) {
          try {
            const response = await fetch(new URL(path, location.origin), {
              credentials: "include",
              headers: {
                accept: "application/json, text/plain, */*",
                ...(accessToken ? { authorization: `Bearer ${accessToken}` } : {}),
                "x-requested-with": "XMLHttpRequest",
                "x-user-ui-request": "1",
                ...(userId && /^\d+$/.test(userId) ? { "new-api-user": userId } : {}),
              },
              cache: "no-store"
            });
            if (!response.ok) continue;
            const contentType = response.headers.get("content-type") || "";
            const body = await response.text();
            if (!body || body.length > 384 * 1024) continue;
            if (!contentType.includes("json") && !/^[\s\[{]/.test(body)) continue;
            if (total + body.length > 460 * 1024) break;
            responses.push({ path, body });
            total += body.length;
            // One successful endpoint is normally enough and avoids
            // unnecessary requests on large dashboards.
            if (responses.length >= 12) break;
          } catch { }
        }
        return { accessToken, userId, responses, captureInstalled: Boolean(window.__balancePetUsageCaptureInstalled) };
      }
    });
    return result?.[0]?.result || { accessToken: "", userId: "", responses: [], captureInstalled: false };
  } catch {
    return { accessToken: "", userId: "", responses: [], captureInstalled: false };
  }
}

syncButton.addEventListener("click", async () => {
  const code = codeBox.value.trim();
  if (!/^\d{6}$/.test(code)) {
    setStatus("请输入 BalancePet 设置中显示的 6 位配对码。", "error");
    return;
  }
  syncButton.disabled = true;
  setStatus("正在读取当前网页会话……");
  try {
    const tabs = await chrome.tabs.query({ active: true, currentWindow: true });
    const tab = tabs[0];
    if (!tab || !tab.url || !/^https?:\/\//i.test(tab.url)) throw new Error("当前标签页不是普通网站页面。");
    const url = new URL(tab.url);
    if (!(await requestPermission(url))) throw new Error("你拒绝了当前站点的 Cookie 权限。");
    const cookies = await readCookies(url);
    const storage = await readWebSession(tab.id);
    const usageCapture = await readUsageResponses(tab.id);
    const usageResponses = usageCapture.responses || [];
    const authorization = usageCapture.accessToken ? `Bearer ${usageCapture.accessToken}` : "";
    const userId = usageCapture.userId || "";
    const captureInstalled = Boolean(usageCapture.captureInstalled);
    const storageEntries = [...(storage.local || []), ...(storage.session || [])]
      .filter(entry => entry && typeof entry.key === "string" && typeof entry.value === "string")
      .filter(entry => /session|token|auth|credential/i.test(entry.key))
      .filter(entry => entry.value.length > 0 && entry.value.length <= 256 * 1024)
      .slice(0, 32);
    if (cookies.length === 0 && storageEntries.length === 0 && usageResponses.length === 0 && !authorization) {
      throw new Error(`当前页面没有可读取的 Cookie 或网页会话（${url.hostname}）。请确认你在同一中转站页面已登录，并重新点击同步。`);
    }
    const response = await fetch("http://127.0.0.1:28571/v1/session", {
      method: "POST",
      headers: { "content-type": "application/json" },
      body: JSON.stringify({
        code,
        host: url.hostname,
        cookies: cookies.map(cookie => ({ name: cookie.name, value: cookie.value })),
        storage: storageEntries,
        usageResponses,
        authorization,
        userId,
        captureInstalled
      })
    });
    const result = await response.json().catch(() => ({}));
    if (!response.ok || !result.ok) throw new Error(result.message || `BalancePet 返回 HTTP ${response.status}`);
    await chrome.storage.local.set({
      balancePetPairing: {
        code,
        host: url.hostname,
        tabId: tab.id,
        expiresAt: Date.now() + 10 * 60 * 1000,
        lastSyncAt: Date.now()
      }
    });
    await chrome.alarms.create("balancePetUsageSync", { periodInMinutes: 0.5 });
    setStatus((result.message || `浏览器会话已同步（Cookie ${cookies.length} 项，网页会话 ${storageEntries.length} 项，用量响应 ${usageResponses.length} 项）。`) + "；只要浏览器会话有效就会持续自动同步新用量。", "ok");
  } catch (error) {
    setStatus(error?.message || "同步失败。", "error");
  } finally {
    syncButton.disabled = false;
  }
});
