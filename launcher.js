const toggle = document.getElementById('toggle');
const lock = document.getElementById('lock');
const status = document.getElementById('status');
let state = { visible: true, locked: false };

function render(next) {
  state = next;
  toggle.setAttribute('aria-pressed', String(state.visible));
  document.getElementById('toggle-label').textContent = state.visible ? '收起蒙版' : '显示蒙版';
  lock.setAttribute('aria-pressed', String(state.locked));
  lock.textContent = state.locked ? '编辑' : '穿透';
  lock.title = state.locked ? '当前鼠标穿透 · 点击恢复编辑' : '点击使鼠标穿透到游戏';
  const error = state.shortcutErrors?.[0] || state.persistenceError;
  status.textContent = error || `${state.visible ? (state.locked ? '穿透中' : '编辑中') : '已收起'} · Ctrl+Shift+G 开关`;
  status.title = error || 'GGD-help V0.1.1 · 拖动左侧六点可移动开关';
}

function report(error) {
  status.textContent = '操作失败，请从托盘恢复';
  status.title = error.message;
}

toggle.addEventListener('click', () => window.overlayAPI.toggleVisible().then(render).catch(report));
lock.addEventListener('click', () => window.overlayAPI.setLocked(!state.locked).then(render).catch(report));
document.getElementById('menu').addEventListener('click', () => window.overlayAPI.showMenu().catch(report));
window.overlayAPI.onState(render);
window.overlayAPI.getState().then(render).catch(report);
