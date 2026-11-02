'use strict';

const assert = require('node:assert/strict');
const path = require('node:path');
const { spawn } = require('node:child_process');
const { createInterface } = require('node:readline');

async function createNativeMouse() {
  assert.equal(process.platform, 'win32', 'native mouse verification requires Windows');
  const child = spawn('powershell.exe', ['-NoLogo', '-NoProfile', '-NonInteractive', '-ExecutionPolicy', 'Bypass', '-File', path.join(__dirname, 'native-mouse.ps1')], {
    windowsHide: true, stdio: ['pipe', 'pipe', 'pipe'],
  });
  let errorOutput = '';
  child.stderr.on('data', data => { errorOutput += data.toString(); });
  const lines = createInterface({ input: child.stdout });
  const waiting = [];
  let failure;
  let readyResolve;
  let readyReject;
  const ready = new Promise((resolve, reject) => { readyResolve = resolve; readyReject = reject; });
  const fail = error => {
    failure = error;
    readyReject(error);
    while (waiting.length) waiting.shift().reject(error);
  };
  child.once('error', fail);
  child.once('exit', code => {
    if (code !== 0 || waiting.length) fail(new Error(`Native mouse helper exited (${code}): ${errorOutput}`));
  });
  lines.on('line', line => {
    let result;
    try { result = JSON.parse(line); }
    catch { fail(new Error(`Unexpected native mouse output: ${line}`)); return; }
    if (result.ready) { readyResolve(); return; }
    const pending = waiting.shift();
    if (!pending) return;
    if (result.error) pending.reject(new Error(result.error));
    else pending.resolve();
  });
  await ready;
  const send = async (action, point) => {
    if (failure) throw failure;
    await new Promise((resolve, reject) => {
      waiting.push({ resolve, reject });
      child.stdin.write(JSON.stringify({ ...point, action, x: Math.round(point.x), y: Math.round(point.y) }) + '\n');
    });
  };
  return {
    click: point => send('click', point),
    move: point => send('move', point),
    capture: rect => send('capture', rect),
    async close() {
      if (child.exitCode !== null) return;
      child.stdin.end('{"action":"close"}\n');
      await new Promise(resolve => child.once('exit', resolve));
      lines.close();
    },
  };
}

module.exports = { createNativeMouse };
