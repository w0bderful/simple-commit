using System;
using System.IO;
using System.IO.Compression;
using System.Web.Script.Serialization;
public static class Tests {
    static int count;
    static void Check(bool b,string message){count++;if(!b)throw new Exception(message);}
    public static void Run(){
        var now=new DateTime(2026,9,20,12,0,0,DateTimeKind.Utc);
        var older=new RepoEntry{Url="https://github.com/a/old",CommitUtc=now.AddDays(-1),DownloadSelected=true};var latest=new RepoEntry{Url="https://github.com/a/new",CommitUtc=now};var unknown=new RepoEntry{Url="https://github.com/a/unknown"};
        var ordering=new System.Collections.Generic.List<RepoEntry>{unknown,older,latest};RepoOrder.Recent(ordering);Check(ordering[0]==latest&&ordering[2]==unknown,"recent commits first, unknown dates last");
        RepoOrder.Move(ordering,latest,3);Check(ordering[2]==latest&&ordering[0]==older&&older.DownloadSelected,"drag downward preserves checks");
        RepoOrder.Move(ordering,latest,0);Check(ordering[0]==latest,"drag upward");RepoOrder.Move(ordering,latest,1);Check(ordering[0]==latest,"drop after self is no-op");
        var orderSaved=new JavaScriptSerializer().Deserialize<Settings>(new JavaScriptSerializer().Serialize(new Settings{Repositories=ordering,SortRecent=true}));Check(orderSaved.SortRecent&&orderSaved.Repositories[0].Id==latest.Id&&orderSaved.Repositories[1].DownloadSelected,"sorting and row order persist");
        var batch=BulkInput.Parse(" https://github.com/a/b.git/ \r\nhttps://github.com/a/b\n\nhttps://gitlab.com/a/sub/b\nhttps://codeberg.org/a/b\nhttps://gitgud.io/a/b");
        Check(batch.Urls.Count==4&&batch.Duplicates==1&&batch.Errors.Count==0,"bulk URLs normalize duplicates and retain all four providers");
        var invalid=BulkInput.Parse("https://github.com/a/b\nwrong\nhttps://github.com/a/b/tree/main");Check(invalid.Errors.Count==2&&invalid.Errors[0].StartsWith("2행")&&invalid.Urls.Count==1,"bulk invalid line feedback");
        Check(BulkInput.Parse(" \n\r\n").Urls.Count==0,"empty bulk input");
        Check(RelativeTime.Format(DateTime.MinValue,now)=="—","unknown time");
        Check(RelativeTime.Format(now.AddSeconds(-59),now)=="방금 전","seconds");
        Check(RelativeTime.Format(now.AddMinutes(-7),now)=="7분 전","minutes");
        Check(RelativeTime.Format(now.AddMinutes(-125),now)=="2시간 5분 전","hours");
        Check(RelativeTime.Format(now.AddDays(-3),now)=="3일 전","days");
        Check(RelativeTime.Format(now.AddMinutes(5),now)=="미래 시각","future");
        string sha=new string('a',40);
        var c=Backend.Decode("[{\"sha\":\""+sha+"\",\"commit\":{\"message\":\"hello\\nbody\",\"author\":{\"date\":\"2020-01-01T00:00:00Z\"},\"committer\":{\"date\":\"2026-09-20T20:53:00+09:00\"}}}]",true);
        Check(c.Message=="hello"&&c.CommittedUtc==now.AddMinutes(-7),"GitHub uses committer date, correct timezone");
        c=Backend.Decode("[{\"id\":\""+sha+"\",\"title\":\"test\",\"committed_date\":\"2026-09-20T11:53:00Z\"}]",false);
        Check(c.CommittedUtc==now.AddMinutes(-7),"GitGud date");
        var js=new JavaScriptSerializer();var legacy=js.Deserialize<Settings>("{\"Url\":\"https://github.com/a/b\",\"Branch\":\"main\",\"Folder\":\"C:\\\\Downloads\",\"LastSha\":\""+sha+"\",\"Watching\":true}");legacy.Migrate();legacy.Migrate();
        foreach(string host in new[]{"github.com","gitgud.io","codeberg.org","gitlab.com"}){
            var repo=Repo.Parse("https://"+host+"/a/b");int calls=0;
            var result=Backend.ReadBranches(repo,address=>{calls++;if(address==repo.Api)return "{\"default_branch\":\"main\"}";
                Check(address.Contains(repo.GitLabApi?"/repository/branches?":"/branches?"),"branch endpoint");
                Check(address.Contains(host=="codeberg.org"?"?limit=100":"?per_page=100"),"provider pagination parameter");
                if(address.EndsWith("page=1")){var rows=new System.Collections.Generic.List<object>();for(int i=0;i<100;i++)rows.Add(new{name="branch-"+i});return js.Serialize(rows);}
                return address.EndsWith("page=2")?"[{\"name\":\"main\"},{\"name\":\"feature/a\"}]":"[]";});
            Check(result.Names.Count==102&&result.Default=="main"&&result.Names.Contains("feature/a")&&calls==4,"all branch pages and default");
        }
        Check(legacy.Repositories.Count==1&&legacy.Repositories[0].LastSha==sha&&legacy.Watching,"legacy migration once preserves monitoring");
        legacy.Repositories.Add(new RepoEntry{Url="https://gitgud.io/a/b",DownloadedSha=sha,DownloadedPath="test.zip",CommitUtc=now,DownloadSelected=true});
        var saved=js.Deserialize<Settings>(js.Serialize(legacy));saved.Migrate();
        Check(saved.Repositories.Count==2&&saved.Repositories[1].CommitUtc==now&&saved.Repositories[1].DownloadedSha==sha,"multi-repo persistence");
        Check(!saved.Repositories[0].DownloadSelected&&saved.Repositories[1].DownloadSelected,"selected and unselected repositories survive restart");
        var e=new RepoEntry{LastSha=sha};Check(ZipState.Describe(e,false)=="다운로드 안 함","no zip");e.DownloadedPath="x";e.DownloadedSha=sha;
        Check(ZipState.Describe(e,true)=="최신 ZIP","up-to-date");Check(ZipState.Describe(e,false)=="ZIP 파일 없음","missing zip");e.DownloadedSha=new string('b',40);Check(ZipState.Describe(e,true)=="ZIP 업데이트 필요","outdated");e.ImportedZip=true;e.DownloadedSha=sha.Substring(0,7);Check(ZipState.Describe(e,true)=="최신 추정 · 연결 ZIP","import is explicitly inferred");e.LastSha="";Check(ZipState.Describe(e,true)=="원격 확인 필요","unknown remote");e.DownloadedSha="";Check(ZipState.Describe(e,true)=="기존 ZIP · 버전 미확인","unknown zip");
        string path=Path.Combine(Path.GetTempPath(),"simplecommit-test-"+Guid.NewGuid()+".zip");
        try{using(var z=ZipFile.Open(path,ZipArchiveMode.Create)){z.CreateEntry("owner-repo-abcdef1/README");z.CreateEntry("owner-repo-abcdef1/src/app");}Check(ZipState.Identify(path)=="abcdef1","GitHub ZIP import");}finally{if(File.Exists(path))File.Delete(path);}
        Check(Repo.Parse("https://github.com/a/b.git/").Path=="a/b","URL normalization");
        Check(Backend.Request(Repo.Parse("https://gitgud.io/group/sub/repo").CommitUrl("")).RequestUri.AbsoluteUri.Contains("group%2Fsub%2Frepo"),"GitGud slash encoding");
        Check(Repo.Parse("https://github.com/a/b").CommitUrl("feature/x").EndsWith("sha=feature%2Fx"),"branch encoding");
        var cb=Repo.Parse("https://codeberg.org/a/b.git");Check(cb.Api=="https://codeberg.org/api/v1/repos/a/b"&&cb.Provider=="Codeberg","Codeberg API");
        Check(cb.CommitUrl("feature/x").Contains("limit=1")&&cb.CommitUrl("feature/x").EndsWith("sha=feature%2Fx"),"Codeberg branch commits");
        Check(cb.ZipUrl(sha).EndsWith("/archive/"+sha+".zip"),"Codeberg commit-pinned ZIP");
        var gl=Repo.Parse("https://gitlab.com/group/sub/project.git");Check(gl.Api=="https://gitlab.com/api/v4/projects/group%2Fsub%2Fproject"&&gl.GitLabApi,"GitLab subgroup API");
        Check(gl.CommitUrl("feature/x").EndsWith("ref_name=feature%2Fx")&&gl.ZipUrl(sha).EndsWith("archive.zip?sha="+sha),"GitLab branch and ZIP URLs");
        var options=new Settings{DefaultDownloadFolder="C:\\CustomDownloads"};var restored=js.Deserialize<Settings>(js.Serialize(options));
        var timing=new Settings{CheckMinutes=25,Watching=false};timing.Migrate();var timingRestored=js.Deserialize<Settings>(js.Serialize(timing));Check(timingRestored.CheckMinutes==25&&timingRestored.Watching,"interval persists and monitoring always enabled");
        timing.CheckMinutes=0;timing.Migrate();Check(timing.CheckMinutes==1,"interval lower bound");
        timing.NotificationSeconds=12;var noticeSettings=js.Deserialize<Settings>(js.Serialize(timing));Check(noticeSettings.NotificationSeconds==12,"notification duration persists");noticeSettings.NotificationSeconds=0;noticeSettings.Migrate();Check(noticeSettings.NotificationSeconds==1,"notification minimum");
        Check(restored.DefaultDownloadFolder==options.DefaultDownloadFolder,"default folder persistence");
        using(var d=new RepoDialog(null,restored.DefaultDownloadFolder))Check(d.Folder==options.DefaultDownloadFolder,"new repository uses default folder");
        using(var d=new RepoDialog(new RepoEntry{Folder="C:\\Existing"},restored.DefaultDownloadFolder))Check(d.Folder=="C:\\Existing","existing folder stays unchanged");
        string scanDir=Path.Combine(Path.GetTempPath(),"simplecommit-scan-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(scanDir);
        try{
            string good=Path.Combine(scanDir,"repo-aaaaaaaa-20260920-120000.zip"),old=Path.Combine(scanDir,"repo-bbbbbbbb-20260920-130000.zip"),broken=Path.Combine(scanDir,"repo-aaaaaaaa-20260920-140000.zip");
            using(var z=ZipFile.Open(good,ZipArchiveMode.Create))z.CreateEntry("owner-repo-aaaaaaa/readme");
            using(var z=ZipFile.Open(old,ZipArchiveMode.Create))z.CreateEntry("owner-repo-bbbbbbb/readme");
            File.WriteAllText(broken,"not a zip");
            var candidate=new RepoEntry{Url="https://github.com/owner/repo",Folder=scanDir,LastSha=sha};
            var found=ZipState.FindExisting(candidate);Check(found!=null&&found.Path==good,"find current ZIP and ignore corrupt archives");
            candidate.DownloadedPath=good;candidate.DownloadedSha=sha;Check(ZipState.CanSkip(candidate),"known current valid ZIP is skipped");
            candidate.ImportedZip=true;Check(ZipState.CanSkip(candidate),"matching imported ZIP is skipped");
            candidate.DownloadedSha=sha.Substring(0,8);Check(ZipState.CanSkip(candidate),"short imported commit is skipped");
            candidate.LastSha=new string('b',40);Check(!ZipState.CanSkip(candidate),"new remote commit downloads");candidate.LastSha=sha;
            candidate.DownloadedSha="";Check(!ZipState.CanSkip(candidate),"unknown ZIP version downloads");candidate.DownloadedSha=sha;
            candidate.ImportedZip=false;candidate.DownloadedPath=broken;Check(!ZipState.CanSkip(candidate),"broken ZIP is re-downloaded");
        }finally{foreach(var file in Directory.GetFiles(scanDir))File.Delete(file);Directory.Delete(scanDir);}
        string nativeDir=Path.Combine(Path.GetTempPath(),"simplecommit-native-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(nativeDir);
        try{
            string native=Path.Combine(nativeDir,"repo-main.zip");using(var z=ZipFile.Open(native,ZipArchiveMode.Create))z.CreateEntry("repo-main/readme");
            var entry=new RepoEntry{Url="https://github.com/owner/repo",Folder=nativeDir,LastSha=sha};
            var found=ZipState.FindExisting(entry);Check(found!=null&&found.Sha=="","native branch ZIP auto connects without claiming a version");
            using(var file=new FileStream(native,FileMode.Open,FileAccess.ReadWrite)){file.Seek(-2,SeekOrigin.End);file.WriteByte(40);file.WriteByte(0);var bytes=System.Text.Encoding.ASCII.GetBytes(sha);file.Write(bytes,0,bytes.Length);}
            Check(ZipState.Identify(native)==sha,"commit ID from git archive comment");Check(ZipState.FindExisting(entry).Sha==sha,"native ZIP full commit identification");
            var other=new RepoEntry{Url="https://github.com/owner/different",Folder=nativeDir};Check(ZipState.FindExisting(other)==null,"unrelated repository ZIP not connected");
        }finally{foreach(var file in Directory.GetFiles(nativeDir))File.Delete(file);Directory.Delete(nativeDir);}
        Console.WriteLine("PASS: "+count+" checks — migration, multi-repo persistence, timestamps/timezones, ZIP comparison/import, URLs");
    }
}
