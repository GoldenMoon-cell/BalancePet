(() => {
  if (window.__balancePetUsageCaptureInstalled) return;
  window.__balancePetUsageCaptureInstalled = true;
  const captured = Array.isArray(window.__balancePetUsageResponses)
    ? window.__balancePetUsageResponses
    : [];
  window.__balancePetUsageResponses = captured;
  const maxBody = 384 * 1024;

  const shouldCapture = (url) => {
    try {
      const parsed = new URL(url, location.href);
      if (parsed.origin !== location.origin) return false;
      return /\/api\/(log|usage|user)(?:[/?]|$)/i.test(parsed.pathname)
        || /\/usage(?:[/?]|$)/i.test(parsed.pathname);
    } catch {
      return false;
    }
  };

  const add = (url, status, contentType, body) => {
    if (!shouldCapture(url) || !body || body.length > maxBody) return;
    if (!/json|javascript|text\//i.test(contentType || "") && !/^[\s\[{]/.test(body)) return;
    let path;
    try {
      const parsed = new URL(url, location.href);
      path = parsed.pathname + parsed.search;
    } catch {
      path = String(url);
    }
    if (captured.some(item => item.path === path && item.body === body)) return;
    captured.push({ path, status, body });
    while (captured.length > 16) captured.shift();
  };

  const originalFetch = window.fetch;
  window.fetch = function (...args) {
    const requestUrl = typeof args[0] === "string" ? args[0] : args[0]?.url;
    const result = originalFetch.apply(this, args);
    if (!shouldCapture(requestUrl)) return result;
    return result.then(response => {
      try {
        response.clone().text().then(body => add(requestUrl, response.status, response.headers.get("content-type") || "", body)).catch(() => {});
      } catch { }
      return response;
    });
  };

  const originalOpen = XMLHttpRequest.prototype.open;
  const originalSend = XMLHttpRequest.prototype.send;
  XMLHttpRequest.prototype.open = function (method, url, ...rest) {
    this.__balancePetCaptureUrl = url;
    return originalOpen.call(this, method, url, ...rest);
  };
  XMLHttpRequest.prototype.send = function (...args) {
    const url = this.__balancePetCaptureUrl;
    if (shouldCapture(url)) {
      this.addEventListener("load", () => {
        let body = "";
        try {
          body = this.responseType === "json" && this.response != null
            ? JSON.stringify(this.response)
            : (typeof this.responseText === "string" ? this.responseText : "");
        } catch { }
        add(url, this.status, this.getResponseHeader("content-type") || "", body);
      }, { once: true });
    }
    return originalSend.apply(this, args);
  };
})();
