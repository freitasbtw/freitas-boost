'use strict';

const { contextBridge, ipcRenderer } = require('electron');

// API segura exposta ao renderer. Nenhum acesso direto a Node/Electron
// vaza para a pagina; tudo passa por estes canais controlados.
contextBridge.exposeInMainWorld('fb', {
  systemInfo: () => ipcRenderer.invoke('system:info'),
  cleanTemp: () => ipcRenderer.invoke('clean:temp'),
  optimizeRam: () => ipcRenderer.invoke('ram:optimize'),
  listProcesses: () => ipcRenderer.invoke('process:list'),
  killProcesses: (items) => ipcRenderer.invoke('process:kill', items),
  fpsMode: () => ipcRenderer.invoke('fps:enable'),
  restoreMode: () => ipcRenderer.invoke('fps:restore'),
  cs2Profile: () => ipcRenderer.invoke('cs2:profile'),
  stateHistory: () => ipcRenderer.invoke('state:history'),
  captureState: () => ipcRenderer.invoke('state:capture'),
  restoreState: (id) => ipcRenderer.invoke('state:restore', id),
  deleteState: (id) => ipcRenderer.invoke('state:delete', id),
  relaunchAsAdmin: () => ipcRenderer.invoke('admin:relaunch'),
});
