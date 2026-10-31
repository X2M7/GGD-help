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

module.exports = { clampLauncher, sanitizeSettings };
