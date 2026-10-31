const { contextBridge, ipcRenderer } = require('electron');

contextBridge.exposeInMainWorld('overlayAPI', {
  getState: () => ipcRenderer.invoke('overlay:get-state'),
  toggleVisible: () => ipcRenderer.invoke('overlay:toggle-visible'),
  setLocked: (value) => ipcRenderer.invoke('overlay:set-locked', value),
  selectDisplay: (id) => ipcRenderer.invoke('overlay:select-display', id),
  setInteractive: (value) => ipcRenderer.send('overlay:interactive', value),
  quit: () => ipcRenderer.invoke('overlay:quit'),
  showMenu: () => ipcRenderer.invoke('launcher:menu'),
  onState: (callback) => {
    if (typeof callback !== 'function') throw new TypeError('callback must be a function');
    const listener = (_event, state) => callback(state);
    ipcRenderer.on('overlay:state', listener);
    return () => ipcRenderer.removeListener('overlay:state', listener);
  },
});
