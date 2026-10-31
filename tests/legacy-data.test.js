'use strict';

const assert = require('node:assert/strict');
const fs = require('node:fs');
const os = require('node:os');
const path = require('node:path');
const test = require('node:test');
const { migrateLegacyData } = require('../legacy-data');

function workspace(t) {
  const root = fs.mkdtempSync(path.join(os.tmpdir(), 'ggd-legacy-data-test-'));
  t.after(() => {
    // Remove only the unique directory created by this test.
    assert.equal(path.dirname(root), path.resolve(os.tmpdir()));
    assert.match(path.basename(root), /^ggd-legacy-data-test-/);
    fs.rmSync(root, { recursive: true, force: true });
  });
  return { root, legacy: path.join(root, 'legacy'), destination: path.join(root, 'new') };
}

function seedLegacy(legacy) {
  fs.mkdirSync(path.join(legacy, 'Local Storage', 'leveldb'), { recursive: true });
  fs.writeFileSync(path.join(legacy, 'Local Storage', 'leveldb', '000003.log'), 'saved roles, death rounds and positions');
  fs.writeFileSync(path.join(legacy, 'overlay-window.json'), JSON.stringify({ displayId: 42, launcherPosition: { x: 12, y: 24 } }));
  fs.mkdirSync(path.join(legacy, 'Cache'));
  fs.writeFileSync(path.join(legacy, 'Cache', 'unused'), 'old cache');
}

test('first migration copies records and window settings while retaining the originals', t => {
  const { legacy, destination } = workspace(t);
  seedLegacy(legacy);

  assert.equal(migrateLegacyData(destination, legacy), 'migrated');
  for (const relative of ['Local Storage/leveldb/000003.log', 'overlay-window.json']) {
    assert.deepEqual(fs.readFileSync(path.join(destination, relative)), fs.readFileSync(path.join(legacy, relative)));
  }
  assert.equal(fs.existsSync(path.join(legacy, 'Cache', 'unused')), true);
  assert.equal(fs.existsSync(path.join(destination, 'Cache')), false, 'unrelated browser caches are not migrated');
  assert.deepEqual(fs.readdirSync(destination).sort(), ['Local Storage', 'overlay-window.json'], 'successful migration leaves no staging directory');
});

test('an existing new record store is never replaced by older records', t => {
  const { legacy, destination } = workspace(t);
  seedLegacy(legacy);
  const newRecord = path.join(destination, 'Local Storage', 'leveldb', '000003.log');
  fs.mkdirSync(path.dirname(newRecord), { recursive: true });
  fs.writeFileSync(newRecord, 'new match in GGD-help');

  assert.equal(migrateLegacyData(destination, legacy), 'existing');
  assert.equal(fs.readFileSync(newRecord, 'utf8'), 'new match in GGD-help');
  assert.equal(fs.existsSync(path.join(destination, 'overlay-window.json')), false, 'does not mix old preferences into an existing new profile');
  assert.equal(fs.readFileSync(path.join(legacy, 'Local Storage', 'leveldb', '000003.log'), 'utf8'), 'saved roles, death rounds and positions');
});

test('existing new window settings also prevent a migration from overwriting the profile', t => {
  const { legacy, destination } = workspace(t);
  seedLegacy(legacy);
  fs.mkdirSync(destination);
  const newSettings = path.join(destination, 'overlay-window.json');
  fs.writeFileSync(newSettings, '{"displayId":99}');

  assert.equal(migrateLegacyData(destination, legacy), 'existing');
  assert.equal(fs.readFileSync(newSettings, 'utf8'), '{"displayId":99}');
  assert.equal(fs.existsSync(path.join(destination, 'Local Storage')), false);
});

test('missing legacy data is a harmless no-op', t => {
  const { legacy, destination } = workspace(t);
  assert.equal(migrateLegacyData(destination, legacy), 'absent');
  assert.equal(fs.existsSync(destination), false);
});

test('a copy failure retains the old data and does not install a partial new profile', t => {
  const { legacy, destination } = workspace(t);
  seedLegacy(legacy);
  const originalCopy = fs.cpSync;
  t.mock.method(fs, 'cpSync', (source, target, options) => {
    if (path.basename(source) === 'overlay-window.json') {
      const error = new Error('Simulated access failure while copying old settings');
      error.code = 'EACCES';
      throw error;
    }
    return originalCopy(source, target, options);
  });

  assert.throws(() => migrateLegacyData(destination, legacy), error => error.code === 'EACCES');
  assert.equal(fs.readFileSync(path.join(legacy, 'Local Storage', 'leveldb', '000003.log'), 'utf8'), 'saved roles, death rounds and positions');
  assert.equal(JSON.parse(fs.readFileSync(path.join(legacy, 'overlay-window.json'), 'utf8')).displayId, 42);
  assert.equal(fs.existsSync(path.join(destination, 'Local Storage')), false);
  assert.equal(fs.existsSync(path.join(destination, 'overlay-window.json')), false);
  const staging = fs.readdirSync(destination).find(name => name.startsWith('.legacy-migration-'));
  assert.ok(staging, 'the staged copy is retained for diagnosis rather than deleting the original');
  assert.equal(fs.readFileSync(path.join(destination, staging, 'Local Storage', 'leveldb', '000003.log'), 'utf8'), 'saved roles, death rounds and positions');
});
