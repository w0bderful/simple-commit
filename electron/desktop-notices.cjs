const {BrowserWindow,ipcMain,screen}=require('electron');
const path=require('node:path');
const {randomUUID}=require('node:crypto');

class DesktopNotices {
  constructor(backend){
    this.backend=backend;this.queue=[];this.seen=new Set();this.window=null;this.ready=false;this.disposed=false;
    this.channel='simplecommit-notice-dismiss';
    this.dismissHandler=(event,id)=>{if(event.sender===this.window?.webContents&&typeof id==='string')this.dismiss(id,true)};
    ipcMain.on(this.channel,this.dismissHandler);
    this.reposition=()=>this.render();
    screen.on('display-metrics-changed',this.reposition);screen.on('display-removed',this.reposition);screen.on('display-added',this.reposition);
    this.poll=setInterval(()=>this.sync(),300);
  }
  sync(){
    if(this.disposed)return;
    const notices=this.backend.notifications;
    for(const n of notices)if(!this.seen.has(n.Id)){this.seen.add(n.Id);this.queue.push({...n,options:{...this.backend.settings}})}
    for(const n of this.queue)if(!n.preview&&!notices.some(current=>current.Id===n.Id))this.dismiss(n.Id,false);
    for(const id of this.seen)if(!notices.some(n=>n.Id===id))this.seen.delete(id);
    this.render();
  }
  preview(options){
    for(const n of [...this.queue])if(n.preview)this.remove(n);
    this.queue.unshift({Id:'preview-'+randomUUID(),Text:'알림 테스트입니다. 새 커밋 알림이 바탕화면 오른쪽 아래에 표시됩니다.',preview:true,options});
    this.render();
  }
  create(){
    if(this.window||this.disposed)return;
    this.window=new BrowserWindow({width:392,height:164,show:false,frame:false,transparent:true,hasShadow:false,skipTaskbar:true,resizable:false,movable:false,minimizable:false,maximizable:false,focusable:false,alwaysOnTop:true,title:'SimpleCommit 알림',webPreferences:{preload:path.join(__dirname,'notice-preload.cjs'),nodeIntegration:false,contextIsolation:true,sandbox:true,backgroundThrottling:false}});
    this.window.setAlwaysOnTop(true,'floating');
    this.window.webContents.setWindowOpenHandler(()=>({action:'deny'}));
    this.window.webContents.on('will-navigate',event=>event.preventDefault());
    this.window.webContents.on('will-attach-webview',event=>event.preventDefault());
    this.window.on('closed',()=>{this.window=null;this.ready=false;this.lastPayload=null});
    this.window.webContents.once('did-finish-load',()=>{this.ready=true;this.render()});
    this.window.loadFile(path.join(__dirname,'www','notice.html')).catch(()=>{if(!this.disposed)this.window?.destroy()});
  }
  visible(){
    const area=screen.getPrimaryDisplay().workArea;
    return this.queue.slice(0,Math.max(1,Math.min(3,Math.floor((area.height-32)/148))));
  }
  render(){
    if(this.disposed)return;
    if(!this.queue.length){this.window?.hide();return}
    this.create();if(!this.ready)return;
    const visible=this.visible(),area=screen.getPrimaryDisplay().workArea;
    const width=Math.min(392,area.width-24),height=Math.min(visible.length*148+12,area.height-24);
    const bounds={x:area.x+area.width-width-12,y:area.y+area.height-height-12,width,height};
    if(JSON.stringify(bounds)!==JSON.stringify(this.window.getBounds()))this.window.setBounds(bounds);
    const payload={dark:this.backend.settings.MaterialDark,notices:visible.map(n=>({Id:n.Id,Text:n.Text,preview:!!n.preview,closing:!!n.closing})),remaining:Math.max(0,this.queue.length-visible.length)};
    const serialized=JSON.stringify(payload);
    if(serialized!==this.lastPayload){this.lastPayload=serialized;this.window.webContents.send('simplecommit-notices',payload)}
    if(!this.window.isVisible())this.window.showInactive();
    for(const n of visible)if(!n.timer&&!n.closing&&!n.options.KeepNotificationUntilDismissed)n.timer=setTimeout(()=>this.dismiss(n.Id,false),Math.max(1,Math.min(120,Number(n.options.NotificationSeconds)||7))*1000);
  }
  dismiss(id,ack){
    const n=this.queue.find(n=>n.Id===id);if(!n||n.closing)return;
    n.closing=true;clearTimeout(n.timer);this.render();
    n.closeTimer=setTimeout(()=>{
      this.remove(n);
      if(ack&&!n.preview)this.backend.api('ack',{id}).catch(()=>{});
      this.render();
    },180);
  }
  remove(n){clearTimeout(n.timer);clearTimeout(n.closeTimer);this.queue=this.queue.filter(item=>item!==n)}
  close(){this.disposed=true;clearInterval(this.poll);for(const n of [...this.queue])this.remove(n);ipcMain.removeListener(this.channel,this.dismissHandler);for(const event of ['display-metrics-changed','display-removed','display-added'])screen.removeListener(event,this.reposition);this.window?.destroy();}
}
module.exports={DesktopNotices};
