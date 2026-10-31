const test = require('node:test');
const assert = require('node:assert/strict');
const vm = require('node:vm');
const fs = require('node:fs');
const path = require('node:path');
const core = require('../core');

function near(actual, expected, epsilon = 1e-8) {
  assert.ok(Math.abs(actual - expected) < epsilon, `${actual} should be close to ${expected}`);
}

test('16 independent player records and normalized imported records', () => {
  const session = core.createSession();
  assert.equal(session.players.length, 16);
  assert.equal(session.round, 1);
  session.players[0].notes = '侦探查过';
  assert.equal(session.players[1].notes, '');
  assert.deepEqual(core.normalizeSession(null), core.createSession());
  assert.deepEqual(core.normalizeSession([]), core.createSession());
});

test('recorded death round is independent of later meeting rounds and cause changes', () => {
  const original = core.setRound(core.createSession(), 3);
  const killed = core.setDeath(original, 4, original.round, '刀杀');
  const nextRound = core.setRound(killed, 4);
  assert.equal(original.players[3].death, null);
  assert.deepEqual(nextRound.players[3].death, { round: 3, cause: '刀杀' });
  const corrected = core.setDeath(nextRound, 4, 2, '刺杀');
  assert.deepEqual(corrected.players[3].death, { round: 2, cause: '刺杀' });
  assert.deepEqual(killed.players[3].death, { round: 3, cause: '刀杀' });
});

test('reviving clears death while retaining role and notes', () => {
  const session = core.createSession();
  Object.assign(session.players[6], { name: '小蓝', role: '警长', notes: '第2轮跳警长' });
  const dead = core.setDeath(session, 7, 2, '投票出局');
  const alive = core.revivePlayer(dead, 7);
  assert.equal(alive.players[6].death, null);
  assert.equal(alive.players[6].role, '警长');
  assert.equal(alive.players[6].notes, '第2轮跳警长');
  assert.equal(dead.players[6].death.cause, '投票出局');
});

test('rounds and corrupted fields are bounded, invalid causes become unknown', () => {
  const raw = { round: -3, players: [{ id: 1, name: 'x'.repeat(200), role: 10, notes: '\0note', death: { round: 900, cause: '<bad>' } }] };
  const result = core.normalizeSession(raw);
  assert.equal(result.round, 1);
  assert.equal(result.players[0].name.length, 32);
  assert.equal(result.players[0].role, '');
  assert.equal(result.players[0].notes, 'note');
  assert.deepEqual(result.players[0].death, { round: 99, cause: '不明' });
  assert.equal(core.setRound(result, Infinity).round, 1);
  assert.equal(core.setRound(result, 4.7).round, 5);
  assert.equal(core.setRound(result, 100).round, 99);
  assert.equal(core.setDeath(result, 2, undefined, '刀杀').players[1].death.round, 1);
  assert.deepEqual(core.setDeath(result, 99, 1, '刀杀'), result);
});

test('explicit player IDs survive reordered saved data', () => {
  const result = core.normalizeSession({ players: [{ id: 2, name: '第二位' }, { id: 1, name: '第一位' }] });
  assert.equal(result.players[0].name, '第一位');
  assert.equal(result.players[1].name, '第二位');
  assert.equal(result.players[2].name, '');
});

test('role groups are deduplicated and contain the original roles', () => {
  const all = core.ROLE_GROUPS.flatMap((group) => group.roles);
  assert.equal(new Set(all).size, all.length);
  assert.ok(all.includes('警长'));
  assert.ok(all.includes('决斗呆呆'));
  assert.ok(all.includes('术士'));
});

test('default 1920×1080 positions align with original grid controls', () => {
  const positions = core.computePositions(core.createLayout(), 1920, 1080);
  assert.equal(positions.length, 16);
  near(positions[0].x, 198);
  near(positions[0].y, 273);
  near(positions[1].x - positions[0].x, 386);
  near(positions[4].y - positions[0].y, 178);
  near(positions[15].scale, 1);
});

test('every resolution preset produces finite positions with full cards on screen', () => {
  for (const preset of core.RESOLUTION_PRESETS) {
    const layout = { ...core.createLayout(), aspect: preset.aspect };
    for (const position of core.computePositions(layout, preset.width, preset.height)) {
      assert.ok(Number.isFinite(position.x) && Number.isFinite(position.y));
      assert.ok(position.x >= 0 && position.y >= 0);
      assert.ok(position.x + core.CARD_WIDTH * position.scale <= preset.width + 1e-8);
      assert.ok(position.y + core.CARD_HEIGHT * position.scale <= preset.height + 1e-8);
    }
  }
});

test('16:9 screen resolutions scale grid and cards proportionally', () => {
  const base = core.computePositions(core.createLayout(), 1920, 1080);
  for (const factor of [2 / 3, 5 / 6, 4 / 3, 2]) {
    const scaled = core.computePositions(core.createLayout(), 1920 * factor, 1080 * factor);
    scaled.forEach((position, i) => {
      near(position.x, base[i].x * factor);
      near(position.y, base[i].y * factor);
      near(position.scale, factor);
    });
  }
});

test('letterboxed 16:9 viewport centers within ultrawide and 16:10 displays', () => {
  assert.deepEqual(core.computeViewport(3440, 1440, '16:9'), { x: 440, y: 0, width: 2560, height: 1440 });
  assert.deepEqual(core.computeViewport(1920, 1200, '16:9'), { x: 0, y: 60, width: 1920, height: 1080 });
  assert.deepEqual(core.computeViewport(1920, 1200, '16:10'), { x: 0, y: 0, width: 1920, height: 1200 });
  assert.deepEqual(core.computeViewport(3440, 1440, 'stretch'), { x: 0, y: 0, width: 3440, height: 1440 });
  near(core.computePositions(core.createLayout(), 3440, 1440)[0].x, 440 + 198 * 4 / 3);
});

test('ultrawide resolution presets use their exact ratio without artificial side margins', () => {
  for (const [width, height] of [[2560, 1080], [3440, 1440]]) {
    const preset = core.RESOLUTION_PRESETS.find((item) => item.width === width && item.height === height);
    assert.ok(preset);
    const viewport = core.computeViewport(preset.width, preset.height, preset.aspect);
    near(viewport.x, 0);
    near(viewport.y, 0);
    near(viewport.width, preset.width);
    near(viewport.height, preset.height);
    const layout = core.normalizeLayout({ ...core.createLayout(), aspect: preset.aspect });
    assert.equal(layout.aspect, preset.aspect);
    near(core.computePositions(layout, preset.width, preset.height)[0].x, 198 / 1920 * preset.width);
  }
  // A manually selected exact 21:9 viewport still deliberately letterboxes.
  near(core.computeViewport(3440, 1440, '21:9').x, 40);
});

test('dragging in screen coordinates round-trips after offsets, spacing scale and letterboxing', () => {
  const layout = { ...core.createLayout(), scale: 1.2, offsetX: 0.07, offsetY: -0.04, cardScale: 1.3 };
  const before = JSON.stringify(layout);
  const moved = core.movePlayer(layout, 6, 800, 470, 3440, 1440);
  const actual = core.computePositions(moved, 3440, 1440)[5];
  near(actual.x, 800);
  near(actual.y, 470);
  assert.equal(JSON.stringify(layout), before);
  assert.deepEqual(moved.positions[4], layout.positions[4]);
});

test('cards can be moved into letterbox margins and remain accessible at edges', () => {
  let layout = core.movePlayer(core.createLayout(), 1, -900, 99999, 3440, 1440);
  let actual = core.computePositions(layout, 3440, 1440)[0];
  near(actual.x, 0);
  near(actual.y + core.CARD_HEIGHT * actual.scale, 1440);
  layout = core.movePlayer(layout, 1, 99999, -900, 3440, 1440);
  actual = core.computePositions(layout, 3440, 1440)[0];
  near(actual.x + core.CARD_WIDTH * actual.scale, 3440);
  near(actual.y, 0);
});

test('dragging still reaches screen edges with extreme offsets and small spacing', () => {
  const layout = { ...core.createLayout(), scale: 0.35, offsetX: 1, offsetY: -1 };
  const moved = core.movePlayer(layout, 1, 0, 1200, 3440, 1440);
  const actual = core.computePositions(moved, 3440, 1440)[0];
  near(actual.x, 0);
  near(actual.y, 1200);
});

test('layout normalization repairs malformed imports without shared fallback state', () => {
  const raw = { aspect: 'bad', offsetX: Infinity, offsetY: null, scale: 0, cardScale: 99, positions: [{ x: 'NaN', y: NaN }, null] };
  const result = core.normalizeLayout(raw);
  assert.equal(result.aspect, '16:9');
  assert.equal(result.offsetX, 0);
  assert.equal(result.offsetY, 0);
  assert.equal(result.scale, 0.35);
  assert.equal(result.cardScale, 1.8);
  assert.equal(result.positions.length, 16);
  assert.deepEqual(result.positions[0], core.createLayout().positions[0]);
  result.positions[0].x = 0;
  assert.notEqual(core.createLayout().positions[0].x, 0);
});

test('tiny dimensions and extreme calibration never leave cards off screen', () => {
  const layout = { ...core.createLayout(), offsetX: 1, offsetY: -1, scale: 2, cardScale: 1.8 };
  for (const [width, height] of [[1, 1], [50, 20], [800, 600], [2160, 3840]]) {
    for (const position of core.computePositions(layout, width, height)) {
      assert.ok(position.x >= 0 && position.y >= 0);
      assert.ok(position.x + core.CARD_WIDTH * position.scale <= width + 1e-8);
      assert.ok(position.y + core.CARD_HEIGHT * position.scale <= height + 1e-8);
    }
  }
});

test('profiles distinguish display identity, resolution and Windows scaling', () => {
  const display = { id: 100, bounds: { x: -1920, y: 0, width: 1920, height: 1080 }, scaleFactor: 1 };
  const key = core.profileKey(display);
  assert.equal(key, 'display:100|1920x1080|dpi:1');
  assert.notEqual(core.profileKey({ ...display, id: 101 }), key);
  assert.notEqual(core.profileKey({ ...display, scaleFactor: 1.5 }), key);
  assert.notEqual(core.profileKey({ ...display, bounds: { width: 2560, height: 1440 } }), key);
  assert.equal(core.profileKey({ ...display, bounds: { ...display.bounds, x: 0 } }), key);
});

test('browser build exposes only its namespace without requiring Node integration', () => {
  const context = vm.createContext({});
  vm.runInContext(fs.readFileSync(path.join(__dirname, '..', 'core.js'), 'utf8'), context);
  assert.equal(context.OverlayCore.createSession().players.length, 16);
  assert.equal(context.OverlayCore.createLayout().aspect, '16:9');
  assert.deepEqual(Object.keys(context), ['OverlayCore']);
});
