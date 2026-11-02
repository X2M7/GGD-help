'use strict';

// A real Electron smoke test. It never uses the player's normal saved settings.
const assert = require('node:assert/strict');
const fs = require('node:fs/promises');
const os = require('node:os');
const path = require('node:path');
const { _electron: electron } = require('playwright');
const { createNativeMouse } = require('./native-mouse.cjs');

const project = path.resolve(__dirname, '..');
const artifacts = path.join(project, 'artifacts');

async function openApp(profile, errors) {
  const env = { ...process.env, OVERLAY_TEST_USER_DATA: profile };
  delete env.ELECTRON_RUN_AS_NODE;
  const application = await electron.launch({ args: [project], env, timeout: 45000 });
  application.context().setDefaultTimeout(10000);
  const seen = new Set();
  const watch = page => {
    if (seen.has(page)) return;
    seen.add(page);
    page.on('pageerror', error => errors.push(`${page.url()}: ${error.message}`));
    page.on('console', message => {
      if (message.type() === 'error') errors.push(`${page.url()}: ${message.text()}`);
    });
  };
  application.on('window', watch);
  application.windows().forEach(watch);
  await application.firstWindow();
  await application.evaluate(async ({ BrowserWindow }) => {
    const deadline = Date.now() + 10000;
    while (BrowserWindow.getAllWindows().length < 2) {
      if (Date.now() > deadline) throw new Error('Expected overlay and launcher windows.');
      await new Promise(resolve => setTimeout(resolve, 50));
    }
  });
  const waitForWindow = async pattern => {
    const deadline = Date.now() + 10000;
    while (Date.now() < deadline) {
      const page = application.windows().find(item => pattern.test(item.url()));
      if (page) return page;
      await new Promise(resolve => setTimeout(resolve, 50));
    }
    throw new Error(`Window did not load: ${pattern}`);
  };
  const overlay = await waitForWindow(/\/index\.html(?:\?|$)/);
  const launcher = await waitForWindow(/\/launcher\.html(?:\?|$)/);
  assert.ok(overlay, 'overlay window exists');
  assert.ok(launcher, 'launcher window exists');
  await overlay.waitForLoadState('domcontentloaded');
  await launcher.waitForLoadState('domcontentloaded');
  await overlay.waitForFunction(() => Boolean(window.overlayAPI && window.OverlayCore));
  await application.evaluate(async ({ BrowserWindow }) => {
    const deadline = Date.now() + 10000;
    while (BrowserWindow.getAllWindows().some(win => !win.isVisible())) {
      if (Date.now() > deadline) throw new Error('Windows did not become visible.');
      await new Promise(resolve => setTimeout(resolve, 25));
    }
  });
  await overlay.evaluate(() => new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve))));
  return { application, overlay, launcher };
}

async function windowInfo(application) {
  return application.evaluate(({ BrowserWindow }) => BrowserWindow.getAllWindows().map(win => ({
    url: win.webContents.getURL(),
    bounds: win.getBounds(),
    alwaysOnTop: win.isAlwaysOnTop(),
    visible: win.isVisible(),
    background: win.getBackgroundColor(),
    preferences: win.webContents.getLastWebPreferences(),
  })));
}

async function waitForState(page, key, value) {
  await page.waitForFunction(async ({ key, value }) => (await window.overlayAPI.getState())[key] === value, { key, value });
}

async function verifyNativeMouse(application, overlay, launcher) {
  if (process.platform !== 'win32') return;
  const mouse = await createNativeMouse();
  let backdrop;
  const physicalPoint = async (page, point) => application.evaluate(({ BrowserWindow, screen }, { url, point }) => {
    const bounds = BrowserWindow.getAllWindows().find(win => win.webContents.getURL() === url).getBounds();
    return screen.dipToScreenPoint({ x: Math.round(bounds.x + point.x), y: Math.round(bounds.y + point.y) });
  }, { url: page.url(), point });
  const click = async (page, selector) => {
    const rect = await page.locator(selector).boundingBox();
    assert.ok(rect, `native click target is visible: ${selector}`);
    await mouse.click(await physicalPoint(page, { x: rect.x + rect.width / 2, y: rect.y + rect.height / 2 }));
  };
  try {
    await application.evaluate(({ BrowserWindow }) => {
      const win = BrowserWindow.getAllWindows().find(item => /\/index\.html/.test(item.webContents.getURL()));
      win.__qaOriginalIgnoreMouseEvents = win.setIgnoreMouseEvents;
      win.setIgnoreMouseEvents = function (ignore) {
        return this.__qaOriginalIgnoreMouseEvents(ignore, { forward: false });
      };
      win.setIgnoreMouseEvents(true);
    });
    // Native SendInput enters via the HWND, so a window that ignores all mouse
    // events cannot pass this test the way it can pass Playwright/CDP clicks.
    // Explicitly suppress forwarded mouse-move events to reproduce the missing
    // event path reported on Windows 10, even when this test runs on Windows 11.
    await mouse.move(await physicalPoint(overlay, { x: 8, y: 300 }));
    await click(overlay, '#round-next');
    await overlay.waitForFunction(() => document.getElementById('round-input').value === '2');
    await click(overlay, '#round-prev');
    await overlay.waitForFunction(() => document.getElementById('round-input').value === '1');
    const note = '.player-card[data-player-id="1"] .player-notes';
    await click(overlay, note);
    await overlay.waitForFunction(selector => document.activeElement === document.querySelector(selector), note);

    const role = '.player-card[data-player-id="1"] .role-select[data-role-group="good"]';
    const selectBounds = await overlay.locator(role).boundingBox();
    await click(overlay, role);
    await overlay.waitForFunction(selector => document.querySelector(selector).matches(':open'), role);
    const popupOrigin = await physicalPoint(overlay, { x: selectBounds.x, y: selectBounds.y });
    await mouse.capture({ ...popupOrigin, width: 300, height: 600, path: path.join(artifacts, 'native-select.png') });
    // Chromium's Windows popup starts below the select with its placeholder row.
    // Click the following identity row, using the captured native popup as a QA
    // artifact. This click must reach the popup rather than falling through it.
    await mouse.click(await physicalPoint(overlay, {
      x: selectBounds.x + selectBounds.width / 2,
      y: selectBounds.y + selectBounds.height * 2.7,
    }));
    await overlay.waitForFunction(selector => Boolean(document.querySelector(selector).value), role);
    assert.ok(await overlay.locator(role).inputValue(), 'a role is selected with real Windows mouse clicks');

    await click(overlay, '.player-card[data-player-id="1"] [data-cause="刀杀"]');
    await overlay.waitForFunction(() => JSON.parse(localStorage.getItem('goose-duck-overlay:v2')).session.players[0].death?.cause === '刀杀');
    await click(overlay, '.player-card[data-player-id="1"] .revive-button');
    await overlay.waitForFunction(() => JSON.parse(localStorage.getItem('goose-duck-overlay:v2')).session.players[0].death === null);

    const namePoint = await overlay.locator('.player-card[data-player-id="1"]').evaluate(element => {
      const rect = element.getBoundingClientRect();
      const controls = element.querySelector('.player-controls').getBoundingClientRect();
      return { x: rect.left + rect.width / 2, y: (rect.top + controls.top) / 2 };
    });
    backdrop = await application.evaluateHandle(async ({ BrowserWindow }, point) => {
      const overlayWindow = BrowserWindow.getAllWindows().find(win => /\/index\.html/.test(win.webContents.getURL()));
      const bounds = overlayWindow.getBounds();
      const win = new BrowserWindow({ x: Math.round(bounds.x + point.x - 45), y: Math.round(bounds.y + point.y - 9),
        width: 90, height: 18, frame: false, show: false, skipTaskbar: true,
        webPreferences: { contextIsolation: true, nodeIntegration: false, sandbox: true } });
      await win.loadURL('data:text/html,<body style="margin:0;background:%2337424d"><script>window.nativeClicks=0;document.addEventListener("click",()=>window.nativeClicks++)</script>');
      win.showInactive();
      return win;
    }, namePoint);
    await mouse.click(await physicalPoint(overlay, namePoint));
    const clicks = await backdrop.evaluate(win => win.webContents.executeJavaScript('window.nativeClicks'));
    assert.equal(clicks, 1, 'the transparent nickname area sends native clicks to the window behind it');
    await backdrop.evaluate(win => win.destroy());
    backdrop = null;
    await click(overlay, '#lock-button');
    await waitForState(overlay, 'locked', true);
    await click(launcher, '#lock');
    await waitForState(overlay, 'locked', false);
    await click(overlay, '#hide-button');
    await waitForState(launcher, 'visible', false);
    await click(launcher, '#toggle');
    await waitForState(overlay, 'visible', true);
    console.log(`Native Windows mouse passed on ${os.release()} with mouse forwarding disabled: toolbar, note focus, select popup, death controls, transparent nickname passthrough and floating switch.`);
  } finally {
    if (backdrop) await backdrop.evaluate(win => win.destroy()).catch(() => {});
    await application.evaluate(({ BrowserWindow }) => {
      const win = BrowserWindow.getAllWindows().find(item => /\/index\.html/.test(item.webContents.getURL()));
      if (win?.__qaOriginalIgnoreMouseEvents) {
        win.setIgnoreMouseEvents = win.__qaOriginalIgnoreMouseEvents;
        delete win.__qaOriginalIgnoreMouseEvents;
      }
    }).catch(() => {});
    await mouse.close();
  }
}

async function verifyFeatureToggles(overlay, card) {
  const groups = [
    ['roles', '.player-roles'],
    ['notes', '.player-notes-row'],
    ['death', '.death-actions, .death-record'],
  ];
  const storedBefore = await overlay.evaluate(() => JSON.parse(localStorage.getItem('goose-duck-overlay:v2')).session);
  for (const [key, selector] of groups) {
    assert.equal(await overlay.locator('#feature-' + key).isChecked(), true, 'new settings default to enabled');
    await overlay.locator('#feature-' + key).uncheck();
    assert.equal(await overlay.locator('.player-card ' + selector.split(', ').join(', .player-card ')).filter({ visible: true }).count(), 0, `${key} is hidden for all players`);
    for (const [other, otherSelector] of groups) {
      if (other !== key) assert.equal(await card.locator(otherSelector).first().isVisible(), true, `disabling ${key} preserves ${other}`);
    }
    await overlay.locator('#feature-' + key).check();
    assert.equal(await card.locator(selector).first().isVisible(), true);
  }
  for (const [key] of groups) await overlay.locator('#feature-' + key).uncheck();
  assert.equal(await card.locator('.player-controls').isVisible(), false, 'all features off leaves no empty background');
  const hit = await card.evaluate(element => {
    const rect = element.getBoundingClientRect();
    return Boolean(document.elementFromPoint(rect.left + rect.width / 2, rect.top + rect.height / 2)?.closest('[data-interactive]'));
  });
  assert.equal(hit, false, 'all features off leaves player area click-through');
  await overlay.locator('#calibration-toggle').check();
  assert.equal(await card.locator('.drag-handle').isVisible(), true, 'calibration remains available with every feature off');
  await overlay.locator('#calibration-toggle').uncheck();
  const storedAfter = await overlay.evaluate(() => JSON.parse(localStorage.getItem('goose-duck-overlay:v2')));
  assert.deepEqual(storedAfter.session, storedBefore, 'feature toggles never erase player records');
  assert.deepEqual(storedAfter.preferences.features, { roles: false, notes: false, death: false });
  // Restore now; a second all-off check before restart proves disk persistence.
  for (const [key] of groups) await overlay.locator('#feature-' + key).check();
}

async function main() {
  const profile = await fs.mkdtemp(path.join(os.tmpdir(), 'ggd-help-qa-'));
  await fs.mkdir(artifacts, { recursive: true });
  const errors = [];
  let running;
  try {
    running = await openApp(profile, errors);
    const { application, overlay, launcher } = running;
    const info = await windowInfo(application);
    assert.equal(info.length, 2);
    for (const win of info) {
      assert.equal(win.alwaysOnTop, true, 'both windows stay above the game');
      // Electron's getter returns RGB on Windows, discarding the alpha channel.
      assert.ok(['#000000', '#00000000'].includes(win.background));
      assert.equal(win.preferences.contextIsolation, true);
      assert.equal(win.preferences.nodeIntegration, false);
      assert.equal(win.preferences.sandbox, true);
    }
    const initial = await overlay.evaluate(() => window.overlayAPI.getState());
    const display = initial.displays.find(item => item.id === initial.displayId);
    assert.ok(display, 'selected display is available');
    const physicalDisplay = await application.evaluate(({ screen }, id) => {
      const current = screen.getAllDisplays().find(item => item.id === id);
      return screen.dipToScreenRect(null, current.bounds);
    }, initial.displayId);
    console.log(`Display bounds: ${JSON.stringify(display.bounds)}; DIP conversion: ${JSON.stringify(physicalDisplay)}; scale ${display.scaleFactor}.`);
    const overlayBounds = info.find(win => /\/index\.html/.test(win.url)).bounds;
    for (const axis of ['x', 'y']) assert.ok(Math.abs(overlayBounds[axis] - display.bounds[axis]) <= 1, 'overlay origin follows its display');
    // Windows converts rounded DIP coordinates to physical pixels and back. At
    // fractional scaling it can round outward by one DIP; it must cover the full
    // monitor instead of being clamped to the taskbar's smaller work area.
    for (const dimension of ['width', 'height']) {
      assert.ok(overlayBounds[dimension] >= display.bounds[dimension] && overlayBounds[dimension] <= display.bounds[dimension] + 1,
        `overlay ${dimension} covers its display within one DIP rounding`);
    }
    assert.equal(initial.visible, true);
    assert.equal(initial.locked, false);

    const card = overlay.locator('.player-card[data-player-id="1"]');
    await card.waitFor({ state: 'visible' });
    assert.equal(await overlay.evaluate(() => getComputedStyle(document.body).backgroundColor), 'rgba(0, 0, 0, 0)');
    assert.equal(await overlay.locator('.player-name, .player-number').count(), 0, 'player number and nickname controls are removed');
    assert.equal(await overlay.locator('.player-card .role-select').count(), 48, 'every player has three independent identity selectors');
    const transparentBands = await overlay.evaluate(() => ({
      viewport: { width: innerWidth, height: innerHeight },
      points: Array.from(document.querySelectorAll('.player-card')).map(element => {
        const rect = element.getBoundingClientRect();
        const controls = element.querySelector('.player-controls').getBoundingClientRect();
        const point = { x: rect.left + rect.width / 2, y: (rect.top + controls.top) / 2 };
        const hit = document.elementFromPoint(point.x, point.y);
        return { ...point, gap: controls.top - rect.top, interactive: Boolean(hit?.closest('[data-interactive]')) };
      }),
    }));
    for (const point of transparentBands.points) {
      assert.ok(point.gap > 0, 'the former name row remains an empty band above the controls');
      assert.equal(point.interactive, false, 'the empty name row does not intercept DOM pointer hit-testing');
    }
    const capture = await application.evaluate(async ({ BrowserWindow }, bands) => {
      const win = BrowserWindow.getAllWindows().find(item => /\/index\.html/.test(item.webContents.getURL()));
      const image = await win.webContents.capturePage();
      const bitmap = image.toBitmap();
      const size = image.getSize();
      let transparent = 0;
      let painted = 0;
      for (let i = 3; i < bitmap.length; i += 4) {
        if (bitmap[i] === 0) transparent += 1;
        if (bitmap[i] > 0) painted += 1;
      }
      const nameBandAlpha = bands.points.map(point => {
        const x = Math.floor(point.x * size.width / bands.viewport.width);
        const y = Math.floor(point.y * size.height / bands.viewport.height);
        return bitmap[(y * size.width + x) * 4 + 3];
      });
      return { transparent, painted, size, nameBandAlpha, png: image.toPNG().toString('base64') };
    }, transparentBands);
    await fs.writeFile(path.join(artifacts, 'overlay-native.png'), Buffer.from(capture.png, 'base64'));
    console.log(`Native capture: ${capture.size.width}x${capture.size.height}; ${capture.transparent} transparent pixels, ${capture.painted} painted pixels.`);
    assert.ok(capture.transparent > 100 && capture.painted > 100, 'native compositor preserves transparent empty regions');
    assert.deepEqual(capture.nameBandAlpha, Array(16).fill(0), 'every former nickname row is fully transparent in the native compositor');
    assert.equal(await overlay.locator('.player-card').count(), 16);
    await verifyNativeMouse(application, overlay, launcher);
    await overlay.locator('#round-input').fill('3');
    await overlay.locator('#round-input').press('Tab');
    await card.locator('.role-select[data-role-group="good"]').selectOption({ label: '警长' });
    await card.locator('.role-select[data-role-group="neutral"]').selectOption({ label: '呆呆' });
    await card.locator('.role-select[data-role-group="bad"]').selectOption({ label: '刺客' });
    await card.locator('.player-notes').fill('第三轮发言，待核实');
    await card.locator('.player-notes-extra').fill('另一条独立线索：与五号同行');
    await overlay.waitForFunction(() => {
      const player = JSON.parse(localStorage.getItem('goose-duck-overlay:v2')).session.players[0];
      return player.roles.good === '警长' && player.roles.neutral === '呆呆' && player.roles.bad === '刺客'
        && player.notes === '第三轮发言，待核实' && player.notes2 === '另一条独立线索：与五号同行';
    });
    await card.locator('[data-cause="刀杀"]').click();
    await overlay.waitForFunction(() => {
      const player = JSON.parse(localStorage.getItem('goose-duck-overlay:v2')).session.players[0];
      return player.death?.round === 3 && player.death.cause === '刀杀';
    });
    assert.match(await card.locator('.death-status').innerText(), /3/);
    assert.match(await card.locator('.death-status').innerText(), /刀杀/);
    await overlay.locator('#round-next').click();
    assert.equal(await overlay.locator('#round-input').inputValue(), '4');
    assert.equal(await card.locator('.death-round').inputValue(), '3', 'advancing the meeting preserves the recorded death round');
    await card.locator('.death-round').fill('2');
    await card.locator('.death-round').press('Tab');
    assert.match(await card.locator('.death-status').innerText(), /2/);
    await card.locator('.revive-button').click();
    await overlay.waitForFunction(() => JSON.parse(localStorage.getItem('goose-duck-overlay:v2')).session.players[0].death === null);
    await card.locator('[data-cause="投票出局"]').click();
    await overlay.waitForFunction(() => {
      const player = JSON.parse(localStorage.getItem('goose-duck-overlay:v2')).session.players[0];
      return player.death?.round === 4 && player.death.cause === '投票出局';
    });

    await overlay.locator('#settings-toggle').click();
    await overlay.locator('#settings-panel').waitFor({ state: 'visible' });
    await verifyFeatureToggles(overlay, card);
    await overlay.locator('#display-select').selectOption(String(initial.displayId));
    await waitForState(overlay, 'displayId', initial.displayId);
    await overlay.locator('#aspect-select').selectOption('16:10');
    await overlay.locator('#aspect-select').selectOption('16:9');
    await overlay.screenshot({ path: path.join(artifacts, 'overlay-settings.png'), omitBackground: true });
    await overlay.locator('#calibration-toggle').click();
    await overlay.locator('#settings-toggle').click();
    const before = await card.boundingBox();
    const handle = await card.locator('.drag-handle').boundingBox();
    assert.ok(before && handle, 'calibration provides a drag handle');
    await overlay.mouse.move(handle.x + handle.width / 2, handle.y + handle.height / 2);
    await overlay.mouse.down();
    await overlay.mouse.move(handle.x + handle.width / 2 + 24, handle.y + handle.height / 2 + 18, { steps: 8 });
    await overlay.mouse.up();
    const after = await card.boundingBox();
    assert.ok(Math.abs(after.x - before.x - 24) < 3, 'dragging changes the actual horizontal position');
    assert.ok(Math.abs(after.y - before.y - 18) < 3, 'dragging changes the actual vertical position');
    await overlay.waitForFunction(() => Object.keys(JSON.parse(localStorage.getItem('goose-duck-overlay:v2')).profiles).length > 0);
    const saved = await overlay.evaluate(() => JSON.parse(localStorage.getItem('goose-duck-overlay:v2')));

    await application.evaluate(({ BrowserWindow }) => BrowserWindow.getAllWindows().find(win => /\/index\.html/.test(win.webContents.getURL())).focus());
    await overlay.locator('#lock-button').click();
    await waitForState(overlay, 'locked', true);
    const lockedFocus = await application.evaluate(async ({ BrowserWindow }) => {
      const win = BrowserWindow.getAllWindows().find(item => /\/index\.html/.test(item.webContents.getURL()));
      const deadline = Date.now() + 1500;
      while (win.isFocused() && Date.now() < deadline) await new Promise(resolve => setTimeout(resolve, 25));
      return { focused: win.isFocused(), focusable: win.isFocusable() };
    });
    assert.deepEqual(lockedFocus, { focused: false, focusable: false }, 'locking releases game keyboard focus');
    await launcher.locator('#lock').click();
    await waitForState(overlay, 'locked', false);
    await overlay.locator('#hide-button').click();
    await waitForState(launcher, 'visible', false);
    assert.equal((await windowInfo(application)).find(win => /\/index\.html/.test(win.url)).visible, false);
    await launcher.locator('#toggle').click();
    await waitForState(overlay, 'visible', true);
    assert.equal((await windowInfo(application)).find(win => /\/index\.html/.test(win.url)).visible, true);

    // A native move verifies position persistence. CDP mouse events cannot prove
    // that Windows' non-client app-region dragging or click-through hit-testing works.
    const launcherWindow = await application.browserWindow(launcher);
    const savedLauncherBounds = await launcherWindow.evaluate(win => {
      const current = win.getBounds();
      win.setPosition(current.x - 30, current.y + 40);
      return win.getBounds();
    });

    await overlay.screenshot({ path: path.join(artifacts, 'overlay.png'), omitBackground: true });
    await launcher.screenshot({ path: path.join(artifacts, 'launcher.png'), omitBackground: true });
    await overlay.locator('#settings-toggle').click();
    for (const key of ['roles', 'notes', 'death']) await overlay.locator('#feature-' + key).uncheck();
    const savedPreferences = await overlay.evaluate(() => JSON.parse(localStorage.getItem('goose-duck-overlay:v2')).preferences);
    await application.close();
    running = null;
    running = await openApp(profile, errors);
    const restoredLauncherWindow = await running.application.browserWindow(running.launcher);
    const restoredLauncherBounds = await restoredLauncherWindow.evaluate(win => win.getBounds());
    assert.deepEqual({ x: restoredLauncherBounds.x, y: restoredLauncherBounds.y },
      { x: savedLauncherBounds.x, y: savedLauncherBounds.y }, 'floating switch position survives restart');
    await running.overlay.locator('.player-card[data-player-id="1"]').waitFor({ state: 'visible' });
    const restored = await running.overlay.evaluate(() => JSON.parse(localStorage.getItem('goose-duck-overlay:v2')));
    assert.deepEqual(restored.session, saved.session, 'meeting, identity, notes and death records survive restart');
    assert.deepEqual(restored.profiles, saved.profiles, 'calibrated positions survive restart');
    assert.deepEqual(restored.preferences, savedPreferences, 'feature choices survive a full application restart');
    const restoredCard = running.overlay.locator('.player-card[data-player-id="1"]');
    assert.equal(await restoredCard.locator('.player-controls').isVisible(), false, 'disabled features stay invisible after restart');
    assert.equal(await restoredCard.locator('.role-select[data-role-group="good"]').inputValue(), '警长');
    assert.equal(await restoredCard.locator('.role-select[data-role-group="neutral"]').inputValue(), '呆呆');
    assert.equal(await restoredCard.locator('.role-select[data-role-group="bad"]').inputValue(), '刺客');
    assert.equal(await restoredCard.locator('.player-notes').inputValue(), '第三轮发言，待核实');
    assert.equal(await restoredCard.locator('.player-notes-extra').inputValue(), '另一条独立线索：与五号同行');
    assert.equal(await restoredCard.locator('.death-round').inputValue(), '4');
    const restoredBounds = await restoredCard.boundingBox();
    assert.ok(Math.abs(restoredBounds.x - after.x) < 3 && Math.abs(restoredBounds.y - after.y) < 3, 'restored coordinates are applied');
    await running.overlay.locator('#settings-toggle').click();
    for (const key of ['roles', 'notes', 'death']) {
      assert.equal(await running.overlay.locator('#feature-' + key).isChecked(), false);
      await running.overlay.locator('#feature-' + key).check();
    }
    await running.overlay.locator('#settings-close').click();
    assert.equal(await restoredCard.locator('.player-roles').isVisible(), true);
    assert.equal(await restoredCard.locator('.player-notes-row').isVisible(), true);
    assert.equal(await restoredCard.locator('.death-record').isVisible(), true);
    assert.deepEqual(await running.overlay.evaluate(() => JSON.parse(localStorage.getItem('goose-duck-overlay:v2')).session), saved.session,
      'enabling features after restart restores the original records');
    await running.overlay.locator('#new-game').click();
    await running.overlay.locator('#confirm-new-game').click();
    await running.overlay.waitForFunction(() => {
      const session = JSON.parse(localStorage.getItem('goose-duck-overlay:v2')).session;
      return session.round === 1 && session.players.every(player => player.death === null
        && !player.name && !player.role && !player.roles.good && !player.roles.neutral && !player.roles.bad
        && !player.notes && !player.notes2);
    });
    const reset = await running.overlay.evaluate(() => JSON.parse(localStorage.getItem('goose-duck-overlay:v2')));
    assert.deepEqual(reset.profiles, saved.profiles, 'starting a new match preserves screen calibration');
    assert.deepEqual(errors, [], 'no renderer errors');
    console.log('Electron smoke test passed: native Windows mouse routing, transparent name rows, independent identities and notes, feature toggles and restart persistence, death records, display bounds, calibration, lock, visibility and match reset.');
  } catch (error) {
    if (running) {
      for (const [name, page] of [['overlay', running.overlay], ['launcher', running.launcher]]) {
        if (!page.isClosed()) await page.screenshot({ path: path.join(artifacts, `failure-${name}.png`) }).catch(() => {});
      }
    }
    throw error;
  } finally {
    if (running) await running.application.close().catch(() => {});
    // Retain the temporary profile for local diagnosis and reproducibility.
    console.log(`Isolated test profile: ${profile}`);
  }
}

main().catch(error => {
  console.error(error);
  process.exitCode = 1;
});
