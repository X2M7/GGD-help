const { app, BrowserWindow, globalShortcut, ipcMain, Menu, Tray, nativeImage, screen } = require('electron');
const fs = require('node:fs');
const path = require('node:path');
const { pathToFileURL } = require('node:url');
const { clampLauncher, sanitizeSettings } = require('./shell-state');
const { migrateLegacyData } = require('./legacy-data');

app.setName('GGD-help');
// Tests use a separate profile, so running them never changes a player's records.
const userDataPath = process.env.OVERLAY_TEST_USER_DATA
  ? path.resolve(process.env.OVERLAY_TEST_USER_DATA)
  : path.join(app.getPath('appData'), 'GGD-help');
fs.mkdirSync(userDataPath, { recursive: true });
app.setPath('userData', userDataPath);

const LAUNCHER_SIZE = { width: 266, height: 66 };
let overlay;
let launcher;
let tray;
let quitting = false;
let settings = {};
let saveTimer;
let interactive = false;
let visible = true;
let locked = false;
let displayId;
let persistenceError = '';
const shortcutErrors = [];

function settingsPath() {
  return path.join(app.getPath('userData'), 'overlay-window.json');
}

function readSettings() {
  try {
    settings = sanitizeSettings(JSON.parse(fs.readFileSync(settingsPath(), 'utf8')));
  } catch (error) {
    settings = {};
    if (error.code !== 'ENOENT') persistenceError = '窗口配置无法读取，已使用默认位置。';
  }
}

function saveSettings() {
  clearTimeout(saveTimer);
  try {
    fs.mkdirSync(app.getPath('userData'), { recursive: true });
    const target = settingsPath();
    fs.writeFileSync(`${target}.tmp`, JSON.stringify(settings, null, 2), 'utf8');
    fs.renameSync(`${target}.tmp`, target);
    if (persistenceError) {
      persistenceError = '';
      broadcast();
    }
  } catch {
    persistenceError = '窗口位置无法保存，请检查配置目录是否可写。';
    broadcast();
  }
}

function activeDisplay() {
  return screen.getAllDisplays().find((display) => display.id === displayId) || screen.getPrimaryDisplay();
}

function getState() {
  return {
    visible, locked, displayId, shortcutErrors: [...shortcutErrors], persistenceError,
    displays: screen.getAllDisplays().map((display, index) => ({
      id: display.id,
      label: display.label || `显示器 ${index + 1}`,
      bounds: { ...display.bounds },
      scaleFactor: display.scaleFactor,
      primary: display.id === screen.getPrimaryDisplay().id,
    })),
  };
}

function broadcast() {
  if (!app.isReady()) return;
  const state = getState();
  for (const win of [overlay, launcher]) {
    if (win && !win.isDestroyed() && !win.webContents.isDestroyed()) {
      win.webContents.send('overlay:state', state);
    }
  }
  if (tray) tray.setContextMenu(buildMenu());
}

function updateMouseMode() {
  if (!overlay || overlay.isDestroyed()) return;
  overlay.setIgnoreMouseEvents(locked || !interactive, { forward: true });
  // Mouse passthrough alone does not release keyboard focus. Explicitly end
  // editing when the user locks/hides the overlay so game keys cannot be eaten.
  if ((locked || !visible) && overlay.isFocused()) overlay.blur();
  overlay.setFocusable(!locked);
}

function keepLauncherAbove() {
  if (!launcher || launcher.isDestroyed()) return;
  launcher.setAlwaysOnTop(true, 'screen-saver');
  launcher.showInactive();
  launcher.moveTop();
}

function setVisible(value) {
  visible = value;
  interactive = false;
  updateMouseMode();
  if (visible) {
    overlay.showInactive();
    overlay.setBounds(activeDisplay().bounds);
  }
  else overlay.hide();
  keepLauncherAbove();
  broadcast();
  return getState();
}

function setLocked(value) {
  // On Windows blur() can leave an already active transparent HWND active,
  // even after WS_EX_NOACTIVATE is set. Hiding it first relinquishes native
  // keyboard focus; showInactive then restores only its visual annotations.
  const releaseFocus = value && overlay.isFocused();
  if (releaseFocus) overlay.hide();
  locked = value;
  interactive = false;
  updateMouseMode();
  if (releaseFocus && visible) {
    overlay.showInactive();
    overlay.setBounds(activeDisplay().bounds);
  }
  keepLauncherAbove();
  broadcast();
  return getState();
}

function restoreLauncher(moveToSelected = false) {
  if (!launcher || launcher.isDestroyed()) return;
  const current = launcher.getBounds();
  const display = moveToSelected ? activeDisplay() : screen.getDisplayMatching(current);
  const desired = moveToSelected
    ? { x: display.workArea.x + display.workArea.width - LAUNCHER_SIZE.width - 16, y: display.workArea.y + 16 }
    : current;
  const position = clampLauncher(desired, display.workArea, LAUNCHER_SIZE);
  launcher.setBounds({ ...position, ...LAUNCHER_SIZE });
  settings.launcherPosition = position;
  keepLauncherAbove();
}

function selectDisplay(id) {
  const display = screen.getAllDisplays().find((item) => item.id === id);
  if (!display) throw new Error('显示器不存在，请重新选择。');
  displayId = display.id;
  settings.displayId = displayId;
  overlay.setBounds(display.bounds);
  interactive = false;
  updateMouseMode();
  restoreLauncher(true);
  saveSettings();
  broadcast();
  return getState();
}

function restoreControls() {
  setLocked(false);
  setVisible(true);
  restoreLauncher(true);
}

function buildMenu() {
  return Menu.buildFromTemplate([
    { label: visible ? '收起蒙版  Ctrl+Shift+G' : '显示蒙版  Ctrl+Shift+G', click: () => setVisible(!visible) },
    { label: locked ? '允许编辑  Ctrl+Shift+L' : '鼠标穿透  Ctrl+Shift+L', click: () => setLocked(!locked) },
    { type: 'separator' },
    {
      label: '游戏所在显示器',
      submenu: getState().displays.map((display) => ({
        label: `${display.label} · 约 ${Math.round(display.bounds.width * display.scaleFactor)}×${Math.round(display.bounds.height * display.scaleFactor)}`,
        type: 'radio', checked: display.id === displayId,
        click: () => selectDisplay(display.id),
      })),
    },
    { label: '找回开关并恢复编辑', click: restoreControls },
    { type: 'separator' },
    { label: '退出 GGD-help', click: () => app.quit() },
  ]);
}

function authorize(event, allowed = 'both') {
  const windows = allowed === 'overlay' ? [overlay] : allowed === 'launcher' ? [launcher] : [overlay, launcher];
  const win = windows.find((candidate) => candidate && !candidate.isDestroyed() && candidate.webContents === event.sender);
  const expectedFile = win === overlay ? 'index.html' : 'launcher.html';
  if (!win || event.senderFrame !== win.webContents.mainFrame || event.senderFrame.url !== pathToFileURL(path.join(__dirname, expectedFile)).href) {
    throw new Error('无权访问桌面窗口。');
  }
}

function registerIPC() {
  const handle = (channel, callback, allowed) => ipcMain.handle(channel, (event, ...args) => {
    authorize(event, allowed);
    return callback(...args);
  });
  handle('overlay:get-state', getState);
  handle('overlay:toggle-visible', () => setVisible(!visible));
  handle('overlay:set-locked', (value) => {
    if (typeof value !== 'boolean') throw new TypeError('锁定状态必须是布尔值。');
    return setLocked(value);
  });
  handle('overlay:select-display', (value) => {
    if (!Number.isSafeInteger(value)) throw new TypeError('显示器编号不正确。');
    return selectDisplay(value);
  });
  handle('overlay:quit', () => app.quit());
  handle('launcher:menu', () => { buildMenu().popup({ window: launcher }); }, 'launcher');
  ipcMain.on('overlay:interactive', (event, value) => {
    try {
      authorize(event, 'overlay');
      if (typeof value !== 'boolean' || interactive === value) return;
      interactive = value;
      updateMouseMode();
    } catch { /* Ignore unauthorized, malformed, or late renderer messages. */ }
  });
}

function secureWindow(win, file) {
  const url = pathToFileURL(path.join(__dirname, file)).href;
  win.webContents.setWindowOpenHandler(() => ({ action: 'deny' }));
  win.webContents.on('will-navigate', (event, destination) => {
    if (destination !== url) event.preventDefault();
  });
  win.webContents.on('will-attach-webview', (event) => event.preventDefault());
  win.webContents.on('did-finish-load', () => {
    win.webContents.setZoomFactor(1);
    win.webContents.setVisualZoomLevelLimits(1, 1);
    broadcast();
  });
  win.on('close', (event) => {
    if (!quitting) { event.preventDefault(); setVisible(false); }
  });
  win.loadFile(file);
}

function createWindows() {
  const display = activeDisplay();
  displayId = display.id;
  const shared = {
    frame: false, transparent: true, backgroundColor: '#00000000',
    show: false, resizable: false, maximizable: false, minimizable: false,
    fullscreenable: false, hasShadow: false, skipTaskbar: true,
    webPreferences: {
      preload: path.join(__dirname, 'preload.js'),
      contextIsolation: true, nodeIntegration: false, sandbox: true,
      webSecurity: true, devTools: !app.isPackaged,
      backgroundThrottling: false,
    },
  };
  overlay = new BrowserWindow({ ...shared, ...display.bounds, title: `GGD-help V${app.getVersion()} · 会议蒙版` });
  // Windows clamps a newly created window to the work area (excluding the
  // taskbar), even when explicit monitor bounds were supplied to the ctor.
  // Reapply them after creation and show. Fractional DPI can round the native
  // result outward by one DIP; preserving that coverage avoids clipped edges.
  overlay.setBounds(display.bounds);
  overlay.setAlwaysOnTop(true, 'screen-saver');
  overlay.setVisibleOnAllWorkspaces(true, { visibleOnFullScreen: true });
  updateMouseMode();

  const saved = settings.launcherPosition;
  const targetDisplay = saved ? screen.getDisplayNearestPoint(saved) : display;
  const position = clampLauncher(saved || {
    x: display.workArea.x + display.workArea.width - LAUNCHER_SIZE.width - 16,
    y: display.workArea.y + 16,
  }, targetDisplay.workArea, LAUNCHER_SIZE);
  launcher = new BrowserWindow({ ...shared, ...position, ...LAUNCHER_SIZE, focusable: false, title: 'GGD-help · 悬浮开关' });
  launcher.setAlwaysOnTop(true, 'screen-saver');
  launcher.setVisibleOnAllWorkspaces(true, { visibleOnFullScreen: true });
  launcher.on('move', () => {
    const { x, y } = launcher.getBounds();
    settings.launcherPosition = { x, y };
    clearTimeout(saveTimer);
    saveTimer = setTimeout(saveSettings, 350);
  });
  overlay.on('focus', keepLauncherAbove);
  overlay.once('ready-to-show', () => {
    if (visible) {
      overlay.showInactive();
      overlay.setBounds(activeDisplay().bounds);
    }
    keepLauncherAbove();
  });
  launcher.once('ready-to-show', keepLauncherAbove);
  secureWindow(overlay, 'index.html');
  secureWindow(launcher, 'launcher.html');
}

function makeTray() {
  // Native image pixels keep the tray self-contained (no downloaded asset required).
  const size = 24;
  const pixels = Buffer.alloc(size * size * 4);
  for (let y = 0; y < size; y += 1) {
    for (let x = 0; x < size; x += 1) {
      if ((x - 11.5) ** 2 + (y - 11.5) ** 2 > 115) continue;
      const i = (y * size + x) * 4;
      const inner = x > 6 && x < 17 && y > 6 && y < 17;
      pixels[i] = inner ? 25 : 90;
      pixels[i + 1] = inner ? 45 : 217;
      pixels[i + 2] = inner ? 35 : 164;
      pixels[i + 3] = 255;
    }
  }
  tray = new Tray(nativeImage.createFromBitmap(pixels, { width: size, height: size }));
  tray.setToolTip(`GGD-help V${app.getVersion()} · Ctrl+Shift+G 开关蒙版`);
  tray.setContextMenu(buildMenu());
  tray.on('double-click', restoreControls);
}

if (!app.requestSingleInstanceLock()) {
  app.quit();
} else {
  if (!process.env.OVERLAY_TEST_USER_DATA) {
    try {
      migrateLegacyData(app.getPath('userData'), path.join(app.getPath('appData'), 'goose-duck-help-overlay'));
    } catch {
      persistenceError = '旧版记录迁移未完成，原数据仍保留在旧配置目录。';
    }
  }
  app.on('second-instance', () => { if (overlay) restoreControls(); });
  app.whenReady().then(() => {
    readSettings();
    displayId = settings.displayId;
    Menu.setApplicationMenu(null);
    app.on('web-contents-created', (_event, contents) => {
      contents.session.setPermissionRequestHandler((_webContents, _permission, callback) => callback(false));
      contents.session.setPermissionCheckHandler(() => false);
    });
    registerIPC();
    createWindows();
    makeTray();
    for (const [accelerator, callback] of [
      ['CommandOrControl+Shift+G', () => setVisible(!visible)],
      ['CommandOrControl+Shift+L', () => setLocked(!locked)],
    ]) {
      if (!globalShortcut.register(accelerator, callback)) {
        shortcutErrors.push(`${accelerator.replace('CommandOrControl', 'Ctrl')} 被其他程序占用，请使用悬浮开关。`);
      }
    }
    const refreshDisplays = () => {
      displayId = activeDisplay().id;
      settings.displayId = displayId;
      overlay.setBounds(activeDisplay().bounds);
      restoreLauncher();
      saveSettings();
      broadcast();
    };
    screen.on('display-added', refreshDisplays);
    screen.on('display-removed', refreshDisplays);
    screen.on('display-metrics-changed', refreshDisplays);
    broadcast();
  }).catch((error) => {
    console.error('无法启动 GGD-help：', error);
    app.quit();
  });
}

app.on('activate', () => { if (overlay) restoreControls(); });
app.on('before-quit', () => {
  quitting = true;
  if (app.isReady()) saveSettings();
});
app.on('will-quit', () => {
  globalShortcut.unregisterAll();
  if (tray) tray.destroy();
});
app.on('window-all-closed', () => app.quit());
