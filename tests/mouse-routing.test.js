'use strict';

const test = require('node:test');
const assert = require('node:assert/strict');
const { sanitizeInteractiveRegions, hitsInteractiveRegion, shouldIgnoreMouse } = require('../shell-state');

test('rejects malformed or oversized renderer regions without widening the clickable area', () => {
  const valid = { x: 10.5, y: 20.25, width: 250, height: 122 };
  assert.deepEqual(sanitizeInteractiveRegions([
    valid, null, { ...valid, width: -1 }, { ...valid, y: NaN },
    { ...valid, x: '10' }, { ...valid, height: Infinity }, { ...valid, width: 100001 },
  ]), [valid]);
  assert.deepEqual(sanitizeInteractiveRegions({ regions: [valid] }), []);
  assert.deepEqual(sanitizeInteractiveRegions(Array(257).fill(valid)), []);
});

test('transparent nickname strip and unused card area stay pass-through', () => {
  const bounds = { x: 0, y: 0, width: 1920, height: 1080 };
  const regions = [{ x: 198, y: 302, width: 250, height: 122 }];
  assert.equal(hitsInteractiveRegion({ x: 220, y: 280 }, bounds, regions), false);
  assert.equal(hitsInteractiveRegion({ x: 220, y: 302 }, bounds, regions), true);
  assert.equal(hitsInteractiveRegion({ x: 448, y: 310 }, bounds, regions), false);
  assert.equal(hitsInteractiveRegion({ x: 220, y: 424 }, bounds, regions), false);
});

test('native DIP coordinates match CSS controls at fractional DPI on a secondary display', () => {
  // A 2560 × 1600 display at 150% has approximately 1707 × 1067 DIP.
  const bounds = { x: -1707, y: -1067, width: 1707, height: 1067 };
  const regions = [{ x: 100.5, y: 200.5, width: 200, height: 90 }];
  assert.equal(hitsInteractiveRegion({ x: -1600, y: -860 }, bounds, regions), true);
  assert.equal(hitsInteractiveRegion({ x: -1550, y: -900 }, bounds, regions), false);
  assert.equal(hitsInteractiveRegion({ x: 100, y: 200 }, bounds, regions), false);
});

test('clips overflowing control regions to the overlay window', () => {
  const bounds = { x: 2000, y: 0, width: 1280, height: 720 };
  const regions = [{ x: -10, y: -10, width: 1500, height: 900 }];
  assert.equal(hitsInteractiveRegion({ x: 2000, y: 0 }, bounds, regions), true);
  assert.equal(hitsInteractiveRegion({ x: 1999, y: 10 }, bounds, regions), false);
  assert.equal(hitsInteractiveRegion({ x: 3280, y: 10 }, bounds, regions), false);
  assert.equal(hitsInteractiveRegion({ x: 2010, y: 720 }, bounds, regions), false);
});

test('native hit-testing restores mouse input without any forwarded renderer mouse event', () => {
  const state = {
    visible: true, locked: false, captured: false,
    bounds: { x: 0, y: 0, width: 1920, height: 1080 },
    regions: [{ x: 200, y: 300, width: 250, height: 122 }],
  };
  assert.equal(shouldIgnoreMouse({ ...state, point: { x: 50, y: 50 } }), true);
  assert.equal(shouldIgnoreMouse({ ...state, point: { x: 230, y: 330 } }), false);
  assert.equal(shouldIgnoreMouse({ ...state, point: { x: 800, y: 500 } }), true);
  assert.equal(shouldIgnoreMouse({ ...state, regions: [], point: { x: 230, y: 330 } }), true);
});

test('drag and dropdown capture survives leaving a card but never overrides hidden or locked mode', () => {
  const state = {
    visible: true, locked: false, captured: true,
    point: { x: 800, y: 800 }, bounds: { x: 0, y: 0, width: 1920, height: 1080 }, regions: [],
  };
  assert.equal(shouldIgnoreMouse(state), false);
  assert.equal(shouldIgnoreMouse({ ...state, captured: false }), true);
  assert.equal(shouldIgnoreMouse({ ...state, locked: true }), true);
  assert.equal(shouldIgnoreMouse({ ...state, visible: false }), true);
});
