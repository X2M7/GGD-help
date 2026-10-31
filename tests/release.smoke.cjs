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

async function main() {
  const executable = path.resolve(__dirname, '../release/GGD-help-V' + version + '-win-x64.exe');
  await fs.access(executable);
  const profile = await fs.mkdtemp(path.join(os.tmpdir(), 'ggd-help-release-qa-'));
  const port = await unusedPort();
  const env = { ...process.env, OVERLAY_TEST_USER_DATA: profile };
  delete env.ELECTRON_RUN_AS_NODE;
  const child = spawn(executable, ['--remote-debugging-port=' + port, '--remote-debugging-address=127.0.0.1'], {
    env, windowsHide: true, stdio: 'ignore',
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
    const pageDeadline = Date.now() + 15000;
    while (!overlay && Date.now() < pageDeadline) {
      overlay = context.pages().find(page => /\/index\.html$/.test(page.url()));
      if (!overlay) await new Promise(resolve => setTimeout(resolve, 50));
    }
    assert.ok(overlay, 'portable app loads the overlay');
    await overlay.locator('.player-card').first().waitFor({ state: 'visible' });
    assert.match(await overlay.title(), /GGD-help/);
    assert.equal(await overlay.locator('.player-card').count(), 16);
    await overlay.locator('#round-input').fill('2');
    await overlay.locator('#round-input').press('Tab');
    const card = overlay.locator('.player-card[data-player-id="2"]');
    await card.locator('[data-cause="炸死"]').click();
    assert.match(await card.locator('.death-status').innerText(), /第 2 轮 · 炸死/);
    const state = await overlay.evaluate(() => window.overlayAPI.getState());
    assert.equal(state.visible, true);
    assert.equal(state.locked, false);
    const launcher = context.pages().find(page => /\/launcher\.html$/.test(page.url()));
    assert.ok(launcher, 'portable app loads the separate floating switch');
    console.log('Portable release passed: self extraction, GGD-help title, both windows, 16 players and death recording.');
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
    console.log('Isolated release-test profile: ' + profile);
  }
}

main().catch(error => { console.error(error); process.exitCode = 1; });
