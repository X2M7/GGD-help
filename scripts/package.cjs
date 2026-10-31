const path = require('node:path');

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
  console.log(output.join('\n'));
})().catch(error => { console.error(error); process.exitCode = 1; });
