const path = require('node:path');
const fs = require('node:fs/promises');
const { build } = require('../package.json');

(async () => {
  const { packager } = await import('@electron/packager');
  const output = await packager({
    dir: path.resolve(__dirname, '..'),
    name: 'GGD-help',
    executableName: 'GGD-help',
    platform: 'win32',
    arch: 'x64',
    out: path.resolve(__dirname, '../out'),
    overwrite: true,
    asar: true,
    prune: true,
    ignore: [/^\/(tests|artifacts|scripts|release|\.git)(\/|$)/, /\.cmd$/],
    appCopyright: 'Based on snapea/goose-duck-help',
    win32metadata: { ProductName: 'GGD-help', FileDescription: 'GGD-help meeting notes overlay' },
  });
  // Match the portable build's supported Chromium UI languages. Packager does
  // not implement electron-builder's electronLanguages option itself.
  const outputRoot = path.resolve(__dirname, '../out');
  const retainedLocales = new Set(build.electronLanguages.map(language => language + '.pak'));
  for (const directory of output) {
    const resolved = await fs.realpath(directory);
    if (!resolved.startsWith(outputRoot + path.sep)) throw new Error('Unexpected package output directory');
    const locales = path.join(resolved, 'locales');
    for (const entry of await fs.readdir(locales, { withFileTypes: true })) {
      if (entry.isFile() && entry.name.endsWith('.pak') && !retainedLocales.has(entry.name)) {
        await fs.unlink(path.join(locales, entry.name));
      }
    }
  }
  console.log(output.join('\n'));
})().catch(error => { console.error(error); process.exitCode = 1; });
