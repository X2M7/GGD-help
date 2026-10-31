const test = require('node:test');
const assert = require('node:assert/strict');
const { clampLauncher, sanitizeSettings } = require('../shell-state');

test('keeps launcher reachable on a secondary monitor with negative coordinates', () => {
  const area = { x: -1920, y: 0, width: 1920, height: 1040 };
  assert.deepEqual(clampLauncher({ x: 6000, y: -300 }, area, { width: 266, height: 66 }), { x: -266, y: 0 });
  assert.deepEqual(clampLauncher({ x: -1910, y: 100 }, area, { width: 266, height: 66 }), { x: -1910, y: 100 });
});

test('recovers launcher after display removal or resolution reduction', () => {
  const area = { x: 0, y: 0, width: 1280, height: 680 };
  assert.deepEqual(clampLauncher({ x: 3500, y: 1500 }, area, { width: 266, height: 66 }), { x: 1014, y: 614 });
  assert.deepEqual(clampLauncher({}, area, { width: 266, height: 66 }), { x: 0, y: 0 });
});

test('rejects malformed persisted data and never restores hidden or locked state', () => {
  assert.deepEqual(sanitizeSettings(null), {});
  assert.deepEqual(sanitizeSettings({ displayId: '1', launcherPosition: { x: '0', y: 0 } }), {});
  assert.deepEqual(sanitizeSettings({ displayId: 2, launcherPosition: { x: 12.4, y: -40.8 }, locked: true, visible: false }), {
    displayId: 2, launcherPosition: { x: 12, y: -41 },
  });
});
