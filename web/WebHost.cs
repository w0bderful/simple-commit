using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Web.Script.Serialization;

// A single-user loopback host: no runtime installation, URL ACL or public listener.
public class WebHost {
    readonly object gate=new object();
    readonly string token=Guid.NewGuid().ToString("N"), file;
    readonly int port;
    Settings config;
    TcpListener listener;
    bool busy,stopping;
    string activity="준비 완료";
    readonly List<Notice> notices=new List<Notice>();
    class Notice { public string Id=Guid.NewGuid().ToString("N"),Text; public DateTime Created=DateTime.UtcNow; }
    string Origin {get{return "http://127.0.0.1:"+port;}}
    JavaScriptSerializer Json(){return new JavaScriptSerializer{MaxJsonLength=4*1024*1024};}
    public WebHost(string dir,int number){
        port=number;file=Path.Combine(dir,"settings.json");
        config=SettingsStore.Load(file);
        // The desktop and web hosts have independent startup registrations.
        if(!File.Exists(file))config.StartWithWindows=false;
        SettingsStore.Save(file,config);
    }
    void Save(){SettingsStore.Save(file,config);}
    void NoticeText(string text){notices.Add(new Notice{Text=text});if(notices.Count>100)notices.RemoveAt(0);}
    string Str(Dictionary<string,object> d,string key,string fallback=""){object value;return d.TryGetValue(key,out value)?Convert.ToString(value):fallback;}
    bool Bool(Dictionary<string,object>d,string key,bool fallback=false){object value;return d.TryGetValue(key,out value)?Convert.ToBoolean(value):fallback;}
    int Number(Dictionary<string,object>d,string key,int fallback){object value;return d.TryGetValue(key,out value)?Convert.ToInt32(value):fallback;}
    RepoEntry Entry(string id){var e=config.Repositories.FirstOrDefault(x=>x.Id==id);if(e==null)throw new Exception("저장소를 찾을 수 없습니다.");return e;}
    string Folder(string value){if(String.IsNullOrWhiteSpace(value)||!Path.IsPathRooted(value))throw new Exception("다운로드 폴더의 전체 경로를 입력해 주세요.");return Path.GetFullPath(value);}
    void Idle(){if(busy)throw new Exception("진행 중인 작업이 끝난 뒤 다시 시도해 주세요.");}
    public void Start(){listener=new TcpListener(IPAddress.Loopback,port);listener.Start(32);Task.Run((Action)Accept);}
    void Accept(){while(!stopping){try{var client=listener.AcceptTcpClient();Task.Run(()=>Serve(client));}catch{if(!stopping)Thread.Sleep(100);}}}
    void Reply(NetworkStream stream,int code,byte[] body,string type){
        string headers="HTTP/1.1 "+code+" "+(code==200?"OK":"Error")+"\r\nContent-Type: "+type+"\r\nContent-Length: "+body.Length+"\r\nConnection: close\r\nCache-Control: no-store\r\nX-Content-Type-Options: nosniff\r\nReferrer-Policy: no-referrer\r\nContent-Security-Policy: default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self' data:; connect-src 'self'; frame-ancestors 'none'; base-uri 'none'; form-action 'self'\r\n\r\n";
        byte[] h=Encoding.ASCII.GetBytes(headers);stream.Write(h,0,h.Length);stream.Write(body,0,body.Length);
    }
    void Serve(TcpClient client){using(client){client.ReceiveTimeout=10000;client.SendTimeout=10000;var stream=client.GetStream();try{
        var bytes=new List<byte>();int b;
        while((b=stream.ReadByte())!=-1){bytes.Add((byte)b);int n=bytes.Count;if(n>16384)throw new Exception("요청이 너무 큽니다.");if(n>=4&&bytes[n-4]==13&&bytes[n-3]==10&&bytes[n-2]==13&&bytes[n-1]==10)break;}
        string[] lines=Encoding.ASCII.GetString(bytes.ToArray()).Split(new[]{"\r\n"},StringSplitOptions.None);string[] request=lines[0].Split(' ');
        if(request.Length!=3)throw new Exception("잘못된 요청입니다.");
        var headers=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
        foreach(string line in lines.Skip(1)){int colon=line.IndexOf(':');if(colon>0){string key=line.Substring(0,colon);if(headers.ContainsKey(key))throw new Exception("중복 헤더입니다.");headers[key]=line.Substring(colon+1).Trim();}}
        string host; if(!headers.TryGetValue("Host",out host)||host!="127.0.0.1:"+port){Reply(stream,403,new byte[0],"text/plain");return;}
        string origin;if(headers.TryGetValue("Origin",out origin)&&origin!=Origin){Reply(stream,403,new byte[0],"text/plain");return;}
        string fetch;if(headers.TryGetValue("Sec-Fetch-Site",out fetch)&&fetch=="cross-site"){Reply(stream,403,new byte[0],"text/plain");return;}
        string path=request[1].Split('?')[0];
        if(path.StartsWith("/api/")){
            string auth;if(!headers.TryGetValue("X-SimpleCommit",out auth)||auth!=token){Reply(stream,403,new byte[0],"text/plain");return;}
            if(request[0]!="POST"&&!(request[0]=="GET"&&path=="/api/state"))throw new Exception("허용되지 않은 요청입니다.");
            if(headers.ContainsKey("Transfer-Encoding"))throw new Exception("지원하지 않는 요청 형식입니다.");
            int length=headers.ContainsKey("Content-Length")?Int32.Parse(headers["Content-Length"]):0;if(length<0||length>1048576)throw new Exception("요청이 너무 큽니다.");
            byte[] body=new byte[length];for(int at=0;at<length;){int read=stream.Read(body,at,length-at);if(read==0)throw new IOException("연결이 끊겼습니다.");at+=read;}
            var data=length==0?new Dictionary<string,object>():Json().Deserialize<Dictionary<string,object>>(Encoding.UTF8.GetString(body));
            object result=Api(path,data);Reply(stream,200,Encoding.UTF8.GetBytes(Json().Serialize(result)),"application/json; charset=utf-8");
        }else{
            if(request[0]!="GET"){Reply(stream,405,new byte[0],"text/plain");return;}
            string name=path=="/"?"index.html":path.TrimStart('/');
            if(!new[]{"index.html","app.js","style.css","icon.png"}.Contains(name)){Reply(stream,404,new byte[0],"text/plain");return;}
            using(var asset=typeof(WebHost).Assembly.GetManifestResourceStream("www."+name))using(var ms=new MemoryStream()){asset.CopyTo(ms);byte[] content=ms.ToArray();if(name=="index.html")content=Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(content).Replace("__TOKEN__",token));Reply(stream,200,content,name.EndsWith("png")?"image/png":name.EndsWith("js")?"text/javascript; charset=utf-8":name.EndsWith("css")?"text/css; charset=utf-8":"text/html; charset=utf-8");}
        }
    }catch(Exception ex){try{Reply(stream,400,Encoding.UTF8.GetBytes(Json().Serialize(new{error=ex.Message})),"application/json; charset=utf-8");}catch{}}}}
    object State(){return new{settings=new{config.CheckMinutes,config.DefaultDownloadFolder,config.MaterialDark,config.SortRecent,config.StartWithWindows,config.KeepNotificationUntilDismissed,config.NotificationSeconds},repositories=config.Repositories.Select(e=>new{e.Id,e.Url,Name=Repo.Parse(e.Url).Name,e.Branch,e.Folder,e.LastSha,e.Message,e.Status,e.DownloadedPath,e.DownloadSelected,CommitUtc=e.CommitUtc==DateTime.MinValue?null:e.CommitUtc.ToUniversalTime().ToString("o"),Age=RelativeTime.Format(e.CommitUtc,DateTime.UtcNow),Zip=ZipState.Describe(e,File.Exists(e.DownloadedPath))}).ToArray(),busy,activity,notifications=notices.ToArray(),storage=Path.GetDirectoryName(file)};}
    object Api(string route,Dictionary<string,object>d){
        if(route=="/api/branches"){var repo=Repo.Parse(Str(d,"url"));var branches=Backend.Branches(repo,CancellationToken.None);return new{names=branches.Names,defaultBranch=branches.Default};}
        if(route=="/api/pick-folder"||route=="/api/pick-zip")return new{path=Pick(route.EndsWith("zip"))};
        lock(gate){
            if(route=="/api/state")return State();
            if(route=="/api/export")return new{format="SimpleCommit",version=1,repositories=config.Repositories.ToArray()};
            if(route=="/api/ack"){string id=Str(d,"id");notices.RemoveAll(x=>id=="all"||x.Id==id);return new{ok=true};}
            Idle();
            switch(route){
                case "/api/import":
                    var imported=Json().Deserialize<List<RepoEntry>>(Str(d,"json"));
                    if(imported==null||imported.Count>1000)throw new Exception("목록은 최대 1000개까지 가져올 수 있습니다.");
                    var additions=new List<RepoEntry>();int skipped=0;
                    foreach(var item in imported){
                        if(item==null)throw new Exception("잘못된 저장소 항목입니다.");
                        Repo.Parse(item.Url);item.Branch=item.Branch??"";item.Folder=Folder(item.Folder);
                        if(config.Repositories.Concat(additions).Any(x=>x.Identity==item.Identity)){skipped++;continue;}
                        item.DownloadedPath=item.DownloadedPath??"";item.DownloadedSha=item.DownloadedSha??"";item.LastSha=item.LastSha??"";
                        if(item.DownloadedPath!=""&&(!Path.IsPathRooted(item.DownloadedPath)||!String.Equals(Path.GetExtension(item.DownloadedPath),".zip",StringComparison.OrdinalIgnoreCase)))throw new Exception("목록에 잘못된 ZIP 경로가 있습니다.");
                        item.Id=Guid.NewGuid().ToString("N");item.NextUtc=DateTime.MinValue;item.Status="가져옴 · 확인 대기";additions.Add(item);
                    }
                    config.Repositories.AddRange(additions);Save();return new{added=additions.Count,skipped};
                case "/api/settings":
                    int minutes=Number(d,"CheckMinutes",config.CheckMinutes);if(minutes<1||minutes>10080)throw new Exception("확인 간격은 1~10080분입니다.");
                    config.DefaultDownloadFolder=Folder(Str(d,"DefaultDownloadFolder",config.DefaultDownloadFolder));
                    if(minutes!=config.CheckMinutes)foreach(var item in config.Repositories)item.NextUtc=DateTime.UtcNow.AddMinutes(minutes);
                    config.CheckMinutes=minutes;config.MaterialDark=Bool(d,"MaterialDark");config.StartWithWindows=Bool(d,"StartWithWindows");config.KeepNotificationUntilDismissed=Bool(d,"KeepNotificationUntilDismissed",true);config.NotificationSeconds=Math.Max(1,Math.Min(120,Number(d,"NotificationSeconds",7)));RegisterStartup();Save();break;
                case "/api/add":
                    string folder=Folder(Str(d,"folder",config.DefaultDownloadFolder)),branch=Str(d,"branch");
                    var pending=new List<RepoEntry>();int duplicates=0;
                    foreach(string line in Str(d,"urls").Split(new[]{'\r','\n'},StringSplitOptions.RemoveEmptyEntries)){
                        var repo=Repo.Parse(line.Trim());var entry=new RepoEntry{Url="https://"+repo.Host+"/"+repo.Path,Branch=branch,Folder=folder,DownloadSelected=true};
                        if(config.Repositories.Concat(pending).Any(x=>x.Identity==entry.Identity)){duplicates++;continue;}pending.Add(entry);
                    }
                    if(pending.Count==0&&duplicates==0)throw new Exception("저장소 주소를 입력해 주세요.");config.Repositories.AddRange(pending);Save();activity=pending.Count+"개 등록 · 중복 "+duplicates+"개 건너뜀";break;
                case "/api/edit":
                    var edit=Entry(Str(d,"id"));var parsed=Repo.Parse(Str(d,"url"));string address="https://"+parsed.Host+"/"+parsed.Path;
                    var replacement=new RepoEntry{Url=address,Branch=Str(d,"branch"),Folder=Folder(Str(d,"folder")),DownloadSelected=edit.DownloadSelected};
                    if(config.Repositories.Any(x=>x!=edit&&x.Identity==replacement.Identity))throw new Exception("이미 등록된 저장소와 브랜치입니다.");
                    if(edit.Identity==replacement.Identity){edit.Folder=replacement.Folder;}else{replacement.Id=edit.Id;config.Repositories[config.Repositories.IndexOf(edit)]=replacement;}Save();break;
                case "/api/delete":config.Repositories.Remove(Entry(Str(d,"id")));Save();break;
                case "/api/select":foreach(var entry in config.Repositories)if(Str(d,"id")=="all"||entry.Id==Str(d,"id"))entry.DownloadSelected=Bool(d,"selected");Save();break;
                case "/api/sort":config.SortRecent=Bool(d,"recent");if(config.SortRecent)RepoOrder.Recent(config.Repositories);Save();break;
                case "/api/move":var moving=Entry(Str(d,"id"));var target=Entry(Str(d,"before"));RepoOrder.Move(config.Repositories,moving,config.Repositories.IndexOf(target)+(Bool(d,"after")?1:0));config.SortRecent=false;Save();break;
                case "/api/link":var linked=Entry(Str(d,"id"));string zip=Str(d,"path");if(!Path.IsPathRooted(zip)||!String.Equals(Path.GetExtension(zip),".zip",StringComparison.OrdinalIgnoreCase))throw new Exception("ZIP 파일을 선택해 주세요.");linked.DownloadedSha=ZipState.Identify(zip);linked.DownloadedPath=Path.GetFullPath(zip);linked.ImportedZip=true;Save();break;
                case "/api/run":bool download=Bool(d,"download");Queue(config.Repositories.Where(x=>!download||x.DownloadSelected).ToArray(),download);break;
                default:throw new Exception("지원하지 않는 요청입니다.");
            }
            return new{ok=true};
        }
    }
    void RegisterStartup(){using(var key=Microsoft.Win32.Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run")){if(config.StartWithWindows)key.SetValue("SimpleCommitWeb","\""+(Environment.GetEnvironmentVariable("SIMPLECOMMIT_DESKTOP_EXE")??Application.ExecutablePath)+"\" --tray");else key.DeleteValue("SimpleCommitWeb",false);}}
    string Pick(bool zip){string result="";Exception error=null;var thread=new Thread(()=>{try{if(zip){using(var dialog=new OpenFileDialog{Filter="ZIP 파일|*.zip",Title="기존 ZIP 연결"})if(dialog.ShowDialog()==DialogResult.OK)result=dialog.FileName;}else{using(var dialog=new FolderBrowserDialog{Description="ZIP 다운로드 폴더"})if(dialog.ShowDialog()==DialogResult.OK)result=dialog.SelectedPath;}}catch(Exception ex){error=ex;}});thread.SetApartmentState(ApartmentState.STA);thread.Start();thread.Join();if(error!=null)throw error;return result;}
    void Connect(RepoEntry e){
        if(File.Exists(e.DownloadedPath)&&e.DownloadedSha==e.LastSha&&e.LastSha!="")return;
        foreach(string dir in new[]{e.Folder,config.DefaultDownloadFolder,Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),"Downloads")}.Distinct(StringComparer.OrdinalIgnoreCase)){
            if(config.Repositories.Any(x=>x!=e&&x.Url!=e.Url&&String.Equals(x.Folder,dir,StringComparison.OrdinalIgnoreCase)&&Repo.Parse(x.Url).Name==Repo.Parse(e.Url).Name))continue;
            try{var found=ZipState.FindExisting(new RepoEntry{Url=e.Url,Branch=e.Branch,Folder=dir,LastSha=e.LastSha});if(found!=null&&(!File.Exists(e.DownloadedPath)||(found.Sha!=""&&e.LastSha.StartsWith(found.Sha,StringComparison.OrdinalIgnoreCase)))){e.DownloadedPath=found.Path;e.DownloadedSha=found.Sha;e.ImportedZip=true;}}catch(IOException){}catch(UnauthorizedAccessException){}
        }
    }
    void Queue(RepoEntry[] entries,bool download){if(busy||entries.Length==0)return;busy=true;activity=download?"ZIP 다운로드 준비 중":"커밋 확인 중";Task.Run(()=>Run(entries,download));}
    void Run(RepoEntry[] entries,bool download){int updated=0,failed=0,saved=0,skipped=0;try{foreach(var e in entries){try{
        var repo=Repo.Parse(e.Url);lock(gate){e.Status="확인 중";activity=repo.Name+" · 커밋 확인 중";}
        var info=Backend.Latest(repo,e.Branch);
        lock(gate){bool changed=e.LastSha!=""&&e.LastSha!=info.Sha;e.LastSha=info.Sha;e.Message=info.Message;e.CommitUtc=info.CommittedUtc;e.CheckedUtc=DateTime.UtcNow;e.Status=changed?"새 커밋 있음":"확인 완료";if(changed)updated++;Connect(e);if(download&&ZipState.CanSkip(e)){e.Status="최신 ZIP 있음 · 건너뜀";skipped++;continue;}}
        if(download){string branch=e.Branch;if(String.IsNullOrWhiteSpace(branch))branch=Backend.Branches(repo,CancellationToken.None).Default;string target=Backend.DownloadPath(repo,branch,e.Folder),previous=e.DownloadedPath;
            lock(gate){if(config.Repositories.Any(x=>x!=e&&!String.IsNullOrWhiteSpace(x.DownloadedPath)&&String.Equals(Path.GetFullPath(x.DownloadedPath),target,StringComparison.OrdinalIgnoreCase)))throw new Exception("다른 저장소와 파일명이 같습니다. 다운로드 폴더를 변경하세요.");e.Status="다운로드 중";}
            string path=Backend.Download(repo,info.Sha,e.Folder,n=>{lock(gate){activity=repo.Name+" · "+(n/1048576.0).ToString("0.0")+" MB";}},branch);
            lock(gate){e.DownloadedPath=path;e.DownloadedSha=info.Sha;e.ImportedZip=false;e.Status="ZIP 저장 완료";Save();saved++;if(!config.Repositories.Any(x=>x!=e&&String.Equals(x.DownloadedPath,previous,StringComparison.OrdinalIgnoreCase))){try{Backend.DeletePreviousZip(previous,path);}catch(Exception ex){e.Status="저장 완료 · 이전 ZIP 삭제 실패: "+ex.Message;}}}
        }
    }catch(Exception ex){lock(gate){failed++;e.Status="실패: "+ex.Message;}}finally{lock(gate){e.NextUtc=DateTime.UtcNow.AddMinutes(config.CheckMinutes);Save();}}}
    }catch(Exception ex){lock(gate){NoticeText("저장 실패: "+ex.Message);}}finally{lock(gate){busy=false;if(config.SortRecent)RepoOrder.Recent(config.Repositories);activity=download?"다운로드 완료 · 저장 "+saved+" · 최신 "+skipped+" · 실패 "+failed:"확인 완료 · 새 커밋 "+updated+" · 실패 "+failed;if(updated>0)NoticeText(updated+"개 저장소에 새 커밋이 있습니다.");if(failed>0)NoticeText(failed+"개 저장소에서 작업에 실패했습니다. 목록을 확인해 주세요.");if(download)NoticeText(activity);try{Save();}catch(Exception ex){NoticeText("설정 저장 실패: "+ex.Message);}}}}
    void Tick(){lock(gate){if(!busy)Queue(config.Repositories.Where(x=>x.NextUtc<=DateTime.UtcNow).ToArray(),false);}}
    void Open(){System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Origin){UseShellExecute=true});}
    [STAThread]public static void Main(string[] args){
        ServicePointManager.SecurityProtocol=SecurityProtocolType.Tls12;int port=17843;string dir=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"SimpleCommitWeb");
        for(int i=0;i<args.Length-1;i++){if(args[i]=="--data"){dir=Path.GetFullPath(args[++i]);}else if(args[i]=="--port")port=Int32.Parse(args[++i]);}
        bool created;using(var mutex=new Mutex(true,"Local\\SimpleCommitWeb-"+port,out created)){if(!created){System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("http://127.0.0.1:"+port){UseShellExecute=true});return;}
        try{Application.EnableVisualStyles();var host=new WebHost(dir,port);host.Start();if(args.Contains("--embedded")&&host.config.StartWithWindows)host.RegisterStartup();using(var stream=typeof(WebHost).Assembly.GetManifestResourceStream("app.ico"))using(var icon=new System.Drawing.Icon(stream))using(var tray=new NotifyIcon{Icon=icon,Text="SimpleCommit Web",Visible=!args.Contains("--embedded")})using(var timer=new System.Windows.Forms.Timer{Interval=15000}){
            var menu=new ContextMenuStrip();menu.Items.Add("웹 앱 열기",null,delegate{host.Open();});menu.Items.Add("종료",null,delegate{lock(host.gate){if(host.busy){MessageBox.Show("진행 중인 작업이 끝난 뒤 종료해 주세요.");return;}host.stopping=true;host.listener.Stop();Application.Exit();}});tray.ContextMenuStrip=menu;tray.DoubleClick+=delegate{host.Open();};timer.Tick+=delegate{string parent=Environment.GetEnvironmentVariable("SIMPLECOMMIT_PARENT_PID");if(parent!=null){try{if(System.Diagnostics.Process.GetProcessById(Int32.Parse(parent)).HasExited){Application.Exit();return;}}catch{Application.Exit();return;}}host.Tick();};timer.Start();host.Tick();if(!args.Contains("--tray")&&!args.Contains("--embedded"))host.Open();Application.Run();tray.Visible=false;}
        }catch(Exception ex){MessageBox.Show("웹 앱을 시작할 수 없습니다.\n"+ex.Message,"SimpleCommit Web");}}
    }
}
