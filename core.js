/* Shared, side-effect-free match and screen-layout logic. Works in Electron's
 * isolated renderer without Node integration, and in the Node test runner. */
(function (root, factory) {
  'use strict';
  const api = factory();
  if (typeof module === 'object' && module.exports) module.exports = api;
  else root.OverlayCore = api;
})(typeof globalThis !== 'undefined' ? globalThis : this, function () {
  'use strict';

  const PLAYER_COUNT = 16;
  const CARD_WIDTH = 250;
  const CARD_HEIGHT = 122;
  const BASE_WIDTH = 1920;
  const BASE_HEIGHT = 1080;
  const ASPECTS = Object.freeze(['16:9', '16:10', '21:9', '43:18', '64:27', 'stretch']);
  const DEATH_CAUSES = Object.freeze(['刀杀', '投票出局', '刺杀', '炸死', '吞食', '连带', '不明']);
  // The role names come from the original project, with its duplicate 秃鹫 removed.
  const ROLE_GROUPS = Object.freeze([
    Object.freeze({ label: '鹅', roles: Object.freeze(['好人', '肉汁', '正义使者', '警长', '加拿大', '殡仪员', '网红', '模仿', '侦探', '工程师', '复仇', '观鸟', '保镖', '政治家', '锁匠', '冒险家', '星界行者', '流浪儿童', '追踪者', '跟踪者']) }),
    Object.freeze({ label: '中立', roles: Object.freeze(['中立', '呆呆', '秃鹫', '鹈鹕', '鸽子', '猎鹰', '决斗呆呆']) }),
    Object.freeze({ label: '鸭', roles: Object.freeze(['坏比', '丧葬者', '隐形', '食鸭鸟', '变形者', '静语者', '专业杀手', '间谍', '刺客', '雇佣杀手', '告密者', '派对狂', '爆炸王', '身份窃贼', '忍者', '连环杀手', '术士', '超能力']) }),
  ]);
  const RESOLUTION_PRESETS = Object.freeze([
    [1280, 720, '16:9'], [1366, 768, '16:9'], [1600, 900, '16:9'],
    [1920, 1080, '16:9'], [2560, 1440, '16:9'], [3840, 2160, '16:9'],
    [1920, 1200, '16:10'], [2560, 1600, '16:10'],
    // Commercial "21:9" resolutions do not share the exact 21/9 ratio.
    [2560, 1080, '64:27'], [3440, 1440, '43:18'],
  ].map(([width, height, aspect]) => Object.freeze({ label: `${width} × ${height}`, width, height, aspect })));

  function object(value) {
    return value !== null && typeof value === 'object' && !Array.isArray(value) ? value : {};
  }

  function clamp(value, min, max) {
    return Math.min(max, Math.max(min, value));
  }

  function number(value, fallback, min, max) {
    // Do not coerce null/booleans/strings from a damaged settings file to coordinates.
    return typeof value === 'number' && Number.isFinite(value) ? clamp(value, min, max) : fallback;
  }

  function roundNumber(value, fallback = 1) {
    return Math.round(number(value, fallback, 1, 99));
  }

  function text(value, limit) {
    return typeof value === 'string' ? value.replace(/[\u0000-\u0008\u000b\u000c\u000e-\u001f\u007f]/g, '').slice(0, limit) : '';
  }

  function createSession(count = PLAYER_COUNT) {
    const length = Math.round(number(count, PLAYER_COUNT, 1, PLAYER_COUNT));
    return {
      round: 1,
      players: Array.from({ length }, (_, i) => ({ id: i + 1, name: '', role: '', notes: '', death: null })),
    };
  }

  function normalizeSession(raw) {
    const source = object(raw);
    const players = Array.isArray(source.players) ? source.players.slice(0, PLAYER_COUNT) : [];
    return {
      round: roundNumber(source.round),
      players: createSession().players.map((empty, index) => {
        // Explicit IDs survive reordering; old index-only records can still be read.
        const match = players.find((candidate) => object(candidate).id === empty.id);
        const indexed = object(players[index]);
        const player = object(match || (indexed.id === undefined ? indexed : null));
        const death = object(player.death);
        return {
          id: empty.id,
          name: text(player.name, 32),
          role: text(player.role, 32),
          notes: text(player.notes, 240),
          death: player.death && typeof player.death === 'object' && !Array.isArray(player.death)
            ? { round: roundNumber(death.round), cause: DEATH_CAUSES.includes(death.cause) ? death.cause : '不明' }
            : null,
        };
      }),
    };
  }

  function setRound(session, round) {
    const result = normalizeSession(session);
    result.round = roundNumber(round, result.round);
    return result;
  }

  function setDeath(session, id, round, cause) {
    const result = normalizeSession(session);
    const player = result.players.find((candidate) => candidate.id === id);
    if (player) player.death = {
      round: roundNumber(round, result.round),
      cause: DEATH_CAUSES.includes(cause) ? cause : '不明',
    };
    return result;
  }

  function revivePlayer(session, id) {
    const result = normalizeSession(session);
    const player = result.players.find((candidate) => candidate.id === id);
    if (player) player.death = null;
    return result;
  }

  function createLayout() {
    return {
      aspect: '16:9',
      offsetX: 0,
      offsetY: 0,
      scale: 1,
      cardScale: 1,
      // The original 1920px screenshot used 386px columns and 178px rows.
      // Notes start to the right of each portrait, leaving the game visible.
      positions: Array.from({ length: PLAYER_COUNT }, (_, index) => ({
        x: (198 + (index % 4) * 386) / BASE_WIDTH,
        y: (273 + Math.floor(index / 4) * 178) / BASE_HEIGHT,
      })),
    };
  }

  function normalizeLayout(raw) {
    const source = object(raw);
    const defaults = createLayout();
    const positions = Array.isArray(source.positions) ? source.positions : [];
    return {
      aspect: ASPECTS.includes(source.aspect) ? source.aspect : defaults.aspect,
      offsetX: number(source.offsetX, 0, -1, 1),
      offsetY: number(source.offsetY, 0, -1, 1),
      scale: number(source.scale, 1, 0.35, 2),
      cardScale: number(source.cardScale, 1, 0.6, 1.8),
      // Outside [0,1] is useful when dragging into letterbox regions or undoing
      // large offsets with a small spacing scale. Keep a bounded import range.
      positions: defaults.positions.map((fallback, index) => ({
        x: number(object(positions[index]).x, fallback.x, -8, 9),
        y: number(object(positions[index]).y, fallback.y, -8, 9),
      })),
    };
  }

  function dimensions(width, height) {
    return {
      width: number(width, BASE_WIDTH, 1, 32768),
      height: number(height, BASE_HEIGHT, 1, 32768),
    };
  }

  function computeViewport(width, height, aspect = '16:9') {
    const size = dimensions(width, height);
    if (aspect === 'stretch') return { x: 0, y: 0, ...size };
    const selected = ASPECTS.includes(aspect) ? aspect : '16:9';
    const [numerator, denominator] = selected.split(':').map(Number);
    const ratio = numerator / denominator;
    const viewWidth = Math.min(size.width, size.height * ratio);
    const viewHeight = Math.min(size.height, size.width / ratio);
    return {
      x: (size.width - viewWidth) / 2,
      y: (size.height - viewHeight) / 2,
      width: viewWidth,
      height: viewHeight,
    };
  }

  function geometry(raw, width, height) {
    const layout = normalizeLayout(raw);
    const size = dimensions(width, height);
    const viewport = computeViewport(size.width, size.height, layout.aspect);
    // UI scale is independent of grid spacing, so widening a layout needn't
    // make its controls larger. Clamp only to keep even tiny displays usable.
    const scale = Math.min(
      Math.min(viewport.width / BASE_WIDTH, viewport.height / BASE_HEIGHT) * layout.cardScale,
      size.width / CARD_WIDTH,
      size.height / CARD_HEIGHT,
    );
    return { layout, size, viewport, scale };
  }

  function computePositions(raw, width, height) {
    const { layout, size, viewport, scale } = geometry(raw, width, height);
    return layout.positions.map((position, index) => ({
      id: index + 1,
      x: clamp(viewport.x + ((position.x - 0.5) * layout.scale + 0.5 + layout.offsetX) * viewport.width, 0, size.width - CARD_WIDTH * scale),
      y: clamp(viewport.y + ((position.y - 0.5) * layout.scale + 0.5 + layout.offsetY) * viewport.height, 0, size.height - CARD_HEIGHT * scale),
      scale,
    }));
  }

  function movePlayer(raw, id, screenX, screenY, width, height) {
    const { layout, size, viewport, scale } = geometry(raw, width, height);
    if (!Number.isInteger(id) || id < 1 || id > PLAYER_COUNT) return layout;
    if (typeof screenX !== 'number' || !Number.isFinite(screenX) || typeof screenY !== 'number' || !Number.isFinite(screenY)) return layout;
    const x = clamp(screenX, 0, size.width - CARD_WIDTH * scale);
    const y = clamp(screenY, 0, size.height - CARD_HEIGHT * scale);
    layout.positions[id - 1] = {
      x: ((x - viewport.x) / viewport.width - 0.5 - layout.offsetX) / layout.scale + 0.5,
      y: ((y - viewport.y) / viewport.height - 0.5 - layout.offsetY) / layout.scale + 0.5,
    };
    return normalizeLayout(layout);
  }

  function profileKey(display) {
    const source = object(display);
    const bounds = object(source.bounds);
    const size = dimensions(bounds.width, bounds.height);
    const id = typeof source.id === 'string' || Number.isFinite(source.id) ? String(source.id).slice(0, 120) : 'primary';
    const scaleFactor = number(source.scaleFactor, 1, 0.25, 8);
    // Electron reports bounds in DIP. Combining DIP dimensions and scaleFactor
    // distinguishes physical resolution changes from Windows scaling changes.
    return `display:${id}|${size.width}x${size.height}|dpi:${scaleFactor}`;
  }

  return Object.freeze({
    PLAYER_COUNT, CARD_WIDTH, CARD_HEIGHT, ASPECTS, DEATH_CAUSES, ROLE_GROUPS, RESOLUTION_PRESETS,
    createSession, normalizeSession, setRound, setDeath, revivePlayer,
    createLayout, normalizeLayout, computeViewport, computePositions, movePlayer, profileKey,
  });
});
