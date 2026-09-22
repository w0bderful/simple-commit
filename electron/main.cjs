const {app,BrowserWindow,Tray,Menu,dialog,shell,session}=require('electron');
const {spawn}=require('node:child_process');
const path=require('node:path');
const fs=require('node:fs');
const {Backend}=require('./backend.cjs');

let window,tray,backend,origin,token,quitting=false,checkingQuit=false;
const smoke=process.argv.includes('--smoke-test');
const smokeData=process.argv.find(a=>a.startsWith('--smoke-data='))?.slice(13);
const startHidden=process.argv.includes('--tray');
const log=(message)=>{if(smoke&&smokeData){fs.mkdirSync(smokeData,{recursive:true});fs.appendFileSync(path.join(smokeData,'electron-test.log'),message+'\n');}};
function openWindow(){if(!window)return;if(window.isMinimized())window.restore();window.show();window.focus();}
function external(url){try{const u=new URL(url);if(u.protocol==='https:'&&['github.com','gitgud.io','gitlab.com','codeberg.org'].includes(u.hostname))shell.openExternal(url);}catch{}}
async function api(route,data){const response=await fetch(origin+'/api/'+route,{method:data===undefined?'GET':'POST',headers:{'X-SimpleCommit':token,'Content-Type':'application/json'},body:data===undefined?undefined:JSON.stringify(data)});if(!response.ok)throw Error('로컬 서버 응답 오류');return response.json();}
async function quit(){if(checkingQuit||quitting)return;checkingQuit=true;try{const state=await api('state');if(state.busy){await dialog.showMessageBox(window,{type:'info',message:'진행 중인 작업이 끝난 뒤 종료해 주세요.'});return;}}catch{}finally{checkingQuit=false;}quitting=true;app.quit();}
if(!app.requestSingleInstanceLock()){app.quit();}else{
 app.on('second-instance',()=>{log('SECOND_INSTANCE');openWindow();});
 app.on('activate',openWindow);
 app.on('window-all-closed',()=>{});
 app.on('before-quit',event=>{if(!quitting){event.preventDefault();quit();return;}if(backend)backend.close().catch(error=>log('CLOSE_ERROR '+error.message));});
 app.whenReady().then(async()=>{
    app.setAppUserModelId('io.w0bderful.simplecommit');
    const icon=app.isPackaged?path.join(process.resourcesPath,'app.ico'):path.join(__dirname,'..','app.ico');
    backend=new Backend({
      dir:smoke&&smokeData?smokeData:path.join(process.env.LOCALAPPDATA||app.getPath('appData'),'SimpleCommitWeb'),
      documents:app.getPath('documents'),downloads:app.getPath('downloads'),autoCheck:!smoke,
      pick:async zip=>{const result=await dialog.showOpenDialog(window,{title:zip?'기존 ZIP 연결':'ZIP 다운로드 폴더',properties:zip?['openFile']:['openDirectory','createDirectory'],...(zip?{filters:[{name:'ZIP 파일',extensions:['zip']}]}:{})});return result.canceled?'':result.filePaths[0]||'';},
      startup:enabled=>{if(!smoke)app.setLoginItemSettings({name:'SimpleCommitWeb',openAtLogin:enabled,path:process.env.PORTABLE_EXECUTABLE_FILE||process.execPath,args:app.isPackaged?['--tray']:[app.getAppPath(),'--tray']});}
    });
    await backend.start();origin=backend.origin;token=backend.token;
    if(backend.settings.StartWithWindows)backend.startup(true);
    session.defaultSession.setPermissionRequestHandler((contents,permission,callback)=>callback(false));
    session.defaultSession.setPermissionCheckHandler(()=>false);
    window=new BrowserWindow({width:1360,height:900,minWidth:720,minHeight:560,show:false,backgroundColor:'#101513',title:'SimpleCommit',icon,autoHideMenuBar:true,webPreferences:{nodeIntegration:false,contextIsolation:true,sandbox:true,webSecurity:true}});
    window.removeMenu();
    window.webContents.setWindowOpenHandler(({url})=>{external(url);return {action:'deny'};});
    window.webContents.on('will-navigate',(event,url)=>{if(new URL(url).origin!==origin){event.preventDefault();external(url);}});
    window.webContents.on('will-attach-webview',event=>event.preventDefault());
    window.on('close',event=>{if(!quitting){event.preventDefault();window.hide();log('CLOSE_TO_TRAY');}});
    window.on('show',()=>log('WINDOW_SHOWN'));
    tray=new Tray(icon);tray.setToolTip('SimpleCommit');tray.setContextMenu(Menu.buildFromTemplate([{label:'열기',click:openWindow},{type:'separator'},{label:'종료',click:quit}]));tray.on('double-click',openWindow);
    await window.loadURL(origin);
    if(!startHidden)openWindow();
    if(smoke){
      await window.webContents.executeJavaScript("new Promise((resolve,reject)=>{const start=Date.now();const t=setInterval(()=>{if(document.querySelector('#connection').textContent==='자동 확인 실행 중'){clearInterval(t);resolve(true)}else if(Date.now()-start>10000){clearInterval(t);reject(Error('UI connection failed'))}},100)})");
      const result=await window.webContents.executeJavaScript("({title:document.title,rows:document.querySelectorAll('#rows tr').length,node:typeof require,theme:document.documentElement.dataset.theme})");
      log(JSON.stringify({ready:true,...result,version:process.versions.electron,origin}));
      await window.webContents.capturePage().then(image=>fs.writeFileSync(path.join(smokeData,'electron-window.png'),image.toPNG()));
      window.close();if(window.isVisible())throw Error('Close-to-tray failed');openWindow();if(!window.isVisible())throw Error('Restore failed');
      log('TRAY_RESTORE_PASS');
      const second=spawn(process.execPath,app.isPackaged?['--tray']:[app.getAppPath(),'--tray'],{windowsHide:true,stdio:'ignore'});
      second.on('error',e=>log('SECOND_ERROR '+e.message));
      setTimeout(()=>{quitting=true;app.quit();},15000);
    }
 }).catch(error=>{log('ERROR '+error.stack);dialog.showErrorBox('SimpleCommit 시작 실패',error.message);quitting=true;app.quit();});
}
