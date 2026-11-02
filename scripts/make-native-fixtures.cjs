'use strict';

// Optional integration fixtures for the native, read-only LevelDB migrator.
// Requires the repository's Electron and Playwright development dependencies.
// Every Electron profile is newly created below the OS temporary directory;
// the player's real profile is never opened. Hidden windows avoid focus changes.
const { _electron } = require('playwright');
const fs = require('node:fs/promises');
const os = require('node:os');
const path = require('node:path');
const crypto = require('node:crypto');
const C = require('../core');

async function main() {
  const root = await fs.mkdtemp(path.join(os.tmpdir(), 'ggd-native-fixtures-'));
  const appFile = path.join(root, 'fixture-main.cjs');
  const htmlFile = path.join(root, 'fixture.html');
  await fs.writeFile(htmlFile, '<!doctype html><meta charset="utf-8"><title>GGD migration fixture</title>');
  await fs.writeFile(appFile, `
    const { app, BrowserWindow } = require('electron');
    app.setPath('userData', process.env.FIXTURE_PROFILE);
    app.whenReady().then(() => {
      const window = new BrowserWindow({ show: false });
      window.loadFile(${JSON.stringify(htmlFile)});
    });
    app.on('window-all-closed', () => app.quit());
  `);

  const summary = [];
  for (const name of ['wal', 'sst', 'deleted']) {
    const profile = path.join(root, name);
    await fs.mkdir(profile);
    const env = { ...process.env, FIXTURE_PROFILE: profile };
    delete env.ELECTRON_RUN_AS_NODE;
    const app = await _electron.launch({ executablePath: require('electron'), args: [appFile], env });
    try {
      const page = await app.firstWindow();
      await page.waitForLoadState('domcontentloaded');
      await page.waitForFunction(() => location.protocol === 'file:');
      const state = {
        version: 2,
        session: C.createSession(),
        preferences: C.normalizePreferences({ features: { notes: false } }),
        profiles: {},
      };
      state.session.players[0].notes = '测试记录：中文与 emoji 🦆';
      state.session.players[0].notes2 = '测试用第二备注';
      state.session.players[0].roles = { good: '警长', neutral: '呆呆', bad: '刺客' };
      state.session.players[0].death = { round: 3, cause: '刀杀' };
      state.session.round = 4;
      await page.evaluate(value => localStorage.setItem('goose-duck-overlay:v2', JSON.stringify(value)), state);

      // Flush enough anonymous filler to exercise SST tables, Snappy blocks,
      // fragmented WAL records, and a later update that supersedes old data.
      if (name !== 'wal') {
        for (let pass = 0; pass < 10; pass++) {
          const junk = crypto.randomBytes(25000).toString('base64');
          await page.evaluate(({ junk, pass }) => {
            for (let key = 0; key < 90; key++) localStorage.setItem('fixture-junk-' + key, pass + '-' + key + '-' + junk);
          }, { junk, pass });
          await app.evaluate(({ session }) => session.defaultSession.flushStorageData());
          await new Promise(resolve => setTimeout(resolve, 1600));
        }
      }
      state.session.players[0].notes += ' / 最后更新';
      state.session.round = 7;
      await page.evaluate(value => localStorage.setItem('goose-duck-overlay:v2', JSON.stringify(value)), state);
      if (name === 'deleted') await page.evaluate(() => localStorage.removeItem('goose-duck-overlay:v2'));
      await app.evaluate(({ session }) => session.defaultSession.flushStorageData());
      await fs.writeFile(path.join(profile, 'expected.json'), JSON.stringify(name === 'deleted' ? null : state));
      await new Promise(resolve => setTimeout(resolve, 2000));
    } finally {
      await app.close();
    }
    const files = await fs.readdir(path.join(profile, 'Local Storage', 'leveldb'));
    if (name !== 'wal' && !files.some(file => /\.(?:ldb|sst)$/.test(file))) {
      throw new Error(`Electron did not create an SST table for ${name}; do not treat this fixture as SST coverage.`);
    }
    summary.push({ name, profile, expected: name === 'deleted' ? 'deleted' : 'round 7, notes latest' });
  }

  const artifacts = path.resolve(__dirname, '..', 'artifacts');
  await fs.mkdir(artifacts, { recursive: true });
  const manifest = path.join(artifacts, 'native-fixtures.json');
  await fs.writeFile(manifest, JSON.stringify(summary, null, 2));
  console.log(JSON.stringify({ root, manifest, fixtures: summary }, null, 2));
  console.log('PowerShell verification:');
  console.log('$fixtures = (Get-Content -Raw -Encoding UTF8 artifacts/native-fixtures.json | ConvertFrom-Json).profile');
  console.log('.\\scripts\\test-native-models.ps1 -Fixtures $fixtures');
  // Keep fixtures for inspection and repeatable testing; no recursive cleanup.
}

main().catch(error => { console.error(error); process.exitCode = 1; });
