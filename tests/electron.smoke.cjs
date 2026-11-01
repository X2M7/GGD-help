'use strict';

// A real Electron smoke test. It never uses the player's normal saved settings.
const assert = require('node:assert/strict');
const fs = require('node:fs/promises');
const os = require('node:os');
const path = require('node:path');
const { _electron: electron } = require('playwright');

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
    const restoredCard = running.overlay.locator('.player-card[data-player-id="1"]');
    assert.equal(await restoredCard.locator('.role-select[data-role-group="good"]').inputValue(), '警长');
    assert.equal(await restoredCard.locator('.role-select[data-role-group="neutral"]').inputValue(), '呆呆');
    assert.equal(await restoredCard.locator('.role-select[data-role-group="bad"]').inputValue(), '刺客');
    assert.equal(await restoredCard.locator('.player-notes').inputValue(), '第三轮发言，待核实');
    assert.equal(await restoredCard.locator('.player-notes-extra').inputValue(), '另一条独立线索：与五号同行');
    assert.equal(await restoredCard.locator('.death-round').inputValue(), '4');
    const restoredBounds = await restoredCard.boundingBox();
    assert.ok(Math.abs(restoredBounds.x - after.x) < 3 && Math.abs(restoredBounds.y - after.y) < 3, 'restored coordinates are applied');
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
    console.log('Electron smoke test passed: native transparent name rows, independent good/neutral/bad identities, two note fields, display bounds, death rounds, revival, calibration drag, lock, visibility, restart persistence and match reset.');
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
