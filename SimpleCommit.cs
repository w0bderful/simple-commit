using System;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Text;
using System.Drawing;
using System.Windows.Forms;
using System.Web.Script.Serialization;
using System.Collections.Generic;
using System.Threading.Tasks;
[assembly: System.Runtime.Versioning.TargetFramework(".NETFramework,Version=v4.7.2")]

public class Settings {
    public string DefaultDownloadFolder=Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
    public int CheckMinutes=180;
    public int NotificationSeconds=7;
    public bool SortRecent=false;
    public List<RepoEntry> Repositories = new List<RepoEntry>();
    public string Url = "", Branch = "", Folder = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
    public string LastSha = "", Key = "";
    public bool Watching = false;
    public bool StartWithWindows = true;
    public DateTime NextUtc = DateTime.MinValue;
    public void Migrate() {
        CheckMinutes=Math.Max(1,Math.Min(10080,CheckMinutes));Watching=true;
        NotificationSeconds=Math.Max(1,Math.Min(120,NotificationSeconds));
        if (Repositories == null) Repositories = new List<RepoEntry>();
        if (Repositories.Count == 0 && !String.IsNullOrWhiteSpace(Url))
            Repositories.Add(new RepoEntry { Url=Url, Branch=Branch ?? "", Folder=Folder, LastSha=LastSha ?? "", NextUtc=NextUtc });
        Url=""; Branch=""; LastSha=""; Key="";
    }
}
public static class RepoOrder {
    public static void Recent(List<RepoEntry> entries){
        var positions=new Dictionary<RepoEntry,int>();for(int i=0;i<entries.Count;i++)positions[entries[i]]=i;
        entries.Sort((a,b)=>{int order=b.CommitUtc.CompareTo(a.CommitUtc);return order!=0?order:positions[a].CompareTo(positions[b]);});
    }
    public static void Move(List<RepoEntry> entries,RepoEntry item,int boundary){int from=entries.IndexOf(item);if(from<0)return;boundary=Math.Max(0,Math.Min(entries.Count,boundary));entries.RemoveAt(from);if(boundary>from)boundary--;entries.Insert(boundary,item);}
}
public class RepoEntry {
    public string Id=Guid.NewGuid().ToString("N"), Url="", Branch="", Folder=Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
    public string LastSha="", Message="", Status="확인 대기";
    public DateTime CommitUtc=DateTime.MinValue, CheckedUtc=DateTime.MinValue, NextUtc=DateTime.MinValue;
    public string DownloadedSha="", DownloadedPath="";
    public bool ImportedZip=false;
    public bool DownloadSelected=false;
    public string Identity { get { var r=Repo.Parse(Url); return r.Host+"/"+r.Path+"#"+Branch; } }
}
public static class ZipState {
    public class Found { public string Path,Sha; }
    public static Found FindExisting(RepoEntry entry) {
        if(!Directory.Exists(entry.Folder))return null;
        string name=Repo.Parse(entry.Url).Name;
        var pattern=new System.Text.RegularExpressions.Regex("^"+System.Text.RegularExpressions.Regex.Escape(name)+"-([a-fA-F0-9]{8})-\\d{8}-\\d{6}(?:-\\d+)?\\.zip$",System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        Found recent=null;DateTime recentTime=DateTime.MinValue;
        foreach(string path in Directory.EnumerateFiles(entry.Folder,"*.zip",SearchOption.TopDirectoryOnly)) {
            var match=pattern.Match(System.IO.Path.GetFileName(path));
            try {
                string root=Root(path);var repo=Repo.Parse(entry.Url);
                string branch=entry.Branch.Replace('/','-');
                string owner=Uri.UnescapeDataString(repo.Path.Split('/')[0]);
                string escaped=System.Text.RegularExpressions.Regex.Escape(name);
                string suffix=branch==""?"(?:main|master|HEAD)":System.Text.RegularExpressions.Regex.Escape(branch);
                bool belongs=String.Equals(root,name,StringComparison.OrdinalIgnoreCase)||System.Text.RegularExpressions.Regex.IsMatch(root,"^(?:"+escaped+"|"+System.Text.RegularExpressions.Regex.Escape(owner+"-"+name)+")-(?:"+suffix+"|[a-fA-F0-9]{7,64}|.+-[a-fA-F0-9]{40,64})$",System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                string rootSha=Identify(path);
                if(!belongs && rootSha!="" && entry.LastSha!="" && entry.LastSha.StartsWith(rootSha,StringComparison.OrdinalIgnoreCase) && root.StartsWith(name+"-",StringComparison.OrdinalIgnoreCase))belongs=true;
                if(!belongs)continue;
                string fileSha=match.Success?match.Groups[1].Value:"";
                if(rootSha!="" && !rootSha.StartsWith(fileSha,StringComparison.OrdinalIgnoreCase) && !fileSha.StartsWith(rootSha,StringComparison.OrdinalIgnoreCase))continue;
                string sha=rootSha.Length>fileSha.Length?rootSha:fileSha;
                var found=new Found{Path=path,Sha=sha};
                if(sha!=""&&entry.LastSha!=""&&entry.LastSha.StartsWith(sha,StringComparison.OrdinalIgnoreCase))return found;
                DateTime modified=File.GetLastWriteTimeUtc(path);if(recent==null||modified>recentTime){recent=found;recentTime=modified;}
            }catch(InvalidDataException){}catch(IOException){}catch(UnauthorizedAccessException){}
        }return recent;
    }
    public static bool CanSkip(RepoEntry e) {
        if(e.LastSha==""||String.IsNullOrEmpty(e.DownloadedSha)||e.DownloadedSha.Length<7||!e.LastSha.StartsWith(e.DownloadedSha,StringComparison.OrdinalIgnoreCase)||!File.Exists(e.DownloadedPath))return false;
        try{
            string archiveSha=Identify(e.DownloadedPath);
            return archiveSha=="" || e.LastSha.StartsWith(archiveSha,StringComparison.OrdinalIgnoreCase);
        }catch{return false;}
    }
    public static string Describe(RepoEntry e, bool exists) {
        if(String.IsNullOrEmpty(e.DownloadedPath)) return "다운로드 안 함";
        if(!exists) return "ZIP 파일 없음";
        if(String.IsNullOrEmpty(e.DownloadedSha)) return "기존 ZIP · 버전 미확인";
        if(String.IsNullOrEmpty(e.LastSha)) return "원격 확인 필요";
        bool matches=e.LastSha.StartsWith(e.DownloadedSha,StringComparison.OrdinalIgnoreCase);
        return matches ? (e.ImportedZip ? "최신 추정 · 연결 ZIP" : "최신 ZIP") : "ZIP 업데이트 필요";
    }
    public static string Root(string path) {
        using(var zip=ZipFile.OpenRead(path)) {
            if(zip.Entries.Count==0) throw new InvalidDataException("빈 ZIP 파일입니다.");
            string root=null;
            foreach(var entry in zip.Entries) {
                string current=entry.FullName.Split('/')[0];
                if(root==null) root=current;
                else if(current!=root) return "";
            }
            return root??"";
        }
    }
    public static string Identify(string path) {
        string root=Root(path);
        if(root=="")return "";
        // git archive commonly stores its full commit ID in the ZIP end comment.
        using(var file=File.OpenRead(path)){
            int length=(int)Math.Min(file.Length,65557);byte[] tail=new byte[length];file.Seek(-length,SeekOrigin.End);int read=0;
            while(read<length){int n=file.Read(tail,read,length-read);if(n==0)break;read+=n;}
            for(int i=length-22;i>=0;i--)if(tail[i]==0x50&&tail[i+1]==0x4b&&tail[i+2]==5&&tail[i+3]==6){
                int size=tail[i+20]+256*tail[i+21];if(i+22+size!=length)continue;
                string comment=Encoding.ASCII.GetString(tail,i+22,size).Trim();
                if(System.Text.RegularExpressions.Regex.IsMatch(comment,"^[a-fA-F0-9]{40,64}$"))return comment;
                break;
            }
        }
        var match=System.Text.RegularExpressions.Regex.Match(root,"-([a-fA-F0-9]{7,64})$");return match.Success?match.Groups[1].Value:"";
    }
}
public static class RelativeTime {
    public static string Format(DateTime timestamp, DateTime now) {
        if(timestamp==DateTime.MinValue) return "—";
        var span=now.ToUniversalTime()-timestamp.ToUniversalTime();
        if(span.TotalSeconds < -60) return "미래 시각";
        if(span.TotalMinutes < 1) return "방금 전";
        if(span.TotalHours < 1) return ((int)span.TotalMinutes)+"분 전";
        if(span.TotalDays < 1) return ((int)span.TotalHours)+"시간 "+span.Minutes+"분 전";
        return ((int)span.TotalDays)+"일 전";
    }
}
public class Repo {
    public string Host, Path;
    public static Repo Parse(string value) {
        Uri u;
        if (!Uri.TryCreate(value.Trim(), UriKind.Absolute, out u) || u.Scheme != "https" || !u.IsDefaultPort || u.UserInfo != "" || (u.Host != "github.com" && u.Host != "gitgud.io" && u.Host != "codeberg.org" && u.Host != "gitlab.com"))
            throw new Exception("GitHub, GitGud, Codeberg 또는 GitLab.com의 https 저장소 주소를 입력해 주세요.");
        string p = u.AbsolutePath.Trim('/');
        if (p.EndsWith(".git")) p = p.Substring(0, p.Length - 4);
        string[] parts = p.Split('/');
        if (parts.Length < 2 || ((u.Host == "github.com" || u.Host=="codeberg.org") && parts.Length != 2) || p.Contains("/-/") || u.Query != "" || u.Fragment != "")
            throw new Exception("파일·커밋 페이지가 아닌 저장소의 첫 화면 주소를 입력해 주세요.");
        return new Repo { Host = u.Host, Path = p };
    }
    public bool GitLabApi { get { return Host=="gitgud.io" || Host=="gitlab.com"; } }
    public string Provider { get {return Host=="github.com"?"GitHub":Host=="codeberg.org"?"Codeberg":Host=="gitlab.com"?"GitLab":"GitGud";} }
    public string Api { get { return Host == "github.com" ? "https://api.github.com/repos/" + Path : Host=="codeberg.org"?"https://codeberg.org/api/v1/repos/"+Path:"https://"+Host+"/api/v4/projects/" + Uri.EscapeDataString(Uri.UnescapeDataString(Path)); } }
    public string Name { get { string n = Uri.UnescapeDataString(Path.Substring(Path.LastIndexOf('/') + 1)); foreach(char c in System.IO.Path.GetInvalidFileNameChars()) n = n.Replace(c, '_'); return n; } }
    public string CommitUrl(string branch) { return Api + (Host == "codeberg.org"?"/commits?limit=1&stat=false&verification=false&files=false"+(branch==""?"":"&sha="+Uri.EscapeDataString(branch)):Host == "github.com" ? "/commits?per_page=1" + (branch == "" ? "" : "&sha=" + Uri.EscapeDataString(branch)) : "/repository/commits?per_page=1" + (branch == "" ? "" : "&ref_name=" + Uri.EscapeDataString(branch))); }
    public string ZipUrl(string sha) { return Api + (Host == "codeberg.org"?"/archive/"+sha+".zip":Host == "github.com" ? "/zipball/" + sha : "/repository/archive.zip?sha=" + sha); }
}
public class CommitInfo { public string Sha, Message; public DateTime CommittedUtc; }
public class BranchList { public string Default=""; public List<string> Names=new List<string>(); }
public static class Backend {
    public static BranchList Branches(Repo repo, System.Threading.CancellationToken token) {
        return ReadBranches(repo, address=>{
            token.ThrowIfCancellationRequested();var request=Request(address);
            using(token.Register(()=>request.Abort()))
            using(var response=request.GetResponse())
            using(var reader=new StreamReader(response.GetResponseStream())) {var text=reader.ReadToEnd();token.ThrowIfCancellationRequested();return text;}
        });
    }
    public static BranchList ReadBranches(Repo repo, Func<string,string> fetch) {
        var json=new JavaScriptSerializer();var result=new BranchList();
        var metadata=json.Deserialize<Dictionary<string,object>>(fetch(repo.Api));object value;
        if(metadata.TryGetValue("default_branch",out value))result.Default=Convert.ToString(value);
        for(int page=1;;page++) {
            string address=repo.Api+(repo.GitLabApi?"/repository/branches":"/branches")+(repo.Host=="codeberg.org"?"?limit=100&page=":"?per_page=100&page=")+page;
            var rows=json.Deserialize<List<Dictionary<string,object>>>(fetch(address));
            int before=result.Names.Count;
            foreach(var row in rows){string name=Convert.ToString(row["name"]);if(!result.Names.Contains(name))result.Names.Add(name);}
            // Servers can impose a page limit smaller than requested.
            if(rows.Count==0 || result.Names.Count==before)break;
        }
        result.Names.Sort(StringComparer.Ordinal);return result;
    }
    public static HttpWebRequest Request(string url) {
        var r = (HttpWebRequest)WebRequest.Create(url); r.UserAgent = "SimpleCommit/1.0"; r.Timeout = 30000; r.ReadWriteTimeout = 30000; r.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate; return r;
    }
    public static CommitInfo Decode(string json, bool github) {
        var rows = new JavaScriptSerializer().Deserialize<List<Dictionary<string, object>>>(json);
        if (rows == null || rows.Count == 0) throw new Exception("이 브랜치에는 커밋이 없습니다.");
        var row = rows[0];
        string sha = Convert.ToString(row[github ? "sha" : "id"]);
        if (!System.Text.RegularExpressions.Regex.IsMatch(sha, "^[a-fA-F0-9]{40,64}$")) throw new Exception("서버의 커밋 정보가 올바르지 않습니다.");
        var commit = github ? (Dictionary<string, object>)row["commit"] : row;
        object rawDate=null;
        if(github) { object committer; if(commit.TryGetValue("committer",out committer) && committer is Dictionary<string,object>) ((Dictionary<string,object>)committer).TryGetValue("date",out rawDate); }
        else row.TryGetValue("committed_date",out rawDate);
        DateTimeOffset parsed;
        DateTime date=DateTime.MinValue;
        if(rawDate is DateTime) date=((DateTime)rawDate).ToUniversalTime();
        else if(DateTimeOffset.TryParse(Convert.ToString(rawDate),System.Globalization.CultureInfo.InvariantCulture,System.Globalization.DateTimeStyles.AssumeUniversal,out parsed)) date=parsed.UtcDateTime;
        return new CommitInfo { Sha = sha, Message = Convert.ToString(github ? commit["message"] : row["title"]).Split('\n')[0], CommittedUtc=date };
    }
    public static CommitInfo Latest(Repo repo, string branch) {
        using (var response = Request(repo.CommitUrl(branch)).GetResponse())
        using (var reader = new StreamReader(response.GetResponseStream())) return Decode(reader.ReadToEnd(), !repo.GitLabApi);
    }
    public static string Download(Repo repo, string sha, string folder, Action<long> progress) {
        Directory.CreateDirectory(folder);
        string temp = System.IO.Path.Combine(folder, ".simplecommit-" + Guid.NewGuid().ToString("N") + ".part");
        try {
            using (var response = Request(repo.ZipUrl(sha)).GetResponse())
            using (var input = response.GetResponseStream())
            using (var output = new FileStream(temp, FileMode.CreateNew)) {
                byte[] b = new byte[65536]; int count; long total = 0; DateTime last = DateTime.MinValue;
                while ((count = input.Read(b, 0, b.Length)) > 0) { output.Write(b, 0, count); total += count; if ((DateTime.UtcNow-last).TotalMilliseconds > 300) { progress(total); last = DateTime.UtcNow; } }
            }
            using (var zip = ZipFile.OpenRead(temp)) { if (zip.Entries.Count == 0) throw new Exception("다운로드된 ZIP이 비어 있습니다."); }
            string name = repo.Name + "-" + sha.Substring(0, 8) + "-" + DateTime.Now.ToString("yyyyMMdd-HHmmss");
            string path = System.IO.Path.Combine(folder, name + ".zip"); int i = 2;
            while (File.Exists(path)) path = System.IO.Path.Combine(folder, name + "-" + (i++) + ".zip");
            File.Move(temp, path); return path;
        } finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}
