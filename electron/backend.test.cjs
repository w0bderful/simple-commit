const {test} = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const os = require('node:os');
const path = require('node:path');
const {crc32} = require('node:zlib');
const {Backend, parseRepo, inspectZip, canSkip, time, stamp} = require('./backend.cjs');
const SHA = 'a'.repeat(40), NEXT = 'b'.repeat(40);

// A real stored ZIP fixture, including its central directory and git comment.
function zip(root = 'project-master', sha = SHA) {
  const name = Buffer.from(`${root}/README.txt`), data = Buffer.from('repository source\n');
  const local = Buffer.alloc(30), central = Buffer.alloc(46), end = Buffer.alloc(22), comment = Buffer.from(sha);
  local.writeUInt32LE(0x04034b50); local.writeUInt16LE(20, 4); local.writeUInt32LE(crc32(data), 14);
  local.writeUInt32LE(data.length, 18); local.writeUInt32LE(data.length, 22); local.writeUInt16LE(name.length, 26);
  central.writeUInt32LE(0x02014b50); central.writeUInt16LE(20, 4); central.writeUInt16LE(20, 6);
  central.writeUInt32LE(crc32(data), 16); central.writeUInt32LE(data.length, 20); central.writeUInt32LE(data.length, 24); central.writeUInt16LE(name.length, 28);
  end.writeUInt32LE(0x06054b50); end.writeUInt16LE(1, 8); end.writeUInt16LE(1, 10);
  end.writeUInt32LE(central.length + name.length, 12); end.writeUInt32LE(local.length + name.length + data.length, 16); end.writeUInt16LE(comment.length, 20);
  return Buffer.concat([local, name, data, central, name, end, comment]);
}
function setup(t, options = {}) {
  const dir = fs.mkdtempSync(path.join(os.tmpdir(), 'simplecommit-node-test-'));
  const documents = path.join(dir, 'documents'), downloads = path.join(dir, 'downloads');
  fs.mkdirSync(documents); fs.mkdirSync(downloads);
  const backend = new Backend({dir: path.join(dir, 'data'), documents, downloads, autoCheck: false, ...options});
  t.after(async () => { await backend.close(); fs.rmSync(dir, {recursive: true, force: true}); });
  return {backend, dir, documents, downloads};
}
const add = (backend, urls = 'https://github.com/owner/project', branch = 'master') => backend.api('add', {urls, branch, folder: backend.settings.DefaultDownloadFolder});

test('provider URLs, nested GitLab projects and rejected subpages', () => {
  assert.equal(parseRepo('https://github.com/owner/project.git').url, 'https://github.com/owner/project');
  assert.equal(parseRepo('https://gitlab.com/group/sub/project').api, 'https://gitlab.com/api/v4/projects/group%2Fsub%2Fproject');
  assert.equal(parseRepo('https://codeberg.org/a/b').api, 'https://codeberg.org/api/v1/repos/a/b');
  assert.equal(parseRepo('https://gitgud.io/a/b').gitlab, true);
  for (const url of ['http://github.com/a/b','https://example.com/a/b','https://github.com/a/b/tree/master','https://gitlab.com/a/b/-/tree/main','https://user@github.com/a/b','https://github.com/a/b?x=1','https://github.com/a%2fb/c']) assert.throws(() => parseRepo(url));
});
test('separate persistence, selections, BOM and current date format round trip', async t => {
  const {backend, dir, documents, downloads} = setup(t);
  await add(backend); backend.repositories[0].CommitUtc = '/Date(1789999200000)/'; backend.save();
  const file = path.join(backend.dir, 'settings.json'); fs.writeFileSync(file, '\uFEFF' + fs.readFileSync(file, 'utf8'));
  const loaded = new Backend({dir: backend.dir, documents, downloads, autoCheck: false});
  assert.equal(loaded.repositories.length, 1); assert.equal(loaded.repositories[0].DownloadSelected, true);
  assert.equal(time(loaded.repositories[0].CommitUtc), 1789999200000);
  assert.equal(Object.hasOwn(JSON.parse(fs.readFileSync(file, 'utf8').slice(1)), 'Repositories'), false);
  assert.ok(fs.existsSync(path.join(dir, 'data/repositories.json.bak')));
});
test('corrupt list refuses startup without overwriting data; no embedded-list migration', async t => {
  const {backend, documents, downloads} = setup(t); backend.save();
  const list = path.join(backend.dir, 'repositories.json'); fs.writeFileSync(list, 'invalid');
  assert.throws(() => new Backend({dir: backend.dir, documents, downloads}));
  assert.equal(fs.readFileSync(list, 'utf8'), 'invalid');
  fs.unlinkSync(list); fs.writeFileSync(path.join(backend.dir, 'settings.json'), JSON.stringify({Repositories: [{Url: 'https://github.com/a/b'}]}));
  assert.equal(new Backend({dir: backend.dir, documents, downloads}).repositories.length, 0);
});
test('bulk add/import validate atomically, deduplicate and preserve selection', async t => {
  const {backend, documents} = setup(t);
  await assert.rejects(add(backend, 'https://github.com/a/b\nhttps://invalid.example/a/b'));
  assert.equal(backend.repositories.length, 0);
  await add(backend, 'https://github.com/a/b\nhttps://github.com/a/b\nhttps://github.com/a/c');
  assert.equal(backend.repositories.length, 2);
  const exported = await backend.api('export');
  assert.deepEqual(await backend.api('import', {json: JSON.stringify(exported.repositories)}), {added: 0, skipped: 2});
  await assert.rejects(backend.api('import', {json: JSON.stringify([{Url: 'https://github.com/a/d',Folder: documents},{Url:'invalid'}])}));
  assert.equal(backend.repositories.length, 2);
  await backend.api('select', {id:'all',selected:false});
  assert.ok(backend.repositories.every(e => !e.DownloadSelected));
});
test('ordering, edit, settings and acknowledgement', async t => {
  let startup;
  const {backend} = setup(t, {startup: enabled => {startup = enabled;}});
  await add(backend, 'https://github.com/a/one\nhttps://github.com/a/two\nhttps://github.com/a/three');
  const [a,b,c] = backend.repositories; a.CommitUtc = stamp(1); b.CommitUtc = stamp(3); c.CommitUtc = stamp(2);
  await backend.api('sort', {recent:true}); assert.deepEqual(backend.repositories.map(e=>e.Id), [b.Id,c.Id,a.Id]);
  await backend.api('move', {id:b.Id,before:a.Id,after:true}); assert.deepEqual(backend.repositories.map(e=>e.Id), [c.Id,a.Id,b.Id]); assert.equal(backend.settings.SortRecent,false);
  await backend.api('edit', {id:a.Id,url:a.Url,branch:'other',folder:a.Folder}); assert.equal(backend.find(a.Id).LastSha,'');
  await backend.api('settings', {CheckMinutes:5,StartWithWindows:true,KeepNotificationUntilDismissed:false,NotificationSeconds:120});
  assert.equal(startup,true); assert.ok(backend.repositories.every(e=>time(e.NextUtc)>Date.now()));
  assert.equal(backend.settings.NotificationSeconds,120);
  await assert.rejects(backend.api('settings',{CheckMinutes:0}));
  backend.notice('test'); await backend.api('ack',{id:'all'}); assert.equal(backend.notifications.length,0);
});
test('all provider latest and paged branches preserve branch encoding', async t => {
  const urls=[];
  const {backend} = setup(t, {request: async url => {
    urls.push(url);
    if (url.includes('/branches?')) return Response.json(url.includes('page=1')?[{name:'main'},{name:'feature/test'}]:[]);
    if (url.includes('/commits?')) return Response.json([{sha:SHA,id:SHA,title:'title',committed_date:'2026-09-20T00:00:00Z',commit:{message:'title\nbody',committer:{date:'2026-09-20T00:00:00Z'}}}]);
    return Response.json({default_branch:'main'});
  }});
  for (const host of ['github.com','codeberg.org','gitlab.com','gitgud.io']) {
    const repo=parseRepo(`https://${host}/a/b`);
    assert.deepEqual(await backend.branches(repo),{names:['main','feature/test'],defaultBranch:'main'});
    assert.equal((await backend.latest(repo,'feature/test')).sha,SHA);
  }
  assert.equal(urls.filter(u=>u.includes('page=2')).length,4);
  assert.equal(urls.filter(u=>u.includes('feature%2Ftest')).length,4);
});
test('ZIP identity, latest skip and invalid archive rejection', async t => {
  const {documents}=setup(t); const file=path.join(documents,'project-master.zip');
  fs.writeFileSync(file,zip()); assert.deepEqual(await inspectZip(file),{root:'project-master',sha:SHA});
  const e={LastSha:SHA,DownloadedSha:SHA,DownloadedPath:file}; assert.equal(await canSkip(e),true);
  assert.equal(await canSkip({...e,LastSha:NEXT}),false);
  fs.writeFileSync(file,'not a ZIP'); await assert.rejects(inspectZip(file)); assert.equal(await canSkip(e),false);
});
test('existing archive discovery and ambiguity protection', async t => {
  const {backend,documents}=setup(t); await add(backend);
  const file=path.join(documents,'existing.zip'); fs.writeFileSync(file,zip());
  const e=backend.repositories[0]; e.LastSha=SHA; await backend.connect(e); assert.equal(e.DownloadedPath,file);
  await add(backend,'https://github.com/other/project'); e.DownloadedPath=''; e.DownloadedSha='';
  await backend.connect(e); assert.equal(e.DownloadedPath,'');
});
test('download pins SHA, keeps branch filename, deletes tracked prior ZIP, skips latest', async t => {
  let downloads=0;
  const {backend,documents}=setup(t,{request:async url=>{assert.ok(url.endsWith(SHA)); downloads++; return new Response(zip());}});
  await add(backend); backend.latest=async()=>({sha:SHA,message:'commit',committed:stamp()});
  const e=backend.repositories[0], old=path.join(documents,'old.zip'); fs.writeFileSync(old,zip('project-old',NEXT));
  e.DownloadedPath=old; e.DownloadedSha=NEXT;
  await backend.api('run',{download:true}); await backend.job;
  assert.equal(downloads,1); assert.equal(path.basename(e.DownloadedPath),'project-master.zip'); assert.equal(fs.existsSync(old),false);
  await backend.api('run',{download:true}); await backend.job;
  assert.equal(downloads,1); assert.match(e.Status,/건너뜀/);
});
test('bad download preserves destination and tracked previous file, removes partial', async t => {
  const {backend,documents}=setup(t,{request:async()=>new Response('bad archive')}); await add(backend);
  backend.latest=async()=>({sha:NEXT,message:'new',committed:stamp()});
  const e=backend.repositories[0], target=path.join(documents,'project-master.zip'), old=path.join(documents,'old.zip');
  const good=zip(); fs.writeFileSync(target,good); fs.writeFileSync(old,good); e.DownloadedPath=old;e.DownloadedSha=SHA;
  await backend.api('run',{download:true}); await backend.job;
  assert.match(e.Status,/실패/); assert.deepEqual(fs.readFileSync(target),good); assert.equal(fs.existsSync(old),true);
  assert.equal(fs.readdirSync(documents).some(f=>f.endsWith('.part')),false);
});
test('successful update replaces an existing stable branch ZIP', async t => {
  const {backend,documents}=setup(t,{request:async()=>new Response(zip('project-master',NEXT))}); await add(backend);
  backend.latest=async()=>({sha:NEXT,message:'new',committed:stamp()});
  const e=backend.repositories[0], target=path.join(documents,'project-master.zip');
  fs.writeFileSync(target,zip());e.DownloadedPath=target;e.DownloadedSha=SHA;
  await backend.api('run',{download:true});await backend.job;
  assert.equal(e.DownloadedPath,target);assert.equal(e.DownloadedSha,NEXT);assert.equal((await inspectZip(target)).sha,NEXT);
  assert.equal(e.Status,'ZIP 저장 완료');assert.deepEqual(fs.readdirSync(documents),['project-master.zip']);
});
test('shared previous file and destination collision are protected', async t => {
  const {backend,documents}=setup(t,{request:async()=>new Response(zip())});
  await add(backend,'https://github.com/owner/project\nhttps://github.com/owner/other');
  const [e,other]=backend.repositories, old=path.join(documents,'old.zip'); fs.writeFileSync(old,zip('old',NEXT));
  e.DownloadedPath=other.DownloadedPath=old;e.DownloadedSha=other.DownloadedSha=NEXT;other.DownloadSelected=false;
  backend.latest=async()=>({sha:SHA,message:'commit',committed:stamp()});
  await backend.api('run',{download:true}); await backend.job; assert.equal(fs.existsSync(old),true);
  other.DownloadedPath=e.DownloadedPath;e.DownloadedPath=old;e.DownloadedSha=NEXT;fs.unlinkSync(other.DownloadedPath);
  await backend.api('run',{download:true}); await backend.job; assert.match(e.Status,/파일명이 같습니다/);
});
test('desktop notification preview uses draft settings without adding unread notices', async t => {
  const {backend}=setup(t);let preview;
  backend.previewNotice=options=>{preview=options};backend.busy=true;
  await backend.api('test-notice',{KeepNotificationUntilDismissed:false,NotificationSeconds:2});
  assert.deepEqual(preview,{KeepNotificationUntilDismissed:false,NotificationSeconds:2});
  assert.equal(backend.notifications.length,0);assert.equal(backend.settings.NotificationSeconds,7);
  await backend.api('test-notice',{KeepNotificationUntilDismissed:true,NotificationSeconds:999});
  assert.equal(preview.NotificationSeconds,120);assert.equal(preview.KeepNotificationUntilDismissed,true);backend.busy=false;
});
test('loopback API, token, origin, host, method restrictions and busy guard', async t => {
  const {backend}=setup(t); await backend.start();
  const home=await fetch(backend.origin); assert.match(await home.text(),new RegExp(backend.token));
  assert.match(home.headers.get('content-security-policy'),/frame-ancestors 'none'/);
  assert.equal((await fetch(backend.origin+'/api/state')).status,403);
  const headers={'X-SimpleCommit':backend.token};
  assert.equal((await fetch(backend.origin+'/api/state',{headers})).status,200);
  assert.equal((await fetch(backend.origin+'/api/state',{headers:{...headers,Origin:'https://example.com'}})).status,403);
  const wrongHost = await new Promise((resolve,reject)=>require('node:http').get(backend.origin+'/api/state',{headers:{...headers,Host:'localhost'}},r=>{r.resume();resolve(r.statusCode);}).on('error',reject));
  assert.equal(wrongHost,403);
  assert.equal((await fetch(backend.origin+'/api/delete',{headers})).status,405);
  assert.equal((await fetch(backend.origin+'/backend.cjs')).status,404);
  backend.busy=true; await assert.rejects(add(backend)); assert.equal((await backend.api('state')).busy,true); backend.busy=false;
});
