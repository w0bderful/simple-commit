const fs = require('node:fs');
const path = require('node:path');
const http = require('node:http');
const {randomUUID, randomBytes} = require('node:crypto');
const {Readable, Transform} = require('node:stream');
const {pipeline} = require('node:stream/promises');
const yauzl = require('yauzl');

const id = () => randomUUID().replaceAll('-', '');
const stamp = (ms = Date.now()) => `/Date(${ms})/`;
const time = value => {
  const match = typeof value === 'string' && value.match(/^\/Date\((-?\d+)\)\/$/);
  const result = match ? Number(match[1]) : Date.parse(value);
  return Number.isFinite(result) && result > 0 ? result : 0;
};
const safeName = value => value.replace(/[<>:"/\\|?*\x00-\x1f]/g, '_').replace(/[. ]+$/, '') || 'repository';
const escape = value => value.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
const samePath = (a, b) => !!a && !!b && path.resolve(a).toLowerCase() === path.resolve(b).toLowerCase();
const matchesSha = (full, short) => /^[a-f\d]{7,64}$/i.test(short || '') && (full || '').toLowerCase().startsWith(short.toLowerCase());
function parseRepo(value) {
  let u;
  try { u = new URL(value.trim()); } catch { throw Error('올바른 저장소 주소를 입력해 주세요.'); }
  if (u.protocol !== 'https:' || u.port || u.username || u.password || !['github.com', 'codeberg.org', 'gitlab.com', 'gitgud.io'].includes(u.hostname))
    throw Error('GitHub, GitGud, Codeberg 또는 GitLab.com의 https 저장소 주소를 입력해 주세요.');
  const repoPath = u.pathname.replace(/^\/+|\/+$/g, '').replace(/\.git$/, '');
  const parts = repoPath.split('/');
  if (u.search || u.hash || parts.some(p => !p || ['.', '..', '-'].includes(decodeURIComponent(p)) || /[/\\]/.test(decodeURIComponent(p))) || parts.length < 2 || (['github.com', 'codeberg.org'].includes(u.hostname) && parts.length !== 2))
    throw Error('파일·커밋 페이지가 아닌 저장소의 첫 화면 주소를 입력해 주세요.');
  const gitlab = ['gitlab.com', 'gitgud.io'].includes(u.hostname);
  return {host: u.hostname, path: repoPath, url: `https://${u.hostname}/${repoPath}`, name: safeName(decodeURIComponent(parts.at(-1))), owner: decodeURIComponent(parts[0]), gitlab,
    api: u.hostname === 'github.com' ? `https://api.github.com/repos/${repoPath}` : u.hostname === 'codeberg.org' ? `https://codeberg.org/api/v1/repos/${repoPath}` : `https://${u.hostname}/api/v4/projects/${encodeURIComponent(decodeURIComponent(repoPath))}`};
}
const identity = e => { const r = parseRepo(e.Url); return `${r.host}/${r.path}#${e.Branch}`; };
function folder(value) {
  if (typeof value !== 'string' || !path.isAbsolute(value) || value.includes('\0')) throw Error('다운로드 폴더의 전체 경로를 입력해 주세요.');
  return path.resolve(value);
}
function entry(value, defaultFolder) {
  if (!value || typeof value !== 'object' || Array.isArray(value)) throw Error('잘못된 저장소 항목입니다.');
  const r = parseRepo(value.Url);
  const e = {Id: value.Id || id(), Url: r.url, Branch: '', Folder: defaultFolder, LastSha: '', Message: '', Status: '확인 대기',
    CommitUtc: stamp(0), CheckedUtc: stamp(0), NextUtc: stamp(0), DownloadedSha: '', DownloadedPath: '', ImportedZip: false, DownloadSelected: false};
  for (const key of ['Branch','LastSha','Message','Status','DownloadedSha','DownloadedPath']) {
    if (value[key] != null) { if (typeof value[key] !== 'string') throw Error('목록에 잘못된 값이 있습니다.'); e[key] = value[key]; }
  }
  e.Folder = folder(value.Folder ?? defaultFolder);
  for (const key of ['CommitUtc','CheckedUtc','NextUtc']) e[key] = stamp(time(value[key]));
  for (const key of ['ImportedZip','DownloadSelected']) e[key] = value[key] === true;
  if (e.DownloadedPath && (!path.isAbsolute(e.DownloadedPath) || path.extname(e.DownloadedPath).toLowerCase() !== '.zip')) throw Error('목록에 잘못된 ZIP 경로가 있습니다.');
  return e;
}
function readJson(file, fallback) {
  if (!fs.existsSync(file)) return fallback;
  const value = JSON.parse(fs.readFileSync(file, 'utf8').replace(/^\uFEFF/, ''));
  if (value === null) throw Error(`${path.basename(file)} 파일이 비어 있습니다.`);
  return value;
}
function atomicJson(file, value) {
  const temp = `${file}.tmp`;
  try {
    fs.writeFileSync(temp, JSON.stringify(value, null, 2), 'utf8');
    if (fs.existsSync(file)) fs.copyFileSync(file, `${file}.bak`);
    fs.renameSync(temp, file);
  } finally { if (fs.existsSync(temp)) fs.unlinkSync(temp); }
}
function inspectZip(file) {
  return new Promise((resolve, reject) => {
    yauzl.open(file, {lazyEntries: true}, (err, zip) => {
      if (err) return reject(err);
      let root, count = 0;
      const fail = error => { zip.close(); reject(error); };
      zip.on('error', fail);
      zip.on('entry', item => {
        count++;
        const current = item.fileName.split('/')[0];
        if (root === undefined) root = current;
        else if (current !== root) root = '';
        zip.readEntry();
      });
      zip.on('end', () => {
        if (!count) return reject(Error('빈 ZIP 파일입니다.'));
        const comment = String(zip.comment).trim();
        const sha = root ? (/^[a-f\d]{40,64}$/i.test(comment) ? comment : root.match(/-([a-f\d]{7,64})$/i)?.[1] || '') : '';
        zip.once('close', () => resolve({root, sha}));
      });
      zip.readEntry();
    });
  });
}
async function canSkip(e) {
  if (!matchesSha(e.LastSha, e.DownloadedSha) || !fs.existsSync(e.DownloadedPath)) return false;
  try { const {sha} = await inspectZip(e.DownloadedPath); return !sha || matchesSha(e.LastSha, sha); } catch { return false; }
}
function zipDescription(e) {
  if (!e.DownloadedPath) return '다운로드 안 함';
  if (!fs.existsSync(e.DownloadedPath)) return 'ZIP 파일 없음';
  if (!e.DownloadedSha) return '기존 ZIP · 버전 미확인';
  if (!e.LastSha) return '원격 확인 필요';
  return matchesSha(e.LastSha, e.DownloadedSha) ? e.ImportedZip ? '최신 추정 · 연결 ZIP' : '최신 ZIP' : 'ZIP 업데이트 필요';
}
function age(value) {
  if (!time(value)) return '—';
  const minutes = Math.floor((Date.now() - time(value)) / 60000);
  if (minutes < -1) return '미래 시각';
  if (minutes < 1) return '방금 전';
  if (minutes < 60) return `${minutes}분 전`;
  if (minutes < 1440) return `${Math.floor(minutes / 60)}시간 ${minutes % 60}분 전`;
  return `${Math.floor(minutes / 1440)}일 전`;
}
class Backend {
  constructor({dir, documents, downloads, pick = async () => '', startup = () => {}, request = fetch, autoCheck = true}) {
    this.dir = dir; this.downloads = downloads; this.pick = pick; this.startup = startup; this.request = request; this.autoCheck = autoCheck;
    const saved = readJson(path.join(dir, 'settings.json'), {});
    if (typeof saved !== 'object' || Array.isArray(saved)) throw Error('설정 파일 형식이 잘못되었습니다.');
    this.settings = {DefaultDownloadFolder: documents, CheckMinutes: 180, NotificationSeconds: 7, KeepNotificationUntilDismissed: true, TrayHintShown: false, MaterialDark: false, SortRecent: false, StartWithWindows: false};
    for (const key of Object.keys(this.settings)) if (saved[key] !== undefined) this.settings[key] = saved[key];
    this.settings.DefaultDownloadFolder = folder(this.settings.DefaultDownloadFolder);
    for (const [key, max, fallback] of [['CheckMinutes',10080,180],['NotificationSeconds',120,7]]) this.settings[key] = Math.max(1, Math.min(max, Number(this.settings[key]) || fallback));
    const repositories = readJson(path.join(dir, 'repositories.json'), []);
    if (!Array.isArray(repositories)) throw Error('저장소 목록 파일 형식이 잘못되었습니다.');
    this.repositories = repositories.map(e => entry(e, documents));
    this.busy = false; this.activity = '준비 완료'; this.notifications = []; this.token = randomBytes(32).toString('hex');
  }
  save() { fs.mkdirSync(this.dir, {recursive: true}); atomicJson(path.join(this.dir, 'repositories.json'), this.repositories); atomicJson(path.join(this.dir, 'settings.json'), this.settings); }
  notice(Text) { this.notifications.push({Id: id(), Text, Created: new Date().toISOString()}); if (this.notifications.length > 100) this.notifications.shift(); }
  find(Id) { const e = this.repositories.find(r => r.Id === Id); if (!e) throw Error('저장소를 찾을 수 없습니다.'); return e; }
  sort() { if (this.settings.SortRecent) this.repositories.sort((a,b) => time(b.CommitUtc) - time(a.CommitUtc)); }
  state() {
    return {version: require('./package.json').version, settings: this.settings, repositories: this.repositories.map(e => ({...e, Name: parseRepo(e.Url).name, CommitUtc: time(e.CommitUtc) ? new Date(time(e.CommitUtc)).toISOString() : null, Age: age(e.CommitUtc), Zip: zipDescription(e)})), busy: this.busy, activity: this.activity, notifications: this.notifications, storage: this.dir};
  }
  async json(url) {
    const r = await this.request(url, {headers: {'User-Agent': 'SimpleCommit/1.0', Accept: 'application/json'}, signal: AbortSignal.timeout(30000)});
    if (!r.ok) throw Error(`저장소 서버 응답 오류 (${r.status})`);
    return r.json();
  }
  async branches(repo) {
    const metadata = await this.json(repo.api);
    const names = new Set();
    for (let page = 1; ; page++) {
      const suffix = repo.gitlab ? `/repository/branches?per_page=100&page=${page}` : `/branches?${repo.host === 'codeberg.org' ? 'limit' : 'per_page'}=100&page=${page}`;
      const rows = await this.json(repo.api + suffix);
      if (!Array.isArray(rows)) throw Error('브랜치 목록 응답이 잘못되었습니다.');
      const size = names.size;
      for (const row of rows) if (typeof row.name === 'string') names.add(row.name);
      if (!rows.length || names.size === size) break;
    }
    return {names: [...names], defaultBranch: metadata.default_branch || ''};
  }
  async latest(repo, branch) {
    let suffix = repo.gitlab ? '/repository/commits?per_page=1' : repo.host === 'codeberg.org' ? '/commits?limit=1&stat=false&verification=false&files=false' : '/commits?per_page=1';
    if (branch) suffix += `&${repo.gitlab ? 'ref_name' : 'sha'}=${encodeURIComponent(branch)}`;
    const rows = await this.json(repo.api + suffix), item = rows?.[0];
    const sha = repo.gitlab ? item?.id : item?.sha;
    if (!/^[a-f\d]{40,64}$/i.test(sha || '')) throw Error('커밋 정보를 찾을 수 없습니다.');
    return {sha, message: String(repo.gitlab ? item.title : item.commit?.message || '').split(/\r?\n/)[0], committed: stamp(time(repo.gitlab ? item.committed_date : item.commit?.committer?.date))};
  }
  async connect(e) {
    if (fs.existsSync(e.DownloadedPath) && e.DownloadedSha === e.LastSha && e.LastSha) return;
    const repo = parseRepo(e.Url);
    for (const dir of [...new Set([e.Folder, this.settings.DefaultDownloadFolder, this.downloads].filter(Boolean))]) {
      if (this.repositories.some(other => other !== e && other.Url !== e.Url && parseRepo(other.Url).name === repo.name && samePath(other.Folder, dir))) continue;
      let files;
      try { files = fs.readdirSync(dir).filter(f => /\.zip$/i.test(f)).map(f => path.join(dir, f)); } catch { continue; }
      let best;
      for (const file of files) {
        try {
          const info = await inspectZip(file);
          const suffix = e.Branch ? escape(e.Branch.replaceAll('/', '-')) : '(?:main|master|HEAD)';
          const pattern = new RegExp(`^(?:${escape(repo.name)}|${escape(repo.owner + '-' + repo.name)})-(?:${suffix}|[a-f\\d]{7,64}|.+-[a-f\\d]{40,64})$`, 'i');
          if (info.root.toLowerCase() !== repo.name.toLowerCase() && !pattern.test(info.root) && !(matchesSha(e.LastSha, info.sha) && info.root.toLowerCase().startsWith(repo.name.toLowerCase() + '-'))) continue;
          const fileSha = path.basename(file).match(new RegExp(`^${escape(repo.name)}-([a-f\\d]{8})-\\d{8}-\\d{6}(?:-\\d+)?\\.zip$`, 'i'))?.[1] || '';
          if (fileSha && info.sha && !matchesSha(info.sha, fileSha) && !matchesSha(fileSha, info.sha)) continue;
          const sha = info.sha.length > fileSha.length ? info.sha : fileSha;
          const candidate = {file, sha, modified: fs.statSync(file).mtimeMs};
          if (matchesSha(e.LastSha, sha)) { best = candidate; break; }
          if (!best || best.modified < candidate.modified) best = candidate;
        } catch { /* Ignore unreadable or invalid unrelated archives. */ }
      }
      if (best && (!fs.existsSync(e.DownloadedPath) || matchesSha(e.LastSha, best.sha))) {
        e.DownloadedPath = best.file; e.DownloadedSha = best.sha; e.ImportedZip = true;
        if (matchesSha(e.LastSha, best.sha)) return;
      }
    }
  }
  async download(repo, sha, branch, dir) {
    const target = path.join(dir, `${repo.name}-${safeName(branch)}.zip`);
    fs.mkdirSync(dir, {recursive: true});
    const temp = path.join(dir, `.simplecommit-${id()}.part`);
    const url = repo.gitlab ? `${repo.api}/repository/archive.zip?sha=${encodeURIComponent(sha)}` : repo.host === 'codeberg.org' ? `${repo.api}/archive/${sha}.zip` : `${repo.api}/zipball/${sha}`;
    try {
      const response = await this.request(url, {headers: {'User-Agent': 'SimpleCommit/1.0'}, signal: AbortSignal.timeout(30 * 60000)});
      if (!response.ok || !response.body) throw Error(`ZIP 다운로드 실패 (${response.status})`);
      let bytes = 0;
      const progress = new Transform({transform: (chunk, encoding, done) => { bytes += chunk.length; this.activity = `${repo.name} · ${(bytes / 1048576).toFixed(1)} MB`; done(null, chunk); }});
      await pipeline(Readable.fromWeb(response.body), progress, fs.createWriteStream(temp, {flags: 'wx'}));
      const info = await inspectZip(temp);
      if (info.sha && !matchesSha(sha, info.sha)) throw Error('다운로드한 ZIP의 커밋이 일치하지 않습니다.');
      fs.renameSync(temp, target);
      return target;
    } finally { if (fs.existsSync(temp)) fs.unlinkSync(temp); }
  }
  queue(entries, download) {
    if (this.busy || !entries.length) return;
    this.busy = true; this.activity = download ? 'ZIP 다운로드 준비 중' : '커밋 확인 중';
    this.job = this.run(entries, download);
  }
  async run(entries, download) {
    let updated = 0, failed = 0, saved = 0, skipped = 0;
    try {
      for (const e of entries) {
        try {
          const repo = parseRepo(e.Url); e.Status = '확인 중'; this.activity = `${repo.name} · 커밋 확인 중`;
          const info = await this.latest(repo, e.Branch);
          const changed = !!e.LastSha && e.LastSha !== info.sha;
          Object.assign(e, {LastSha: info.sha, Message: info.message, CommitUtc: info.committed, CheckedUtc: stamp(), Status: changed ? '새 커밋 있음' : '확인 완료'});
          if (changed) updated++;
          await this.connect(e);
          if (!download) continue;
          if (await canSkip(e)) { e.Status = '최신 ZIP 있음 · 건너뜀'; skipped++; continue; }
          const branch = e.Branch || (await this.branches(repo)).defaultBranch;
          if (!branch) throw Error('기본 브랜치를 찾을 수 없습니다.');
          const target = path.join(e.Folder, `${repo.name}-${safeName(branch)}.zip`), previous = e.DownloadedPath;
          if (this.repositories.some(other => other !== e && samePath(other.DownloadedPath, target))) throw Error('다른 저장소와 파일명이 같습니다. 다운로드 폴더를 변경하세요.');
          e.Status = '다운로드 중';
          e.DownloadedPath = await this.download(repo, info.sha, branch, e.Folder);
          e.DownloadedSha = info.sha; e.ImportedZip = false; e.Status = 'ZIP 저장 완료'; this.save(); saved++;
          if (previous && path.isAbsolute(previous) && path.extname(previous).toLowerCase() === '.zip' && !samePath(previous, target) && !this.repositories.some(other => other !== e && samePath(other.DownloadedPath, previous))) {
            try { if (fs.existsSync(previous)) fs.unlinkSync(previous); } catch (error) { e.Status = `저장 완료 · 이전 ZIP 삭제 실패: ${error.message}`; }
          }
        } catch (error) { failed++; e.Status = `실패: ${error.message}`; }
        finally { e.NextUtc = stamp(Date.now() + this.settings.CheckMinutes * 60000); this.save(); }
      }
    } catch (error) { this.notice(`저장 실패: ${error.message}`); }
    finally {
      this.busy = false; this.sort();
      this.activity = download ? `다운로드 완료 · 저장 ${saved} · 최신 ${skipped} · 실패 ${failed}` : `확인 완료 · 새 커밋 ${updated} · 실패 ${failed}`;
      if (updated) this.notice(`${updated}개 저장소에 새 커밋이 있습니다.`);
      if (failed) this.notice(`${failed}개 저장소에서 작업에 실패했습니다. 목록을 확인해 주세요.`);
      if (download) this.notice(this.activity);
      try { this.save(); } catch (error) { this.notice(`설정 저장 실패: ${error.message}`); }
    }
  }
  async api(route, d = {}) {
    if (!d || typeof d !== 'object' || Array.isArray(d)) throw Error('잘못된 요청입니다.');
    if (route === 'state') return this.state();
    if (route === 'export') return {format: 'SimpleCommit', version: 1, repositories: this.repositories};
    if (route === 'ack') { this.notifications = this.notifications.filter(n => d.id !== 'all' && n.Id !== d.id); return {ok: true}; }
    if (route === 'branches') return this.branches(parseRepo(d.url));
    if (route === 'pick-folder' || route === 'pick-zip') return {path: await this.pick(route === 'pick-zip')};
    if (this.busy || this.mutating) throw Error('진행 중인 작업이 끝난 뒤 다시 시도해 주세요.');
    this.mutating = true;
    try { return await this.mutate(route, d); } finally { this.mutating = false; }
  }
  async mutate(route, d) {
    switch (route) {
      case 'import': {
        const values = JSON.parse(d.json);
        if (!Array.isArray(values) || values.length > 1000) throw Error('목록은 최대 1000개까지 가져올 수 있습니다.');
        const pending = values.map(v => entry(v, this.settings.DefaultDownloadFolder));
        const identities = new Set(this.repositories.map(identity)); let skipped = 0;
        const additions = pending.filter(e => { const key = identity(e); if (identities.has(key)) { skipped++; return false; } identities.add(key); e.Id = id(); e.NextUtc = stamp(0); e.Status = '가져옴 · 확인 대기'; return true; });
        this.repositories.push(...additions); this.save(); return {added: additions.length, skipped};
      }
      case 'settings': {
        const minutes = d.CheckMinutes ?? this.settings.CheckMinutes;
        if (!Number.isInteger(minutes) || minutes < 1 || minutes > 10080) throw Error('확인 간격은 1~10080분입니다.');
        const next = {...this.settings, DefaultDownloadFolder: folder(d.DefaultDownloadFolder ?? this.settings.DefaultDownloadFolder), CheckMinutes: minutes};
        for (const key of ['MaterialDark','StartWithWindows','KeepNotificationUntilDismissed']) if (d[key] !== undefined) { if (typeof d[key] !== 'boolean') throw Error('설정 값이 잘못되었습니다.'); next[key] = d[key]; }
        if (d.NotificationSeconds !== undefined) { if (!Number.isInteger(d.NotificationSeconds)) throw Error('알림 시간을 입력해 주세요.'); next.NotificationSeconds = Math.max(1, Math.min(120, d.NotificationSeconds)); }
        this.startup(next.StartWithWindows);
        if (minutes !== this.settings.CheckMinutes) for (const e of this.repositories) e.NextUtc = stamp(Date.now() + minutes * 60000);
        this.settings = next; break;
      }
      case 'add': {
        const dir = folder(d.folder ?? this.settings.DefaultDownloadFolder);
        const pending = String(d.urls || '').split(/\r?\n/).map(s => s.trim()).filter(Boolean).map(Url => entry({Url, Branch: d.branch || '', Folder: dir, DownloadSelected: true}, dir));
        if (!pending.length) throw Error('저장소 주소를 입력해 주세요.');
        const identities = new Set(this.repositories.map(identity)); let added = 0;
        for (const e of pending) if (!identities.has(identity(e))) { identities.add(identity(e)); this.repositories.push(e); added++; }
        this.activity = `${added}개 등록 · 중복 ${pending.length - added}개 건너뜀`; break;
      }
      case 'edit': {
        const old = this.find(d.id), next = entry({Url: d.url, Branch: d.branch || '', Folder: d.folder, DownloadSelected: old.DownloadSelected, Id: old.Id}, this.settings.DefaultDownloadFolder);
        if (this.repositories.some(e => e !== old && identity(e) === identity(next))) throw Error('이미 등록된 저장소와 브랜치입니다.');
        if (identity(old) === identity(next)) old.Folder = next.Folder; else this.repositories[this.repositories.indexOf(old)] = next;
        break;
      }
      case 'delete': this.repositories.splice(this.repositories.indexOf(this.find(d.id)), 1); break;
      case 'select': for (const e of this.repositories) if (d.id === 'all' || e.Id === d.id) e.DownloadSelected = d.selected === true; break;
      case 'sort': this.settings.SortRecent = d.recent === true; this.sort(); break;
      case 'move': {
        const moving = this.find(d.id), target = this.find(d.before), from = this.repositories.indexOf(moving);
        let to = this.repositories.indexOf(target) + (d.after ? 1 : 0);
        this.repositories.splice(from, 1); if (to > from) to--; this.repositories.splice(to, 0, moving); this.settings.SortRecent = false; break;
      }
      case 'link': {
        const e = this.find(d.id);
        if (typeof d.path !== 'string' || !path.isAbsolute(d.path) || path.extname(d.path).toLowerCase() !== '.zip') throw Error('ZIP 파일을 선택해 주세요.');
        const info = await inspectZip(d.path); e.DownloadedPath = path.resolve(d.path); e.DownloadedSha = info.sha; e.ImportedZip = true; break;
      }
      case 'run': this.queue(this.repositories.filter(e => !d.download || e.DownloadSelected), !!d.download); return {ok: true};
      default: throw Error('지원하지 않는 요청입니다.');
    }
    this.save(); return {ok: true};
  }
  async start() {
    this.save();
    this.server = http.createServer(async (req, res) => {
      const reply = (status, body, type = 'application/json; charset=utf-8') => {
        res.writeHead(status, {'Content-Type': type, 'Cache-Control': 'no-store', 'X-Content-Type-Options': 'nosniff', 'Referrer-Policy': 'no-referrer', 'Content-Security-Policy': "default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self' data:; connect-src 'self'; frame-ancestors 'none'; base-uri 'none'; form-action 'self'"});
        res.end(typeof body === 'string' || Buffer.isBuffer(body) ? body : JSON.stringify(body));
      };
      try {
        if (req.headers.host !== new URL(this.origin).host || (req.headers.origin && req.headers.origin !== this.origin) || req.headers['sec-fetch-site'] === 'cross-site') return reply(403, {error: '접근이 거부되었습니다.'});
        const pathname = new URL(req.url, this.origin).pathname;
        if (pathname.startsWith('/api/')) {
          if (req.headers['x-simplecommit'] !== this.token) return reply(403, {error: '접근이 거부되었습니다.'});
          if (req.method !== 'POST' && !(req.method === 'GET' && pathname === '/api/state')) return reply(405, {error: '허용되지 않은 요청입니다.'});
          if (Number(req.headers['content-length']) > 1048576) return reply(413, {error: '요청이 너무 큽니다.'});
          const chunks = []; let size = 0;
          for await (const chunk of req) { size += chunk.length; if (size > 1048576) throw Error('요청이 너무 큽니다.'); chunks.push(chunk); }
          const data = size ? JSON.parse(Buffer.concat(chunks).toString('utf8')) : {};
          return reply(200, await this.api(pathname.slice(5), data));
        }
        if (req.method !== 'GET') return reply(405, {});
        const name = pathname === '/' ? 'index.html' : pathname.slice(1);
        const types = {'index.html': 'text/html; charset=utf-8', 'app.js': 'text/javascript; charset=utf-8', 'style.css': 'text/css; charset=utf-8', 'icon.png': 'image/png'};
        if (!types[name]) return reply(404, {});
        let content = fs.readFileSync(path.join(__dirname, 'www', name));
        if (name === 'index.html') content = content.toString('utf8').replace('__TOKEN__', this.token);
        reply(200, content, types[name]);
      } catch (error) { if (!res.headersSent && !res.destroyed) reply(400, {error: error.message}); }
    });
    this.server.requestTimeout = 30000; this.server.headersTimeout = 10000;
    await new Promise((resolve, reject) => { this.server.once('error', reject); this.server.listen(0, '127.0.0.1', resolve); });
    this.origin = `http://127.0.0.1:${this.server.address().port}`;
    if (this.autoCheck) {
      const tick = () => { if (!this.busy && !this.mutating) this.queue(this.repositories.filter(e => time(e.NextUtc) <= Date.now()), false); };
      this.timer = setInterval(tick, 15000); tick();
    }
    return this;
  }
  async close() { clearInterval(this.timer); if (this.job) await this.job; if (this.server) { this.server.closeAllConnections(); await new Promise(resolve => this.server.close(resolve)); } }
}
module.exports = {Backend, parseRepo, inspectZip, canSkip, zipDescription, time, stamp};
