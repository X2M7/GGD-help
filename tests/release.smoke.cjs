'use strict';

// Launch the actual self-extracting download, rather than the unpacked build.
const assert = require('node:assert/strict');
const fs = require('node:fs/promises');
const os = require('node:os');
const path = require('node:path');
const net = require('node:net');
const { spawn } = require('node:child_process');
const { chromium } = require('playwright');
const { version } = require('../package.json');
const { createNativeMouse } = require('./native-mouse.cjs');

async function unusedPort() {
  const server = net.createServer();
  await new Promise((resolve, reject) => {
    server.once('error', reject);
    server.listen(0, '127.0.0.1', resolve);
  });
  const port = server.address().port;
  await new Promise(resolve => server.close(resolve));
  return port;
}

async function verifyNativeReleaseMouse(overlay, launcher) {
  if (process.platform !== 'win32') return;
  const state = await overlay.evaluate(() => window.overlayAPI.getState());
  const display = state.displays.find(item => item.id === state.displayId);
  // A fresh profile starts on the primary monitor. The source smoke additionally
  // uses Electron's full DIP conversion for displays with arbitrary origins.
  assert.ok(display?.primary && display.bounds.x === 0 && display.bounds.y === 0,
    'portable native mouse verification uses the fresh profile on the primary display');
  const mouse = await createNativeMouse();
  const point = async (page, selector) => page.locator(selector).evaluate(element => {
    const bounds = element.getBoundingClientRect();
    return { x: (screenX + bounds.left + bounds.width / 2) * devicePixelRatio,
      y: (screenY + bounds.top + bounds.height / 2) * devicePixelRatio };
  });
  try {
    await mouse.move({ x: 8 * display.scaleFactor, y: 300 * display.scaleFactor });
    await mouse.click(await point(overlay, '#round-next'));
    await overlay.waitForFunction(() => document.getElementById('round-input').value === '2');
    await mouse.click(await point(overlay, '.player-card[data-player-id="1"] .player-notes'));
    await overlay.waitForFunction(() => document.activeElement === document.querySelector('.player-card[data-player-id="1"] .player-notes'));
    await mouse.click(await point(overlay, '#round-prev'));
    await overlay.waitForFunction(() => document.getElementById('round-input').value === '1');
    await mouse.click(await point(overlay, '#lock-button'));
    await overlay.waitForFunction(async () => (await window.overlayAPI.getState()).locked);
    await mouse.click(await point(launcher, '#lock'));
    await overlay.waitForFunction(async () => !(await window.overlayAPI.getState()).locked);
    await mouse.click(await point(overlay, '#hide-button'));
    await launcher.waitForFunction(async () => !(await window.overlayAPI.getState()).visible);
    await mouse.click(await point(launcher, '#toggle'));
    await overlay.waitForFunction(async () => (await window.overlayAPI.getState()).visible);
  } finally { await mouse.close(); }
}

async function main() {
  const executable = path.resolve(__dirname, '../release/GGD-help-V' + version + '-win-x64.exe');
  await fs.access(executable);
  const profile = await fs.mkdtemp(path.join(os.tmpdir(), 'ggd-help-release-qa-'));
  // Simulate downloading just the exe into an otherwise empty directory. This
  // catches accidental dependencies on the unpacked build beside the release.
  const downloadDirectory = path.join(profile, 'single-file-download');
  await fs.mkdir(downloadDirectory);
  const downloadedExecutable = path.join(downloadDirectory, path.basename(executable));
  await fs.copyFile(executable, downloadedExecutable);
  const port = await unusedPort();
  const env = { ...process.env, OVERLAY_TEST_USER_DATA: profile };
  delete env.ELECTRON_RUN_AS_NODE;
  const child = spawn(downloadedExecutable, [
    '--remote-debugging-port=' + port, '--remote-debugging-address=127.0.0.1',
    '--lang=zh-CN', '--proxy-server=http://127.0.0.1:9', '--proxy-bypass-list=<-loopback>',
  ], {
    env, cwd: downloadDirectory, windowsHide: true, stdio: 'ignore',
  });
  let spawnError;
  child.once('error', error => { spawnError = error; });
  let browser;
  let overlay;
  try {
    const endpoint = 'http://127.0.0.1:' + port;
    const deadline = Date.now() + 90000;
    while (!browser && Date.now() < deadline) {
      if (spawnError) throw spawnError;
      if (child.exitCode !== null) throw new Error('Portable launcher exited before opening the application: ' + child.exitCode);
      try {
        const response = await fetch(endpoint + '/json/version', { signal: AbortSignal.timeout(1000) });
        if (response.ok) browser = await chromium.connectOverCDP(endpoint);
      } catch { /* First launch needs time to unpack Electron into its temporary directory. */ }
      if (!browser) await new Promise(resolve => setTimeout(resolve, 250));
    }
    assert.ok(browser, 'portable exe exposes the running app after extraction');
    const context = browser.contexts()[0];
    // Keep CDP itself reachable through localhost while disabling network access
    // for the application's pages. The Windows network settings stay untouched.
    await context.setOffline(true);
    const pageDeadline = Date.now() + 15000;
    while (!overlay && Date.now() < pageDeadline) {
      overlay = context.pages().find(page => /\/index\.html$/.test(page.url()));
      if (!overlay) await new Promise(resolve => setTimeout(resolve, 50));
    }
    assert.ok(overlay, 'portable app loads the overlay');
    await overlay.reload();
    await overlay.locator('.player-card').first().waitFor({ state: 'visible' });
    assert.match(await overlay.title(), /GGD-help/);
    assert.equal(await overlay.locator('html').getAttribute('lang'), 'zh-CN');
    assert.match(await overlay.locator('#settings-toggle').innerText(), /设置/);
    const localizedValidation = await overlay.evaluate(() => {
      const input = document.createElement('input');
      input.type = 'number';
      input.max = '99';
      input.value = '100';
      return input.validationMessage;
    });
    assert.match(localizedValidation, /[\u4e00-\u9fff]/, 'the retained zh-CN locale localizes built-in Chromium controls');
    const launcher = context.pages().find(page => /\/launcher\.html$/.test(page.url()));
    assert.ok(launcher, 'portable app loads the separate floating switch');
    await launcher.locator('#toggle').waitFor({ state: 'visible' });
    await verifyNativeReleaseMouse(overlay, launcher);
    assert.equal(await overlay.locator('.player-card').count(), 16);
    assert.equal(await overlay.locator('.player-name, .player-number').count(), 0, 'release removes the player name/number row');
    assert.equal(await overlay.locator('.player-card .role-select').count(), 48, 'release includes all three identity selectors per player');
    await overlay.locator('#round-input').fill('2');
    await overlay.locator('#round-input').press('Tab');
    const card = overlay.locator('.player-card[data-player-id="2"]');
    const transparentBand = await card.evaluate(element => {
      const rect = element.getBoundingClientRect();
      const controls = element.querySelector('.player-controls').getBoundingClientRect();
      const hit = document.elementFromPoint(rect.left + rect.width / 2, (rect.top + controls.top) / 2);
      return { gap: controls.top - rect.top, interactive: Boolean(hit?.closest('[data-interactive]')) };
    });
    assert.ok(transparentBand.gap > 0);
    assert.equal(transparentBand.interactive, false, 'the former nickname region remains click-through in DOM hit-testing');
    await card.locator('.role-select[data-role-group="good"]').selectOption({ label: '警长' });
    await card.locator('.role-select[data-role-group="neutral"]').selectOption({ label: '呆呆' });
    await card.locator('.role-select[data-role-group="bad"]').selectOption({ label: '刺客' });
    await card.locator('.player-notes').fill('发布包第一条记录');
    await card.locator('.player-notes-extra').fill('发布包第二条独立记录');
    await card.locator('[data-cause="炸死"]').click();
    assert.match(await card.locator('.death-status').innerText(), /第 2 轮 · 炸死/);
    await overlay.waitForFunction(() => {
      const player = JSON.parse(localStorage.getItem('goose-duck-overlay:v2')).session.players[1];
      return player.roles.good === '警长' && player.roles.neutral === '呆呆' && player.roles.bad === '刺客'
        && player.notes === '发布包第一条记录' && player.notes2 === '发布包第二条独立记录'
        && player.death.round === 2 && player.death.cause === '炸死';
    });
    const recordedSession = await overlay.evaluate(() => JSON.parse(localStorage.getItem('goose-duck-overlay:v2')).session);
    await overlay.locator('#settings-toggle').click();
    for (const [key, selectors] of [
      ['roles', ['.player-roles']], ['notes', ['.player-notes-row']], ['death', ['.death-actions', '.death-record']],
    ]) {
      assert.equal(await overlay.locator('#feature-' + key).isChecked(), true, `${key} defaults to visible`);
      await overlay.locator('#feature-' + key).uncheck();
      for (const selector of selectors) assert.equal(await card.locator(selector).isVisible(), false, `${selector} hides in the release`);
    }
    assert.equal(await card.locator('.player-controls').isVisible(), false, 'disabled features do not leave an empty panel');
    await overlay.locator('#calibration-toggle').check();
    assert.equal(await card.locator('.drag-handle').isVisible(), true, 'calibration works when all features are disabled');
    await overlay.locator('#calibration-toggle').uncheck();
    await overlay.reload();
    await card.waitFor({ state: 'visible' });
    assert.equal(await card.locator('.player-controls').isVisible(), false, 'feature choices survive reload in the portable exe');
    await overlay.locator('#settings-toggle').click();
    for (const key of ['roles', 'notes', 'death']) {
      assert.equal(await overlay.locator('#feature-' + key).isChecked(), false);
      await overlay.locator('#feature-' + key).check();
    }
    await overlay.locator('#settings-close').click();
    assert.deepEqual(await overlay.evaluate(() => JSON.parse(localStorage.getItem('goose-duck-overlay:v2')).session), recordedSession,
      're-enabling features preserves every recorded value');
    assert.equal(await card.locator('.role-select[data-role-group="good"]').inputValue(), '警长');
    assert.equal(await card.locator('.role-select[data-role-group="neutral"]').inputValue(), '呆呆');
    assert.equal(await card.locator('.role-select[data-role-group="bad"]').inputValue(), '刺客');
    assert.equal(await card.locator('.player-notes').inputValue(), '发布包第一条记录');
    assert.equal(await card.locator('.player-notes-extra').inputValue(), '发布包第二条独立记录');
    assert.match(await card.locator('.death-status').innerText(), /第 2 轮 · 炸死/);
    const state = await overlay.evaluate(() => window.overlayAPI.getState());
    assert.equal(state.visible, true);
    assert.equal(state.locked, false);
    await fs.mkdir(path.resolve(__dirname, '../artifacts'), { recursive: true });
    // Let the transparent native compositor finish repainting after reopening
    // feature groups, then retain both CDP and desktop captures for review.
    await new Promise(resolve => setTimeout(resolve, 1000));
    await overlay.screenshot({ path: path.resolve(__dirname, '../artifacts/release-V' + version + '.png'), omitBackground: true });
    const activeDisplay = state.displays.find(display => display.id === state.displayId);
    if (process.platform === 'win32' && activeDisplay?.primary && activeDisplay.bounds.x === 0 && activeDisplay.bounds.y === 0) {
      const mouse = await createNativeMouse();
      try {
        await mouse.capture({ x: 0, y: 0, width: Math.floor(activeDisplay.bounds.width * activeDisplay.scaleFactor),
          height: Math.floor(activeDisplay.bounds.height * activeDisplay.scaleFactor),
          path: path.resolve(__dirname, '../artifacts/release-native-V' + version + '.png') });
      } finally { await mouse.close(); }
    }
    console.log('Portable release passed: standalone exe extraction, offline renderer startup, Chinese Chromium locale, native mouse controls and floating switch, transparent name regions, independent identities and notes, all feature toggles, calibration with features disabled, death recording and reload persistence.');
  } finally {
    if (overlay && !overlay.isClosed()) {
      await overlay.evaluate(() => window.overlayAPI.quit()).catch(() => {});
    }
    if (browser) await browser.close().catch(() => {});
    if (child.exitCode === null) {
      await Promise.race([
        new Promise(resolve => child.once('exit', resolve)),
        new Promise(resolve => setTimeout(resolve, 5000)),
      ]);
    }
    await fs.unlink(downloadedExecutable).catch(() => {});
    console.log('Isolated release-test profile: ' + profile);
  }
}

main().catch(error => { console.error(error); process.exitCode = 1; });
