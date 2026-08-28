const { contextBridge, ipcRenderer } = require('electron');

contextBridge.exposeInMainWorld('balancePet', {
  getSettings: () => ipcRenderer.invoke('settings:get'),
  saveSettings: (settings) => ipcRenderer.invoke('settings:save', settings),
  getBalance: (force = false) => ipcRenderer.invoke('balance:get', force),
  getWindowPosition: () => ipcRenderer.invoke('window:position'),
  moveWindow: (position) => ipcRenderer.invoke('window:move', position),
  snapWindow: () => ipcRenderer.invoke('window:snap'),
  hideWindow: () => ipcRenderer.invoke('window:hide'),
  onCodexState: (handler) => ipcRenderer.on('codex:state', (_event, data) => handler(data)),
  onBalanceUpdate: (handler) => ipcRenderer.on('balance:update', (_event, data) => handler(data)),
  onOpenSettings: (handler) => ipcRenderer.on('settings:open', handler)
});
