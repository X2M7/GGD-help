'use strict';

function clampLauncher(position, area, size) {
  const x = Number.isFinite(position?.x) ? Math.round(position.x) : area.x;
  const y = Number.isFinite(position?.y) ? Math.round(position.y) : area.y;
  return {
    x: Math.max(area.x, Math.min(x, area.x + Math.max(0, area.width - size.width))),
    y: Math.max(area.y, Math.min(y, area.y + Math.max(0, area.height - size.height))),
  };
}

function sanitizeSettings(input) {
  if (!input || typeof input !== 'object' || Array.isArray(input)) return {};
  const result = {};
  if (Number.isSafeInteger(input.displayId)) result.displayId = input.displayId;
  if (Number.isFinite(input.launcherPosition?.x) && Number.isFinite(input.launcherPosition?.y)) {
    result.launcherPosition = {
      x: Math.round(input.launcherPosition.x),
      y: Math.round(input.launcherPosition.y),
    };
  }
  return result;
}

function sanitizeInteractiveRegions(input) {
  if (!Array.isArray(input) || input.length > 256) return [];
  return input.filter(rect => rect && ['x', 'y', 'width', 'height'].every(key =>
    typeof rect[key] === 'number' && Number.isFinite(rect[key]) && Math.abs(rect[key]) <= 100000)
    && rect.width > 0 && rect.height > 0)
    .map(({ x, y, width, height }) => ({ x, y, width, height }));
}

// Electron's cursor position and content bounds both use DIP. Renderer regions
// use CSS pixels with the fixed zoom factor of 1, so no DPI multiplication belongs
// here, including on displays to the left or above the primary display.
function hitsInteractiveRegion(point, bounds, regions) {
  const x = point.x - bounds.x;
  const y = point.y - bounds.y;
  if (x < 0 || y < 0 || x >= bounds.width || y >= bounds.height) return false;
  return regions.some(rect => x >= rect.x && y >= rect.y
    && x < rect.x + rect.width && y < rect.y + rect.height);
}

function shouldIgnoreMouse({ visible, locked, captured, point, bounds, regions }) {
  if (!visible || locked) return true;
  return !captured && !hitsInteractiveRegion(point, bounds, regions);
}

module.exports = { clampLauncher, sanitizeSettings, sanitizeInteractiveRegions, hitsInteractiveRegion, shouldIgnoreMouse };
