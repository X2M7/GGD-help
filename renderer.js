'use strict';

(() => {
  const C = window.OverlayCore;
  const api = window.overlayAPI;
  const $ = id => document.getElementById(id);
  const STORAGE_KEY = 'goose-duck-overlay:v2';
  let recoveryMessage = '';
  let stored = {};
  try {
    const value = localStorage.getItem(STORAGE_KEY);
    if (value) {
      try { stored = JSON.parse(value) || {}; }
      catch {
        localStorage.setItem(STORAGE_KEY + ':recovery', value);
        recoveryMessage = '记录文件损坏，已保留原始备份并使用默认设置。';
      }
    }
  } catch { recoveryMessage = '无法读取本地记录，本次操作可能无法保存。'; }
  let session = C.normalizeSession(stored.session);
  const profiles = Object.create(null);
  if (stored.profiles && typeof stored.profiles === 'object') {
    Object.entries(stored.profiles).slice(-100).forEach(([key, value]) => {
      if (key.length < 200) profiles[key] = C.normalizeLayout(value);
    });
  }
  const numeric = (value, fallback, min, max) =>
    typeof value === 'number' && Number.isFinite(value) ? Math.min(max, Math.max(min, value)) : fallback;
  const preferences = {
    playerCount: Math.round(numeric(stored.preferences?.playerCount, 16, 1, 16)),
    opacity: numeric(stored.preferences?.opacity, 68, 15, 95),
  };
  let nativeState = { visible: true, locked: false, displays: [] };
  let layout = C.createLayout();
  let activeProfile = '';
  let calibrating = false;
  let dragging = null;
  let interactive = null;
  let lastPointer = null;
  let toastTimer;
  let confirmAction = null;
  const cards = new Map();
  const draftDeathRounds = new Map();

  function toast(message) {
    clearTimeout(toastTimer);
    $('toast').textContent = message;
    $('toast').hidden = false;
    toastTimer = setTimeout(() => { $('toast').hidden = true; }, 4200);
  }
  function persist() {
    if (activeProfile) profiles[activeProfile] = C.normalizeLayout(layout);
    try {
      localStorage.setItem(STORAGE_KEY, JSON.stringify({ version: 2, session, profiles, preferences }));
      $('save-status').textContent = '已自动保存';
    } catch {
      $('save-status').textContent = '保存失败，请勿关闭助手';
      toast('本地保存失败，请检查磁盘空间。当前记录仍在窗口中。');
    }
  }
  function nativeAction(action) {
    Promise.resolve().then(action).catch(error => toast('操作失败：' + error.message));
  }
  function option(value, label) {
    const el = document.createElement('option');
    el.value = value;
    el.textContent = label;
    return el;
  }
  function createCard(player) {
    const card = document.createElement('section');
    card.className = 'player-card';
    card.dataset.playerId = player.id;
    card.dataset.interactive = '';
    card.setAttribute('aria-label', player.id + ' 号玩家记录');
    // Only fixed application markup is used here; saved text is assigned through value/textContent.
    card.innerHTML = '<div class="player-top"><span class="player-number"></span><input class="player-name" placeholder="玩家昵称" maxlength="32"><button class="drag-handle" title="拖动到对应人物旁">⠿</button></div>' +
      '<div class="player-middle"><select class="role-select"></select><input class="player-notes" placeholder="发言 / 身份线索" maxlength="240"></div>' +
      '<div class="death-actions"></div><div class="death-record"><span class="death-status" aria-live="polite"></span><label>第<input class="death-round" type="number" min="1" max="99">轮</label><button class="revive-button" title="清除死亡标记">存活</button></div>';
    const name = card.querySelector('.player-name');
    const role = card.querySelector('.role-select');
    const notes = card.querySelector('.player-notes');
    const round = card.querySelector('.death-round');
    card.querySelector('.player-number').textContent = String(player.id).padStart(2, '0');
    name.setAttribute('aria-label', player.id + ' 号玩家昵称');
    role.setAttribute('aria-label', player.id + ' 号玩家身份');
    notes.setAttribute('aria-label', player.id + ' 号玩家备注');
    round.setAttribute('aria-label', player.id + ' 号死亡轮次');
    role.append(option('', '身份待确认'));
    C.ROLE_GROUPS.forEach(group => {
      const optgroup = document.createElement('optgroup');
      optgroup.label = group.label;
      group.roles.forEach(value => optgroup.append(option(value, value)));
      role.append(optgroup);
    });
    if (player.role && !Array.from(role.options).some(item => item.value === player.role)) {
      role.append(option(player.role, player.role));
    }
    name.value = player.name;
    role.value = player.role;
    notes.value = player.notes;
    for (const [input, field] of [[name, 'name'], [role, 'role'], [notes, 'notes']]) {
      input.addEventListener(input === role ? 'change' : 'input', () => {
        if (nativeState.locked) return;
        session.players.find(p => p.id === player.id)[field] = input.value;
        persist();
      });
    }
    const labels = ['刀', '票', '刺', '炸', '吞', '连', '？'];
    C.DEATH_CAUSES.forEach((cause, i) => {
      const button = document.createElement('button');
      button.dataset.cause = cause;
      button.textContent = labels[i];
      button.title = '标记死亡：' + cause;
      button.setAttribute('aria-label', player.id + ' 号：' + cause);
      button.addEventListener('click', () => {
        if (nativeState.locked) return;
        const current = session.players.find(p => p.id === player.id);
        session = C.setDeath(session, player.id, current.death?.round ?? draftDeathRounds.get(player.id) ?? session.round, cause);
        renderStatus();
        persist();
      });
      card.querySelector('.death-actions').append(button);
    });
    round.addEventListener('change', () => {
      if (nativeState.locked) return;
      const value = Math.max(1, Math.min(99, Math.round(Number(round.value) || session.round)));
      const current = session.players.find(p => p.id === player.id);
      if (current.death) session = C.setDeath(session, player.id, value, current.death.cause);
      else draftDeathRounds.set(player.id, value);
      renderStatus();
      persist();
    });
    card.querySelector('.revive-button').addEventListener('click', () => {
      if (nativeState.locked) return;
      session = C.revivePlayer(session, player.id);
      draftDeathRounds.delete(player.id);
      renderStatus();
      persist();
    });
    const handle = card.querySelector('.drag-handle');
    handle.setAttribute('aria-label', '拖动校准 ' + player.id + ' 号位置');
    handle.addEventListener('pointerdown', event => {
      if (!calibrating || nativeState.locked || event.button !== 0) return;
      event.preventDefault();
      const bounds = card.getBoundingClientRect();
      dragging = { id: player.id, dx: event.clientX - bounds.left, dy: event.clientY - bounds.top, handle, pointerId: event.pointerId };
      handle.setPointerCapture(event.pointerId);
      card.classList.add('dragging');
      updateInteractive(true);
    });
    handle.addEventListener('pointermove', event => {
      if (!dragging || dragging.id !== player.id) return;
      layout = C.movePlayer(layout, player.id, event.clientX - dragging.dx, event.clientY - dragging.dy, innerWidth, innerHeight);
      positionCards();
    });
    handle.addEventListener('pointerup', finishDrag);
    handle.addEventListener('pointercancel', finishDrag);
    handle.addEventListener('lostpointercapture', finishDrag);
    cards.set(player.id, card);
    return card;
  }
  function finishDrag() {
    if (!dragging) return;
    const previous = dragging;
    dragging = null;
    cards.get(previous.id)?.classList.remove('dragging');
    if (previous.handle.hasPointerCapture(previous.pointerId)) previous.handle.releasePointerCapture(previous.pointerId);
    persist();
    refreshInteractive();
  }
  function buildCards() {
    cards.clear();
    $('players').replaceChildren(...session.players.map(createCard));
    renderStatus();
    positionCards();
  }
  function renderStatus() {
    $('round-input').value = session.round;
    $('round-prev').disabled = session.round <= 1;
    $('round-next').disabled = session.round >= 99;
    const visible = session.players.slice(0, preferences.playerCount);
    $('alive-count').textContent = visible.filter(p => !p.death).length + ' / ' + visible.length + ' 人存活';
    for (const player of session.players) {
      const card = cards.get(player.id);
      if (!card) continue;
      card.hidden = player.id > preferences.playerCount;
      card.classList.toggle('dead', Boolean(player.death));
      card.querySelector('.death-status').textContent = player.death ? '第 ' + player.death.round + ' 轮 · ' + player.death.cause : '存活 · 点击方式标记死亡';
      card.querySelector('.death-round').value = player.death?.round ?? draftDeathRounds.get(player.id) ?? session.round;
      card.querySelector('.revive-button').disabled = !player.death;
      card.querySelector('.revive-button').textContent = player.death ? '复活' : '存活';
      card.querySelectorAll('[data-cause]').forEach(button => button.setAttribute('aria-pressed', String(button.dataset.cause === player.death?.cause)));
    }
  }
  function positionCards() {
    for (const point of C.computePositions(layout, innerWidth, innerHeight)) {
      const card = cards.get(point.id);
      if (!card) continue;
      card.style.left = point.x + 'px';
      card.style.top = point.y + 'px';
      card.style.transform = 'scale(' + point.scale + ')';
    }
  }
  function renderSettings() {
    $('aspect-select').value = layout.aspect;
    $('player-count').value = preferences.playerCount;
    const values = {
      'offset-x': layout.offsetX * 100, 'offset-y': layout.offsetY * 100,
      'layout-scale': layout.scale * 100, 'card-scale': layout.cardScale * 100,
      'panel-opacity': preferences.opacity,
    };
    Object.entries(values).forEach(([id, value]) => {
      $(id).value = value;
      $(id + '-value').textContent = Math.round(value * 10) / 10 + '%';
    });
    document.documentElement.style.setProperty('--panel-opacity', preferences.opacity / 100);
  }
  function setCalibration(value) {
    finishDrag();
    calibrating = value && !nativeState.locked;
    $('calibration-toggle').checked = calibrating;
    document.body.classList.toggle('calibrating', calibrating);
    $('calibration-hint').hidden = !calibrating;
  }
  function showSettings(value) {
    $('settings-panel').hidden = !value;
    $('settings-toggle').setAttribute('aria-expanded', String(value));
    refreshInteractive();
  }
  function applyNativeState(state) {
    nativeState = state;
    interactive = null; // Native mode transitions reset mouse-event routing.
    const display = state.displays.find(item => item.id === state.displayId);
    if (display) {
      const key = C.profileKey(display);
      if (key !== activeProfile) {
        finishDrag();
        if (activeProfile) profiles[activeProfile] = C.normalizeLayout(layout);
        activeProfile = key;
        layout = profiles[key] ? C.normalizeLayout(profiles[key]) : C.createLayout();
        $('resolution-preset').value = '';
        renderSettings();
        positionCards();
        persist();
      }
      const width = Math.round(display.bounds.width * display.scaleFactor);
      const height = Math.round(display.bounds.height * display.scaleFactor);
      $('display-info').textContent = '约 ' + width + ' × ' + height + ' 像素 · Windows 缩放 ' + Math.round(display.scaleFactor * 100) + '% · 已使用此屏幕的位置配置';
    }
    $('display-select').replaceChildren(...state.displays.map(item => option(String(item.id), item.label + ' · 约 ' + Math.round(item.bounds.width * item.scaleFactor) + '×' + Math.round(item.bounds.height * item.scaleFactor))));
    $('display-select').value = String(state.displayId);
    document.body.classList.toggle('locked', state.locked);
    $('mode-label').textContent = state.locked ? '穿透模式' : '记录模式';
    $('locked-hint').hidden = !state.locked;
    ['toolbar', 'players', 'settings-panel', 'confirm-panel'].forEach(id => { $(id).inert = state.locked; });
    if (state.locked || !state.visible) {
      setCalibration(false);
      showSettings(false);
      cancelConfirm();
    }
    const warnings = [...(state.shortcutErrors || []), ...(state.persistenceError ? [state.persistenceError] : [])];
    $('shortcut-warning').hidden = warnings.length === 0;
    $('shortcut-warning').textContent = warnings.join('；');
    refreshInteractive();
  }
  function updateInteractive(value) {
    const next = Boolean(value && !nativeState.locked && nativeState.visible);
    if (interactive === next) return;
    interactive = next;
    api.setInteractive(next);
  }
  function refreshInteractive() {
    const target = lastPointer && document.elementFromPoint(lastPointer.x, lastPointer.y);
    updateInteractive(dragging || target?.closest('[data-interactive]'));
  }
  function changeRound(value) {
    session = C.setRound(session, value);
    draftDeathRounds.clear();
    renderStatus();
    persist();
  }
  function askConfirm(title, description, label, action) {
    confirmAction = action;
    $('confirm-title').textContent = title;
    $('confirm-description').textContent = description;
    $('confirm-new-game').textContent = label;
    $('confirm-panel').hidden = false;
    $('toolbar').inert = true;
    $('players').inert = true;
    $('settings-panel').inert = true;
    $('cancel-confirm').focus();
  }
  function cancelConfirm() {
    confirmAction = null;
    $('confirm-panel').hidden = true;
    ['toolbar', 'players', 'settings-panel'].forEach(id => { $(id).inert = nativeState.locked; });
  }

  C.RESOLUTION_PRESETS.forEach((preset, index) => $('resolution-preset').append(option(String(index), preset.label + ' · ' + preset.aspect)));
  for (let i = 1; i <= 16; i++) $('player-count').append(option(String(i), i + ' 人'));
  $('round-prev').addEventListener('click', () => changeRound(session.round - 1));
  $('round-next').addEventListener('click', () => changeRound(session.round + 1));
  $('round-input').addEventListener('change', event => changeRound(Number(event.target.value) || session.round));
  $('settings-toggle').addEventListener('click', () => showSettings($('settings-panel').hidden));
  $('settings-close').addEventListener('click', () => showSettings(false));
  $('hide-button').addEventListener('click', () => nativeAction(() => api.toggleVisible()));
  $('lock-button').addEventListener('click', () => nativeAction(() => api.setLocked(true)));
  $('quit-button').addEventListener('click', () => { persist(); nativeAction(() => api.quit()); });
  $('display-select').addEventListener('change', event => { persist(); nativeAction(() => api.selectDisplay(Number(event.target.value))); });
  $('aspect-select').addEventListener('change', event => {
    layout.aspect = event.target.value;
    $('resolution-preset').value = '';
    positionCards();
    persist();
  });
  $('resolution-preset').addEventListener('change', event => {
    if (event.target.value === '') return;
    const preset = C.RESOLUTION_PRESETS[Number(event.target.value)];
    layout.aspect = preset.aspect;
    renderSettings();
    positionCards();
    persist();
    toast('已应用 ' + preset.label + ' 的画面比例，保留手动校准。');
  });
  $('player-count').addEventListener('change', event => {
    preferences.playerCount = Number(event.target.value);
    renderStatus();
    persist();
  });
  $('calibration-toggle').addEventListener('change', event => setCalibration(event.target.checked));
  for (const [id, key] of [['offset-x', 'offsetX'], ['offset-y', 'offsetY'], ['layout-scale', 'scale'], ['card-scale', 'cardScale']]) {
    $(id).addEventListener('input', event => {
      layout[key] = Number(event.target.value) / 100;
      renderSettings();
      positionCards();
      persist();
    });
  }
  $('panel-opacity').addEventListener('input', event => {
    preferences.opacity = Number(event.target.value);
    renderSettings();
    persist();
  });
  $('reset-layout').addEventListener('click', () => askConfirm('重置当前屏幕的位置？', '重置此屏幕的拖动校准、位置偏移和记录卡大小。玩家记录与其他屏幕的位置将保留。', '重置位置', () => {
    const aspect = layout.aspect;
    layout = C.createLayout();
    layout.aspect = aspect;
    renderSettings();
    positionCards();
    persist();
    toast('当前屏幕的位置已重置。');
  }));
  $('new-game').addEventListener('click', () => askConfirm('开始新对局？', '清空所有玩家昵称、身份、备注和死亡记录，轮次回到第 1 轮。屏幕位置校准将保留。', '开始新对局', () => {
    session = C.createSession();
    draftDeathRounds.clear();
    buildCards();
    persist();
    toast('新对局已开始。');
  }));
  $('cancel-confirm').addEventListener('click', cancelConfirm);
  $('confirm-new-game').addEventListener('click', () => {
    const action = confirmAction;
    cancelConfirm();
    action?.();
  });
  document.addEventListener('mousemove', event => {
    lastPointer = { x: event.clientX, y: event.clientY };
    refreshInteractive();
  });
  document.addEventListener('mouseleave', () => { lastPointer = null; if (!dragging) updateInteractive(false); });
  document.addEventListener('keydown', event => {
    if (event.key === 'Escape') {
      cancelConfirm();
      showSettings(false);
      setCalibration(false);
    }
  });
  window.addEventListener('resize', positionCards);
  window.addEventListener('beforeunload', persist);
  buildCards();
  renderSettings();
  api.onState(applyNativeState);
  api.getState().then(state => {
    applyNativeState(state);
    if (recoveryMessage) toast(recoveryMessage);
  }).catch(error => toast('无法连接悬浮窗口：' + error.message));
})();
