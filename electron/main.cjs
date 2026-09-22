const {app,BrowserWindow,Tray,Menu,dialog,shell,session,screen}=require('electron');
const {spawn}=require('node:child_process');
const path=require('node:path');
const fs=require('node:fs');
const {Backend}=require('./backend.cjs');
const {DesktopNotices}=require('./desktop-notices.cjs');

let window,tray,backend,desktopNotices,origin,token,quitting=false,checkingQuit=false;
const smoke=process.argv.includes('--smoke-test');
const smokeData=process.argv.find(a=>a.startsWith('--smoke-data='))?.slice(13);
const startHidden=process.argv.includes('--tray');
if(smoke&&smokeData)app.setPath('userData',path.join(smokeData,'electron-profile'));
const log=(message)=>{if(smoke&&smokeData){fs.mkdirSync(smokeData,{recursive:true});fs.appendFileSync(path.join(smokeData,'electron-test.log'),message+'\n');}};
function openWindow(){if(!window)return;if(window.isMinimized())window.restore();window.show();window.focus();}
function external(url){try{const u=new URL(url);if(u.protocol==='https:'&&['github.com','gitgud.io','gitlab.com','codeberg.org'].includes(u.hostname))shell.openExternal(url);}catch{}}
async function api(route,data){const response=await fetch(origin+'/api/'+route,{method:data===undefined?'GET':'POST',headers:{'X-SimpleCommit':token,'Content-Type':'application/json'},body:data===undefined?undefined:JSON.stringify(data)});if(!response.ok)throw Error('로컬 서버 응답 오류');return response.json();}
async function quit(){if(checkingQuit||quitting)return;checkingQuit=true;try{const state=await api('state');if(state.busy){await dialog.showMessageBox(window,{type:'info',message:'진행 중인 작업이 끝난 뒤 종료해 주세요.'});return;}}catch{}finally{checkingQuit=false;}quitting=true;app.quit();}
if(!app.requestSingleInstanceLock()){app.quit();}else{
 app.on('second-instance',()=>{log('SECOND_INSTANCE');openWindow();});
 app.on('activate',openWindow);
 app.on('window-all-closed',()=>{});
 app.on('before-quit',event=>{if(!quitting){event.preventDefault();quit();return;}desktopNotices?.close();if(backend)backend.close().catch(error=>log('CLOSE_ERROR '+error.message));});
 app.whenReady().then(async()=>{
    app.setAppUserModelId('io.w0bderful.simplecommit');
    const icon=app.isPackaged?path.join(process.resourcesPath,'app.ico'):path.join(__dirname,'..','app.ico');
    backend=new Backend({
      dir:smoke&&smokeData?smokeData:path.join(process.env.LOCALAPPDATA||app.getPath('appData'),'SimpleCommitWeb'),
      documents:app.getPath('documents'),downloads:app.getPath('downloads'),autoCheck:!smoke,
      pick:async zip=>{const result=await dialog.showOpenDialog(window,{title:zip?'기존 ZIP 연결':'ZIP 다운로드 폴더',properties:zip?['openFile']:['openDirectory','createDirectory'],...(zip?{filters:[{name:'ZIP 파일',extensions:['zip']}]}:{})});return result.canceled?'':result.filePaths[0]||'';},
      startup:enabled=>{if(!smoke)app.setLoginItemSettings({name:'SimpleCommitWeb',openAtLogin:enabled,path:process.env.PORTABLE_EXECUTABLE_FILE||process.execPath,args:app.isPackaged?['--tray']:[app.getAppPath(),'--tray']});}
    });
    desktopNotices=new DesktopNotices(backend);
    backend.previewNotice=options=>desktopNotices.preview(options);
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
      if(process.argv.includes('--ui-test')){
        const checks=await window.webContents.executeJavaScript(`(async()=>{
          const wait=ms=>new Promise(r=>setTimeout(r,ms));
          const expect=(value,message)=>{if(!value)throw Error(message)};
          const form=document.querySelector('#settings-form');
          const input=(name,value)=>{const el=form.elements[name];if(el.type==='checkbox')el.checked=value;else el.value=value;el.dispatchEvent(new Event('input',{bubbles:true}))};
          const saved=async()=>{for(let n=0;n<100;n++){if(document.querySelector('#settings-status').textContent==='자동 저장됨')return;await wait(50)}throw Error('Autosave timed out')};
          expect(document.querySelector('#app-version').textContent==='v'+state.version,'Version display missing');
          expect(!form.querySelector('[type=submit]'),'Save button still visible');
          const rowHeight=document.querySelector('#rows tr').getBoundingClientRect().height;
          expect(rowHeight<=76,'Rows are not compact');
          page('settings');input('CheckMinutes',181);input('KeepNotificationUntilDismissed',false);input('NotificationSeconds',2);await saved();
          let stored=await api('state');expect(stored.settings.CheckMinutes===181&&stored.settings.NotificationSeconds===2,'Settings not saved');
          document.querySelector('#test-notice').click();await wait(250);expect(!document.querySelector('.toast'),'Duplicate in-app toast');await wait(2300);
          input('KeepNotificationUntilDismissed',true);await saved();
          input('CheckMinutes',0);await wait(650);stored=await api('state');expect(stored.settings.CheckMinutes===181,'Invalid input was saved');input('CheckMinutes',180);await saved();page('repositories');
          openRepo();await wait(240);expect(document.querySelector('#repo-dialog').open,'Dialog did not open');closeDialog(document.querySelector('#repo-dialog'));await wait(220);expect(!document.querySelector('#repo-dialog').open,'Dialog did not close');const toggle=document.querySelector('#notice-toggle');toggle.click();toggle.click();toggle.click();await wait(240);expect(!document.querySelector('#notice-panel').hidden&&!document.querySelector('#notice-panel').inert,'Interrupted fade did not reopen');toggle.click();await wait(220);expect(document.querySelector('#notice-panel').hidden,'Panel did not fade out');await refresh();expect(document.querySelector('#rows').getAnimations({subtree:true}).length===0,'Polling animated rows');return {fades:true,version:state.version,rowHeight,autosave:true,notificationExpiry:true,persistentNotice:true,invalidInputProtected:true};
        })()`);
        log('UI_CHECKS '+JSON.stringify(checks));
        const wait=ms=>new Promise(resolve=>setTimeout(resolve,ms));
        const expect=(value,message)=>{if(!value)throw Error(message)};
        window.hide();
        await api('test-notice',{KeepNotificationUntilDismissed:true,NotificationSeconds:1});
        for(let i=0;i<100&&!desktopNotices.window?.isVisible();i++)await wait(50);
        expect(desktopNotices.window?.isVisible(),'Desktop notice did not open');
        const bounds=desktopNotices.window.getBounds(),area=screen.getPrimaryDisplay().workArea;
        expect(bounds.x+bounds.width===area.x+area.width-12&&bounds.y+bounds.height===area.y+area.height-12,'Desktop notice position incorrect');
        expect(!window.isVisible()&&BrowserWindow.getFocusedWindow()!==desktopNotices.window,'Notice stole focus');
        await wait(1300);expect(desktopNotices.window.isVisible(),'Persistent desktop notice expired');
        await desktopNotices.window.webContents.capturePage().then(image=>fs.writeFileSync(path.join(smokeData,'desktop-notice.png'),image.toPNG()));
        await desktopNotices.window.webContents.executeJavaScript("document.querySelector('button').click()");await wait(300);expect(!desktopNotices.queue.length,'Preview dismiss failed');
        await api('test-notice',{KeepNotificationUntilDismissed:false,NotificationSeconds:1});await wait(1400);expect(!desktopNotices.queue.length&&!desktopNotices.window.isVisible(),'Timed desktop notice did not close');
        for(let i=0;i<4;i++)backend.notice('백그라운드 알림 테스트 '+i);
        desktopNotices.sync();await wait(100);expect(desktopNotices.queue.length===4&&desktopNotices.visible().length<=3,'Desktop notice queue failed');
        await desktopNotices.window.webContents.executeJavaScript("document.querySelector('button').click()");await wait(350);expect(backend.notifications.length===3,'Desktop dismiss did not acknowledge');
        await api('ack',{id:'all'});await wait(600);expect(!desktopNotices.queue.length,'Acknowledgement did not clear desktop notices');
        log('DESKTOP_NOTICES_PASS '+JSON.stringify({bounds,workArea:area,hiddenMain:true,persistent:true,timed:true,queue:true}));
        openWindow();
      }
      await window.webContents.capturePage().then(image=>fs.writeFileSync(path.join(smokeData,'electron-window.png'),image.toPNG()));
      window.close();if(window.isVisible())throw Error('Close-to-tray failed');openWindow();if(!window.isVisible())throw Error('Restore failed');
      log('TRAY_RESTORE_PASS');
      const secondArgs=['--tray','--smoke-test','--smoke-data='+smokeData];
      const second=spawn(process.execPath,app.isPackaged?secondArgs:[app.getAppPath(),...secondArgs],{windowsHide:true,stdio:'ignore'});
      second.on('error',e=>log('SECOND_ERROR '+e.message));
      setTimeout(()=>{quitting=true;app.quit();},15000);
    }
 }).catch(error=>{log('ERROR '+error.stack);dialog.showErrorBox('SimpleCommit 시작 실패',error.message);quitting=true;app.quit();});
}
