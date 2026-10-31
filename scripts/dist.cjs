'use strict';

// Release uploads are performed separately and only when explicitly requested.
// Never discover or use a developer's local signing identity automatically.
process.env.CSC_IDENTITY_AUTO_DISCOVERY = 'false';
const { build, Platform, Arch } = require('electron-builder');

build({
  targets: Platform.WINDOWS.createTarget(['portable'], Arch.x64),
  publish: 'never',
}).then(files => {
  console.log(files.join('\n'));
}).catch(error => {
  console.error(error);
  process.exitCode = 1;
});
