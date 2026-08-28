const { app, BrowserWindow, ipcMain, Menu, nativeImage, safeStorage, screen, Tray } = require('electron');
const fs = require('node:fs');
const path = require('node:path');
const { execFile } = require('node:child_process');
const { fetchBalance: fetchProviderBalance } = require('./providers/generic-json.cjs');

const DEFAULTS = {
  endpoint: '',
  authMode: 'bearer',
  headerName: 'Authorization',
  tokenEncrypted: '',
  balancePath: 'data.balance',
  currency: 'USD',
  refreshSeconds: 60,
  lowThreshold: 5,
  scale: 1,
  sound: true,
  volume: 0.6,
  bubble: true,
  petStyle: 'deepseek',
  interactionMode: 'free',
  x: null,
  y: null,
  flipped: false
};

app.setName('balance-pet');

let mainWindow;
let tray;
let quitting = false;
let cachedBalance = null;
let inFlight = null;
let codexTimer;
let codexWasRunning = false;
let codexTaskSeen = false;
let codexBalanceStart = null;

process.on('uncaughtException', (error) => {
  try { fs.appendFileSync(path.join(app.getPath('userData'), 'startup-error.log'), `${error.stack || error}\n`); } catch {}
});
process.on('unhandledRejection', (error) => {
  try { fs.appendFileSync(path.join(app.getPath('userData'), 'startup-error.log'), `${error?.stack || error}\n`); } catch {}
});

function settingsPath() {
  return path.join(app.getPath('userData'), 'settings.json');
}

function ledgerPath() {
  return path.join(app.getPath('userData'), 'usage-ledger.json');
}

function loadJson(file, fallback) {
  try {
    return { ...fallback, ...JSON.parse(fs.readFileSync(file, 'utf8')) };
  } catch {
    return { ...fallback };
  }
}

function saveJson(file, value) {
  fs.mkdirSync(path.dirname(file), { recursive: true });
  const temporary = `${file}.tmp`;
  fs.writeFileSync(temporary, JSON.stringify(value, null, 2), 'utf8');
  fs.renameSync(temporary, file);
}

function loadSettings() {
  return loadJson(settingsPath(), DEFAULTS);
}

function codexIsRunning() {
  return new Promise((resolve) => {
    execFile('tasklist.exe', ['/fo', 'csv', '/nh'], { windowsHide: true }, (_error, stdout = '') => {
      const names = String(stdout).toUpperCase();
      resolve(names.includes('CHATGPT.EXE') || names.includes('CODEX.EXE'));
    });
  });
}

async function pollCodex() {
  const running = await codexIsRunning();
  if (running && !codexWasRunning) {
    codexTaskSeen = true;
    const snapshot = await getBalance(true);
    codexBalanceStart = snapshot.ok ? snapshot.amount : null;
    mainWindow?.webContents.send('codex:state', { state: 'working' });
  } else if (!running && codexWasRunning && codexTaskSeen) {
    const snapshot = await getBalance(true);
    const spent = snapshot.ok && Number.isFinite(codexBalanceStart)
      ? Math.max(0, codexBalanceStart - snapshot.amount)
      : null;
    mainWindow?.webContents.send('codex:state', { state: 'done', spent, currency: snapshot.currency || 'USD' });
    codexTaskSeen = false;
    codexBalanceStart = null;
  }
  codexWasRunning = running;
}

function publicSettings(settings = loadSettings()) {
  const { tokenEncrypted, ...safe } = settings;
  return { ...safe, hasToken: Boolean(tokenEncrypted) };
}

function encryptToken(token) {
  if (!token) return '';
  if (!safeStorage.isEncryptionAvailable()) throw new Error('Windows secure storage is unavailable');
  return safeStorage.encryptString(token).toString('base64');
}

function decryptToken(value) {
  if (!value) return '';
  if (!safeStorage.isEncryptionAvailable()) throw new Error('Windows secure storage is unavailable');
  return safeStorage.decryptString(Buffer.from(value, 'base64'));
}

function todayKey() {
  const now = new Date();
  return `${now.getFullYear()}-${String(now.getMonth() + 1).padStart(2, '0')}-${String(now.getDate()).padStart(2, '0')}`;
}

function observeBalance(amount, currency) {
  const empty = { date: todayKey(), lastBalance: null, lastCurrency: currency, todayUsage: 0, history: {} };
  const ledger = loadJson(ledgerPath(), empty);
  const today = todayKey();
  if (ledger.date !== today) {
    if (ledger.date) ledger.history[ledger.date] = ledger.todayUsage || 0;
    ledger.history = Object.fromEntries(Object.entries(ledger.history).slice(-30));
    ledger.date = today;
    ledger.todayUsage = 0;
    ledger.lastBalance = amount;
    ledger.lastCurrency = currency;
  } else if (ledger.lastCurrency !== currency || ledger.lastBalance === null) {
    ledger.lastBalance = amount;
    ledger.lastCurrency = currency;
  } else {
    const delta = Number(ledger.lastBalance) - amount;
    if (delta > 0) ledger.todayUsage = Number(ledger.todayUsage || 0) + delta;
    ledger.lastBalance = amount;
  }
  saveJson(ledgerPath(), ledger);
  return Number(ledger.todayUsage || 0);
}

async function getBalance(force = false) {
  const settings = loadSettings();
  if (!settings.endpoint) return { ok: false, needsSetup: true, error: '点击菜单配置余额接口' };
  if (!force && cachedBalance && Date.now() - cachedBalance.fetchedAt < 25000) return cachedBalance;
  if (inFlight) return inFlight;

  inFlight = (async () => {
    try {
      const token = decryptToken(settings.tokenEncrypted);
      const amount = await fetchProviderBalance({ endpoint: settings.endpoint, settings, token });
      const todayUsage = observeBalance(amount, settings.currency);
      cachedBalance = {
        ok: true,
        amount,
        currency: settings.currency,
        todayUsage,
        updatedAt: Date.now(),
        fetchedAt: Date.now(),
        stale: false
      };
      return cachedBalance;
    } catch (error) {
      if (cachedBalance) return { ...cachedBalance, stale: true, error: error.message, fetchedAt: Date.now() };
      return { ok: false, error: error.message || '余额请求失败' };
    } finally {
      inFlight = null;
    }
  })();
  return inFlight;
}

function clamp(value, min, max) {
  return Math.max(min, Math.min(max, value));
}

function saveWindowPosition() {
  if (!mainWindow || mainWindow.isDestroyed()) return;
  const settings = loadSettings();
  const [x, y] = mainWindow.getPosition();
  saveJson(settingsPath(), { ...settings, x, y });
}

function snapWindow() {
  if (!mainWindow || mainWindow.isDestroyed()) return { flipped: false };
  const bounds = mainWindow.getBounds();
  const display = screen.getDisplayMatching(bounds).workArea;
  const centerX = bounds.x + bounds.width / 2;
  const centerY = bounds.y + bounds.height / 2;
  let x = bounds.x;
  let y = bounds.y;
  if (centerX < display.x + display.width / 4) x = display.x;
  else if (centerX > display.x + display.width * 0.75) x = display.x + display.width - bounds.width;
  if (centerY < display.y + display.height / 4) y = display.y;
  else if (centerY > display.y + display.height * 0.75) y = display.y + display.height - bounds.height;
  x = clamp(x, display.x, display.x + display.width - bounds.width);
  y = clamp(y, display.y, display.y + display.height - bounds.height);
  mainWindow.setPosition(Math.round(x), Math.round(y), true);
  const flipped = x === display.x;
  const settings = loadSettings();
  saveJson(settingsPath(), { ...settings, x, y, flipped });
  return { flipped };
}

function createWindow() {
  const settings = loadSettings();
  const primary = screen.getPrimaryDisplay().workArea;
  const width = 430;
  const height = 410;
  const x = Number.isFinite(settings.x) ? settings.x : primary.x + primary.width - width - 24;
  const y = Number.isFinite(settings.y) ? settings.y : primary.y + primary.height - height - 24;
  mainWindow = new BrowserWindow({
    width,
    height,
    x,
    y,
    minWidth: width,
    minHeight: height,
    maxWidth: width,
    maxHeight: height,
    frame: false,
    transparent: true,
    resizable: false,
    alwaysOnTop: true,
    skipTaskbar: true,
    show: false,
    backgroundColor: '#00000000',
    webPreferences: {
      preload: path.join(__dirname, 'preload.cjs'),
      contextIsolation: true,
      nodeIntegration: false,
      sandbox: true
    }
  });
  mainWindow.setAlwaysOnTop(true, 'floating');
  mainWindow.loadFile(path.join(__dirname, 'index.html'));
  mainWindow.once('ready-to-show', () => {
    mainWindow.showInactive();
    const capturePath = process.env.BALANCE_PET_CAPTURE;
    if (capturePath) {
      if (process.env.BALANCE_PET_CAPTURE_MODE === 'settings') {
        setTimeout(() => mainWindow.webContents.send('settings:open'), 250);
      }
      setTimeout(async () => {
        const image = await mainWindow.webContents.capturePage();
        fs.writeFileSync(capturePath, image.toPNG());
        quitting = true;
        app.quit();
      }, 1200);
    }
  });
  mainWindow.on('close', (event) => {
    if (!quitting) {
      event.preventDefault();
      mainWindow.hide();
    }
  });
  mainWindow.on('moved', saveWindowPosition);
}

function createTray() {
  const source = nativeImage.createFromPath(path.join(__dirname, '..', 'assets', 'pet.png'));
  tray = new Tray(source.resize({ width: 32, height: 32 }));
  tray.setToolTip('小余额');
  const menu = Menu.buildFromTemplate([
    { label: '显示小余额', click: () => { mainWindow.show(); mainWindow.focus(); } },
    { label: '立即刷新', click: async () => mainWindow.webContents.send('balance:update', await getBalance(true)) },
    { label: '配置接口', click: () => { mainWindow.show(); mainWindow.focus(); mainWindow.webContents.send('settings:open'); } },
    { type: 'separator' },
    { label: '退出', click: () => { quitting = true; app.quit(); } }
  ]);
  tray.setContextMenu(menu);
  tray.on('click', () => { mainWindow.show(); mainWindow.focus(); });
}

app.whenReady().then(() => {
  ipcMain.handle('settings:get', () => publicSettings());
  ipcMain.handle('settings:save', (_event, input) => {
    const previous = loadSettings();
    const next = {
      ...previous,
      endpoint: String(input.endpoint || '').trim(),
      authMode: ['bearer', 'authorization', 'websee-session', 'x-api-key', 'custom'].includes(input.authMode) ? input.authMode : 'bearer',
      headerName: String(input.headerName || 'Authorization').trim(),
      balancePath: String(input.balancePath || '').trim(),
      currency: String(input.currency || 'USD').trim().toUpperCase(),
      refreshSeconds: clamp(Number(input.refreshSeconds) || 60, 30, 86400),
      lowThreshold: Number(input.lowThreshold) || 0,
      scale: clamp(Number(input.scale) || 1, 0.75, 1.25),
      sound: Boolean(input.sound),
      volume: clamp(Number(input.volume) || 0, 0, 1),
      bubble: Boolean(input.bubble),
      petStyle: ['deepseek', 'chatgpt'].includes(input.petStyle) ? input.petStyle : 'deepseek',
      interactionMode: input.interactionMode === 'locked' ? 'locked' : 'free'
    };
    if (input.token) next.tokenEncrypted = encryptToken(String(input.token));
    if (input.clearToken) next.tokenEncrypted = '';
    saveJson(settingsPath(), next);
    cachedBalance = null;
    return publicSettings(next);
  });
  ipcMain.handle('balance:get', (_event, force) => getBalance(Boolean(force)));
  ipcMain.handle('window:move', (_event, { x, y }) => {
    if (mainWindow) mainWindow.setPosition(Math.round(x), Math.round(y));
  });
  ipcMain.handle('window:position', () => mainWindow ? mainWindow.getBounds() : null);
  ipcMain.handle('window:snap', () => snapWindow());
  ipcMain.handle('window:hide', () => mainWindow?.hide());
  createWindow();
  createTray();
  codexTimer = setInterval(pollCodex, 2000);
  pollCodex();
});

app.on('window-all-closed', (event) => event.preventDefault());
app.on('before-quit', () => { quitting = true; clearInterval(codexTimer); });
