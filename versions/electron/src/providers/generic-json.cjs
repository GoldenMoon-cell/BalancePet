const { URL } = require('node:url');

function readPath(payload, dottedPath) {
  if (!dottedPath) return payload;
  return dottedPath.split('.').filter(Boolean).reduce((current, key) => {
    if (Array.isArray(current) && /^\d+$/.test(key)) {
      const value = current[Number(key)];
      if (value === undefined) throw new Error(`找不到余额字段：${dottedPath}`);
      return value;
    }
    if (current && typeof current === 'object' && Object.hasOwn(current, key)) return current[key];
    throw new Error(`找不到余额字段：${dottedPath}`);
  }, payload);
}

function parseAmount(value) {
  if (typeof value === 'number' && Number.isFinite(value)) return value;
  if (typeof value === 'boolean' || value === null || value === undefined) throw new Error('余额字段不是数字');
  const normalized = String(value).replace(/,/g, '').replace(/(?:USD|CNY|RMB|\$|¥)/gi, '').trim();
  const amount = Number(normalized);
  if (!Number.isFinite(amount)) throw new Error('余额字段不是数字');
  return amount;
}

function providerError(status) {
  if (status === 401) return '认证失败（HTTP 401），请检查令牌和认证方式';
  if (status === 403) return '接口拒绝访问（HTTP 403）';
  if (status === 404) return '余额接口不存在（HTTP 404）';
  return `余额接口返回 HTTP ${status}`;
}

async function fetchJsonWithRetry(url, options) {
  let lastError;
  for (let attempt = 0; attempt < 2; attempt += 1) {
    try {
      const response = await fetch(url, { ...options, signal: AbortSignal.timeout(15000) });
      if (!response.ok) {
        const error = new Error(providerError(response.status));
        error.statusCode = response.status;
        error.transient = response.status >= 500;
        throw error;
      }
      try {
        return await response.json();
      } catch (error) {
        throw new Error('余额接口返回的内容不是 JSON', { cause: error });
      }
    } catch (error) {
      lastError = error;
      const transient = error.transient || error.name === 'TimeoutError' || error.name === 'TypeError';
      if (!transient || attempt === 1) break;
      await new Promise((resolve) => setTimeout(resolve, 500));
    }
  }
  throw lastError;
}

async function fetchBalance({ endpoint, settings, token }) {
  if (!endpoint) throw new Error('请先配置余额 API 地址');
  const url = new URL(endpoint);
  if (!['http:', 'https:'].includes(url.protocol)) throw new Error('接口地址必须以 http:// 或 https:// 开头');
  const headers = { Accept: 'application/json', 'User-Agent': 'BalancePet/0.3' };
  token = String(token || '').trim();
  if (['bearer', 'websee-session'].includes(settings.authMode) && /^bearer\s+/i.test(token)) {
    token = token.replace(/^bearer\s+/i, '').trim();
  }
  if (token) {
    if (settings.authMode === 'x-api-key') headers['x-api-key'] = token;
    else if (settings.authMode === 'authorization') headers.Authorization = /\s/.test(token) ? token : `Bearer ${token}`;
    else if (settings.authMode === 'custom') headers[settings.headerName || 'Authorization'] = token;
    else {
      headers.Authorization = `Bearer ${token}`;
      if (settings.authMode === 'websee-session') {
        const parsed = new URL(url);
        headers.Referer = `${parsed.protocol}//${parsed.host}/dashboard`;
        headers['X-User-UI-Request'] = '1';
        headers['Accept-Language'] = 'zh';
      }
    }
  }
  const payload = await fetchJsonWithRetry(url, { headers });
  return parseAmount(readPath(payload, settings.balancePath));
}

module.exports = { fetchBalance, parseAmount, readPath };
