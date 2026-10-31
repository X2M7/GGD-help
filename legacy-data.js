'use strict';

const fs = require('node:fs');
const path = require('node:path');

// Copy only saved records and window preferences. Cache, locks, and unrelated
// browser data stay in the previous application's directory.
function migrateLegacyData(destination, legacy) {
  const entries = ['Local Storage', 'overlay-window.json'];
  if (path.resolve(destination) === path.resolve(legacy)) return 'unchanged';
  if (entries.some(name => fs.existsSync(path.join(destination, name)))) return 'existing';
  const available = entries.filter(name => fs.existsSync(path.join(legacy, name)));
  if (available.length === 0) return 'absent';

  fs.mkdirSync(destination, { recursive: true });
  const staging = fs.mkdtempSync(path.join(destination, '.legacy-migration-'));
  // Fully copy before installing anything. On failure the old data stays intact
  // and the staging copy is retained for recovery rather than deleting evidence.
  for (const name of available) {
    fs.cpSync(path.join(legacy, name), path.join(staging, name), {
      recursive: true,
      force: false,
      errorOnExist: true,
      dereference: false,
    });
  }
  // Install records before optional window geometry. Never replace new records.
  for (const name of available) {
    const target = path.join(destination, name);
    if (fs.existsSync(target)) throw new Error('New application data already exists.');
    fs.renameSync(path.join(staging, name), target);
  }
  fs.rmdirSync(staging);
  return 'migrated';
}

module.exports = { migrateLegacyData };
