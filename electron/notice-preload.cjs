const {contextBridge,ipcRenderer}=require('electron');
contextBridge.exposeInMainWorld('desktopNotices',{
  onUpdate(callback){ipcRenderer.on('simplecommit-notices',(_event,value)=>callback(value))},
  dismiss(id){if(typeof id==='string')ipcRenderer.send('simplecommit-notice-dismiss',id)}
});
