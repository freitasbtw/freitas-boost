'use strict';

const { app, BrowserWindow, ipcMain, Menu, shell } = require('electron');
const { spawn } = require('child_process');
const path = require('path');
const { runScript } = require('./powershell');

let mainWindow = null;

function createWindow() {
  mainWindow = new BrowserWindow({
    width: 1180,
    height: 760,
    minWidth: 1040,
    minHeight: 680,
    backgroundColor: '#0b0c0e',
    show: false,
    autoHideMenuBar: true,
    title: 'Freitas Boost',
    webPreferences: {
      preload: path.join(__dirname, '..', 'preload', 'preload.js'),
      contextIsolation: true,
      nodeIntegration: false,
      sandbox: true,
    },
  });

  Menu.setApplicationMenu(null);
  mainWindow.loadFile(path.join(__dirname, '..', 'renderer', 'index.html'));

  mainWindow.once('ready-to-show', () => mainWindow.show());

  // Links externos abrem no navegador padrao, nunca dentro do app.
  mainWindow.webContents.setWindowOpenHandler(({ url }) => {
    shell.openExternal(url);
    return { action: 'deny' };
  });

  mainWindow.on('closed', () => {
    mainWindow = null;
  });
}

// --- IPC: cada canal mapeia para um script PowerShell -----------------------

ipcMain.handle('system:info', () => runScript('system-info.ps1'));
ipcMain.handle('clean:temp', () => runScript('clean-temp.ps1'));
ipcMain.handle('ram:optimize', () => runScript('optimize-ram.ps1'));
ipcMain.handle('process:list', () => runScript('list-processes.ps1'));
ipcMain.handle('process:kill', (_event, items) =>
  runScript('kill-processes.ps1', { ITEMS: JSON.stringify(items || []) })
);
ipcMain.handle('fps:enable', () => runScript('fps-mode.ps1'));
ipcMain.handle('fps:restore', () => runScript('restore-mode.ps1'));
ipcMain.handle('cs2:profile', () => runScript('cs2-profile.ps1'));
ipcMain.handle('state:history', () => runScript('state-history.ps1', { ACTION: 'list' }));
ipcMain.handle('state:capture', () =>
  runScript('state-history.ps1', { ACTION: 'capture', LABEL: 'Estado manual' })
);
ipcMain.handle('state:restore', (_event, id) =>
  runScript('state-history.ps1', { ACTION: 'restore', ID: String(id || '') })
);
ipcMain.handle('state:delete', (_event, id) =>
  runScript('state-history.ps1', { ACTION: 'delete', ID: String(id || '') })
);

// Reinicia o app pedindo elevacao de administrador.
ipcMain.handle('admin:relaunch', () => {
  const exe = process.execPath;
  const escape = (s) => s.replace(/'/g, "''");

  let command;
  if (app.isPackaged) {
    command = `Start-Process -FilePath '${escape(exe)}' -Verb RunAs`;
  } else {
    command =
      `Start-Process -FilePath '${escape(exe)}' ` +
      `-ArgumentList '${escape(app.getAppPath())}' -Verb RunAs`;
  }

  spawn('powershell.exe', ['-NoProfile', '-Command', command], {
    detached: true,
    windowsHide: true,
    stdio: 'ignore',
  }).unref();

  app.quit();
});

// --- Ciclo de vida ----------------------------------------------------------

const gotLock = app.requestSingleInstanceLock();
if (!gotLock) {
  app.quit();
} else {
  app.on('second-instance', () => {
    if (mainWindow) {
      if (mainWindow.isMinimized()) mainWindow.restore();
      mainWindow.focus();
    }
  });

  app.whenReady().then(createWindow);

  app.on('window-all-closed', () => {
    if (process.platform !== 'darwin') app.quit();
  });

  app.on('activate', () => {
    if (BrowserWindow.getAllWindows().length === 0) createWindow();
  });
}
