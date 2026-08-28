const root = document.querySelector('#petRoot');
const petBody = document.querySelector('#petBody');
const bubble = document.querySelector('#bubble');
const bubbleContent = document.querySelector('#bubbleContent');
const amountEl = document.querySelector('#amount');
const labelEl = document.querySelector('#label');
const hintEl = document.querySelector('#hint');
const statusDot = document.querySelector('#statusDot');
const settingsPanel = document.querySelector('#settingsPanel');
const settingsForm = document.querySelector('#settingsForm');
const formMessage = document.querySelector('#formMessage');
const pressSound = document.querySelector('#pressSound');
const releaseSound = document.querySelector('#releaseSound');
const petImage = document.querySelector('#petImage');

let settings;
let currentBalance = null;
let timer = null;
let bubbleTimer = null;
let drag = null;
let randomMode = false;
let interaction = null;

const lines = [
  ['状态良好', '余额充足', '今天也可以安心工作'],
  ['我看着呢', '不会漏掉', '余额变化会及时告诉你'],
  ['轻点一点', '正在刷新', '让我看看还剩多少'],
  ['省着点花', '细水长流', '低余额时我会变红'],
  ['工作时间', '陪你写完', '然后记得休息一下']
];

function currencyAmount(amount, currency) {
  if (!Number.isFinite(amount)) return '--';
  if (currency === 'CNY' || currency === 'RMB') return `¥${amount.toFixed(2)}`;
  if (currency === 'USD') return `$${amount.toFixed(2)}`;
  return `${amount.toFixed(2)} ${currency}`;
}

function setStatus(name) {
  statusDot.className = `status-dot status-${name}`;
}

function play(sound) {
  if (!settings?.sound) return;
  sound.volume = settings.volume;
  sound.currentTime = 0;
  sound.play().catch(() => {});
}

function setBubbleContent(label, amount, hint) {
  bubbleContent.classList.add('swapping');
  setTimeout(() => {
    labelEl.textContent = label;
    amountEl.textContent = amount;
    hintEl.textContent = hint;
    bubbleContent.classList.remove('swapping');
  }, 150);
}

function openBubble(autoClose = true) {
  if (!settings?.bubble) return;
  bubble.classList.add('open');
  clearTimeout(bubbleTimer);
  if (autoClose) bubbleTimer = setTimeout(closeBubble, 5200);
}

function closeBubble() {
  bubble.classList.remove('open');
  randomMode = false;
}

function animateAmount(from, to, currency) {
  const start = performance.now();
  const initial = Number.isFinite(from) ? from : to;
  function frame(now) {
    const progress = Math.min(1, (now - start) / 720);
    const eased = 1 - Math.pow(1 - progress, 3);
    amountEl.textContent = currencyAmount(initial + (to - initial) * eased, currency);
    if (progress < 1) requestAnimationFrame(frame);
  }
  requestAnimationFrame(frame);
}

function renderBalance(data, manual = false) {
  if (!data.ok) {
    setStatus('error');
    setBubbleContent(data.needsSetup ? '还没配置' : '刷新失败', '--', data.error || '请检查接口配置');
    if (manual || data.needsSetup) openBubble(false);
    return;
  }
  const previous = currentBalance?.amount;
  currentBalance = data;
  const isLow = data.amount <= settings.lowThreshold;
  setStatus(isLow ? 'low' : 'ok');
  labelEl.textContent = data.stale ? '上次余额' : (isLow ? '余额偏低' : '账户余额');
  hintEl.textContent = data.stale
    ? `网络波动，暂用缓存 · ${data.error || ''}`
    : `今日已用 ${currencyAmount(data.todayUsage, data.currency)}`;
  animateAmount(previous, data.amount, data.currency);
  if (manual || previous === undefined || previous !== data.amount || isLow) openBubble();
}

async function refresh(force = false) {
  setStatus('loading');
  if (force) {
    randomMode = false;
    setBubbleContent('正在刷新', currentBalance ? currencyAmount(currentBalance.amount, currentBalance.currency) : '--', '正在联系中转站');
    openBubble();
  }
  renderBalance(await window.balancePet.getBalance(force), force);
}

function scheduleRefresh() {
  clearInterval(timer);
  timer = setInterval(() => refresh(false), Math.max(30, settings.refreshSeconds) * 1000);
}

function applySettings(next) {
  settings = next;
  document.documentElement.style.setProperty('--pet-scale', settings.scale);
  root.classList.toggle('flipped', Boolean(settings.flipped));
  pressSound.volume = settings.volume;
  releaseSound.volume = settings.volume;
  petImage.src = settings.petStyle === 'chatgpt' ? '../assets/chatgpt-dragon.png' : '../assets/pet.png';
  petBody.classList.toggle('locked', settings.interactionMode === 'locked');
  scheduleRefresh();
}

async function openSettings() {
  const next = await window.balancePet.getSettings();
  applySettings(next);
  for (const key of ['endpoint', 'authMode', 'headerName', 'balancePath', 'currency', 'refreshSeconds', 'lowThreshold', 'scale', 'volume', 'petStyle', 'interactionMode']) {
    document.querySelector(`#${key}`).value = next[key];
  }
  document.querySelector('#token').value = '';
  document.querySelector('#token').placeholder = next.hasToken ? '已安全保存，留空则不修改' : '输入中转站访问令牌';
  document.querySelector('#sound').checked = next.sound;
  document.querySelector('#bubbleEnabled').checked = next.bubble;
  updateSettingControls();
  settingsPanel.classList.add('open');
  settingsPanel.setAttribute('aria-hidden', 'false');
  closeBubble();
}

function closeSettings() {
  settingsPanel.classList.remove('open');
  settingsPanel.setAttribute('aria-hidden', 'true');
}

function updateSettingControls() {
  const custom = document.querySelector('#authMode').value === 'custom';
  document.querySelector('#headerField').style.opacity = custom ? '1' : '.45';
  document.querySelector('#headerName').disabled = !custom;
  document.querySelector('#scaleValue').textContent = `${Math.round(Number(document.querySelector('#scale').value) * 100)}%`;
  document.querySelector('#volumeValue').textContent = `${Math.round(Number(document.querySelector('#volume').value) * 100)}%`;
}

petBody.addEventListener('pointerdown', async (event) => {
  if (event.target.closest('button')) return;
  if (settings?.interactionMode === 'locked') {
    const rect = petBody.getBoundingClientRect();
    const localX = event.clientX - rect.left;
    const localY = event.clientY - rect.top;
    interaction = { x: event.screenX, y: event.screenY, kind: localY < rect.height * 0.36 ? 'hair' : localY < rect.height * 0.78 ? 'mouth' : 'body' };
    petBody.setPointerCapture(event.pointerId);
    petBody.classList.add('pressed');
    play(pressSound);
    return;
  }
  petBody.setPointerCapture(event.pointerId);
  const bounds = await window.balancePet.getWindowPosition();
  drag = { pointerId: event.pointerId, startX: event.screenX, startY: event.screenY, windowX: bounds.x, windowY: bounds.y, moved: false };
  petBody.classList.add('pressed');
  play(pressSound);
});

petBody.addEventListener('pointermove', (event) => {
  if (settings?.interactionMode === 'locked') {
    if (!interaction) return;
    const dx = Math.max(-42, Math.min(42, event.screenX - interaction.x));
    const dy = Math.max(-30, Math.min(30, event.screenY - interaction.y));
    petBody.style.setProperty('--interaction-x', `${interaction.kind === 'body' ? 0 : dx * 0.12}px`);
    petBody.style.setProperty('--interaction-y', `${interaction.kind === 'hair' ? dy * 0.12 : 0}px`);
    petBody.style.setProperty('--interaction-tilt', `${interaction.kind === 'hair' ? dx * 0.12 : interaction.kind === 'mouth' ? dx * 0.04 : 0}deg`);
    return;
  }
  if (!drag || drag.pointerId !== event.pointerId) return;
  const dx = event.screenX - drag.startX;
  const dy = event.screenY - drag.startY;
  if (dx * dx + dy * dy > 9) drag.moved = true;
  if (drag.moved) window.balancePet.moveWindow({ x: drag.windowX + dx, y: drag.windowY + dy });
});

async function releasePet(event) {
  if (settings?.interactionMode === 'locked') {
    petBody.classList.remove('pressed');
    const kind = interaction?.kind;
    interaction = null;
    petBody.style.removeProperty('--interaction-x');
    petBody.style.removeProperty('--interaction-y');
    petBody.style.removeProperty('--interaction-tilt');
    play(releaseSound);
    await refresh(true);
    if (kind === 'hair') setBubbleContent('呆毛被拽了', '哎呀', '轻一点嘛');
    else if (kind === 'mouth') setBubbleContent('嘴角被拉动', '嘿嘿', '锁定互动模式');
    else setBubbleContent('被戳到了', '在呢', '点击刷新余额');
    openBubble(false);
    return;
  }
  if (!drag || drag.pointerId !== event.pointerId) return;
  petBody.classList.remove('pressed');
  play(releaseSound);
  const wasMoved = drag.moved;
  drag = null;
  if (wasMoved) {
    const result = await window.balancePet.snapWindow();
    root.classList.toggle('flipped', result.flipped);
  } else {
    await refresh(true);
  }
}

petBody.addEventListener('pointerup', releasePet);
petBody.addEventListener('pointercancel', releasePet);
petBody.addEventListener('keydown', (event) => {
  if (event.key === 'Enter' || event.key === ' ') refresh(true);
});

bubble.addEventListener('click', () => {
  if (!randomMode) {
    randomMode = true;
    const [label, amount, hint] = lines[Math.floor(Math.random() * lines.length)];
    setBubbleContent(label, amount, hint);
  } else closeBubble();
});

document.querySelector('#menuButton').addEventListener('click', (event) => { event.stopPropagation(); openSettings(); });
document.querySelector('#closeSettings').addEventListener('click', closeSettings);
document.querySelector('#authMode').addEventListener('change', updateSettingControls);
document.querySelector('#scale').addEventListener('input', () => {
  updateSettingControls();
  document.documentElement.style.setProperty('--pet-scale', document.querySelector('#scale').value);
});
document.querySelector('#volume').addEventListener('input', updateSettingControls);

settingsForm.addEventListener('submit', async (event) => {
  event.preventDefault();
  formMessage.className = 'form-message';
  formMessage.textContent = '正在保存并测试...';
  const values = {
    endpoint: document.querySelector('#endpoint').value,
    authMode: document.querySelector('#authMode').value,
    headerName: document.querySelector('#headerName').value,
    token: document.querySelector('#token').value,
    balancePath: document.querySelector('#balancePath').value,
    currency: document.querySelector('#currency').value,
    refreshSeconds: document.querySelector('#refreshSeconds').value,
    lowThreshold: document.querySelector('#lowThreshold').value,
    scale: document.querySelector('#scale').value,
    volume: document.querySelector('#volume').value,
    petStyle: document.querySelector('#petStyle').value,
    interactionMode: document.querySelector('#interactionMode').value,
    sound: document.querySelector('#sound').checked,
    bubble: document.querySelector('#bubbleEnabled').checked
  };
  try {
    applySettings(await window.balancePet.saveSettings(values));
    const result = await window.balancePet.getBalance(true);
    if (!result.ok) throw new Error(result.error);
    formMessage.classList.add('success');
    formMessage.textContent = `连接成功：${currencyAmount(result.amount, result.currency)}`;
    renderBalance(result, true);
    setTimeout(closeSettings, 900);
  } catch (error) {
    formMessage.textContent = error.message || '保存失败';
  }
});

window.balancePet.onBalanceUpdate((data) => renderBalance(data, true));
window.balancePet.onOpenSettings(openSettings);
window.balancePet.onCodexState(({ state, spent, currency }) => {
  const amount = state === 'done' && Number.isFinite(spent) ? `-${currencyAmount(spent, currency)}` : '--';
  setBubbleContent(state === 'working' ? 'Codex 工作中' : 'Codex 完成', amount, state === 'working' ? '正在处理任务' : '本次任务余额变化');
  openBubble(false);
});

(async () => {
  applySettings(await window.balancePet.getSettings());
  await refresh(false);
})();
