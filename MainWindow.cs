using System;
using System.IO;
using System.Net;
using System.Text;
using System.Drawing;
using System.Windows.Forms;
using System.Web.Script.Serialization;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

public static class AppTheme {
    public static string Mode="system";
    public static bool Dark {get{if(Mode!="system")return Mode=="dark";try{return Convert.ToInt32(Microsoft.Win32.Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize","AppsUseLightTheme",1))==0;}catch{return false;}}}
    public static Color Background {get{return Dark?Color.FromArgb(20,23,29):Color.White;}}
    public static Color Surface {get{return Dark?Color.FromArgb(35,41,51):Color.White;}}
    public static Color Foreground {get{return Dark?Color.FromArgb(232,234,238):Color.FromArgb(35,45,60);}}
    public static void Apply(Control root){
        root.BackColor=Background;root.ForeColor=Foreground;
        var page=root as TabPage;if(page!=null)page.UseVisualStyleBackColor=false;
        var button=root as Button;if(button!=null){button.UseVisualStyleBackColor=false;button.FlatStyle=FlatStyle.Flat;button.FlatAppearance.BorderColor=Dark?Color.FromArgb(114,131,154):Color.Silver;button.BackColor=Dark?Color.FromArgb(53,64,80):Color.FromArgb(245,246,248);}
        if(button!=null){button.FlatAppearance.MouseOverBackColor=Dark?Color.FromArgb(72,89,112):Color.FromArgb(226,234,245);button.FlatAppearance.MouseDownBackColor=Dark?Color.FromArgb(43,91,145):Color.FromArgb(205,222,245);}
        if(root is ListView||root is TextBoxBase||root is ComboBox||root is NumericUpDown)root.BackColor=Surface;
        var check=root as CheckBox;if(check!=null)check.UseVisualStyleBackColor=false;
        foreach(Control child in root.Controls)Apply(child);
        var form=root as Form;if(form!=null&&form.IsHandleCreated){try{int dark=Dark?1:0;DwmSetWindowAttribute(form.Handle,20,ref dark,4);}catch{}}
    }
    [System.Runtime.InteropServices.DllImport("dwmapi.dll")]static extern int DwmSetWindowAttribute(IntPtr handle,int attribute,ref int value,int size);
}
public static class AppVisual {
    public static Icon Load(int size){using(var s=typeof(AppVisual).Assembly.GetManifestResourceStream("app.ico")){return s==null?(Icon)SystemIcons.Information.Clone():new Icon(s,size,size);}}
}
public class RepoDialog : Form {
    TextBox url=new TextBox(), folder=new TextBox(); ComboBox branch=new ComboBox();
    Label branchState=new Label(); Button retry=new Button();
    System.Threading.CancellationTokenSource branchRequest;
    class Choice { public string Name,Label; public override string ToString(){return Label;} }
    public string RepoUrl {get{return url.Text.Trim();}}
    public string Branch {get{var selected=branch.SelectedItem as Choice;return selected!=null?selected.Name:branch.Text.Trim();}}
    public string Folder {get{return folder.Text;}}
    public RepoDialog(RepoEntry entry,string defaultFolder=null) { Shown+=delegate{AppTheme.Apply(this);};
        Icon=AppVisual.Load(32);
        Text=entry==null ? "저장소 추가" : "저장소 수정"; Font=new Font("맑은 고딕",10); ClientSize=new Size(560,335); FormBorderStyle=FormBorderStyle.FixedDialog; MaximizeBox=false; MinimizeBox=false; StartPosition=FormStartPosition.CenterParent;
        Add(new Label{Text="저장소 주소 (GitHub · GitGud · Codeberg · GitLab)"},20,16,520,25); Add(url,20,43,520,28);
        Add(new Label{Text="브랜치"},20,86,95,25); Add(branch,115,81,319,28);branch.DropDownStyle=ComboBoxStyle.DropDown;branch.DropDownWidth=420;branch.MaxDropDownItems=12;branch.IntegralHeight=false;
        retry.Text="새로고침";Add(retry,446,79,94,32);
        Add(branchState,20,120,520,42);branchState.Font=new Font("맑은 고딕",9);branchState.Text="링크를 입력하면 브랜치를 자동으로 불러옵니다.";
        Add(new Label{Text="ZIP 저장 폴더"},20,175,400,25); Add(folder,20,203,418,28); folder.ReadOnly=true;
        var browse=new Button{Text="폴더 선택"}; Add(browse,446,201,94,32);
        browse.Click+=delegate {using(var d=new FolderBrowserDialog{SelectedPath=folder.Text}) if(d.ShowDialog(this)==DialogResult.OK) folder.Text=d.SelectedPath;};
        var save=new Button{Text="저장"}; Add(save,324,275,102,36); var cancel=new Button{Text="취소",DialogResult=DialogResult.Cancel}; Add(cancel,438,275,102,36); CancelButton=cancel; AcceptButton=save;
        url.Text=entry==null?"":entry.Url; branch.Text=entry==null?"":entry.Branch; folder.Text=entry==null?(defaultFolder??Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)):entry.Folder;
        url.TextChanged+=async delegate{await LoadBranches(true,true);};
        retry.Click+=async delegate{await LoadBranches(false,false);};
        Shown+=async delegate{if(RepoUrl!="")await LoadBranches(false,false);};
        FormClosed+=delegate{if(branchRequest!=null){branchRequest.Cancel();branchRequest.Dispose();branchRequest=null;}};
        save.Click+=delegate {try {Repo.Parse(RepoUrl); if(String.IsNullOrWhiteSpace(Folder)) throw new Exception("저장 폴더를 선택해 주세요."); DialogResult=DialogResult.OK;} catch(Exception e){MessageBox.Show(this,e.Message,"주소 확인");}};
    }
    async Task LoadBranches(bool debounce,bool changedUrl) {
        if(branchRequest!=null){branchRequest.Cancel();branchRequest.Dispose();}
        var current=new System.Threading.CancellationTokenSource();branchRequest=current;var token=current.Token;
        string retained=changedUrl?"":Branch;
        if(changedUrl){branch.Items.Clear();branch.Text="";}
        Repo repo;try{repo=Repo.Parse(RepoUrl);}catch{branchState.Text="지원하는 저장소의 첫 화면 링크를 입력해 주세요.";return;}
        branchState.Text="브랜치 목록을 불러오는 중…";
        try{
            if(debounce)await Task.Delay(650,token);
            var result=await Task.Run(()=>Backend.Branches(repo,token),token);
            if(token.IsCancellationRequested||IsDisposed)return;
            // Preserve a user's explicit selection or typing while the request was in flight.
            string choice=Branch!=""?Branch:retained;
            branch.BeginUpdate();branch.Items.Clear();
            branch.Items.Add(new Choice{Name="",Label=result.Default==""?"기본 브랜치 (자동)":"기본 브랜치 · "+result.Default});
            foreach(string name in result.Names)branch.Items.Add(new Choice{Name=name,Label=name+(name==result.Default?" (기본)":"")});
            branch.SelectedIndex=0;
            if(choice!=""){var match=branch.Items.Cast<Choice>().FirstOrDefault(x=>x.Name==choice);if(match!=null)branch.SelectedItem=match;else{branch.SelectedIndex=-1;branch.Text=choice;}}
            branch.EndUpdate();branchState.Text=result.Names.Count==0?"브랜치가 없습니다. 빈 저장소인지 확인해 주세요.":result.Names.Count+"개 브랜치 · 목록에서 선택하거나 직접 입력할 수 있습니다.";
        }catch(Exception ex){if(token.IsCancellationRequested||IsDisposed)return;var web=ex as WebException;var response=web==null?null:web.Response as HttpWebResponse;int code=response==null?0:(int)response.StatusCode;if(response!=null)response.Dispose();branchState.Text=code==404?"저장소를 찾지 못했습니다. 공개 저장소 주소를 확인해 주세요.":code==403||code==429?"요청 한도 또는 접근 제한입니다. 새로고침하거나 직접 입력하세요.":"목록을 불러오지 못했습니다. 새로고침하거나 직접 입력하세요.";}
    }
    void Add(Control c,int x,int y,int w,int h){c.SetBounds(x,y,w,h);Controls.Add(c);}
    public static void TestLive(string screenshot){
        var dialog=new RepoDialog(new RepoEntry{Url="https://github.com/octocat/Hello-World",Branch="test"});
        dialog.Shown+=async delegate{try{
            await dialog.LoadBranches(false,false);
            if(dialog.branch.Items.Count<2||dialog.Branch!="test")throw new Exception("Branch list / saved selection failed: "+dialog.branchState.Text);
            using(var bmp=new Bitmap(dialog.Width,dialog.Height)){dialog.DrawToBitmap(bmp,new Rectangle(0,0,dialog.Width,dialog.Height));bmp.Save(screenshot);}
            dialog.branch.SelectedIndex=0;if(dialog.Branch!="")throw new Exception("Default tracking failed");
            Console.WriteLine("PASS: auto branch UI, preserved existing selection, default tracking, rendered dialog");
        }catch(Exception e){Console.WriteLine(e);Environment.ExitCode=1;}finally{dialog.Close();}};Application.Run(dialog);
    }
}
public class MainWindow : Form {
    class RowOrder:System.Collections.IComparer {
        Dictionary<RepoEntry,int> order=new Dictionary<RepoEntry,int>();
        public RowOrder(List<RepoEntry> entries){for(int i=0;i<entries.Count;i++)order[entries[i]]=i;}
        public int Compare(object a,object b){int left,right;if(!order.TryGetValue((RepoEntry)((ListViewItem)a).Tag,out left))left=Int32.MaxValue;if(!order.TryGetValue((RepoEntry)((ListViewItem)b).Tag,out right))right=Int32.MaxValue;return left.CompareTo(right);}
    }
    static bool testing;
    Settings config; bool busy,exiting,refreshing,dragging;
    DateTime lastDragScroll=DateTime.MinValue;
    ListView list=new ListView(); Label state=new Label(),details=new Label(),schedule=new Label();
    Button add=new Button(),edit=new Button(),remove=new Button(),check=new Button(),download=new Button(),link=new Button();
    NumericUpDown interval=new NumericUpDown();
    Button selectAll=new Button(),selectNone=new Button(),defaultFolder=new Button(),bulkAdd=new Button(),sortRecent=new Button(); Label selectedCount=new Label();
    CheckBox startup=new CheckBox(); NotifyIcon tray=new NotifyIcon(); Timer timer=new Timer();
    TabControl tabs=new TabControl();TabPage repositoriesTab=new TabPage("저장소"),settingsTab=new TabPage("설정");
    NumericUpDown noticeSeconds=new NumericUpDown();Label folderSetting=new Label(),linkSetting=new Label(),settingsStatus=new Label();ToastWindow notification;
    bool storageReadFailed;
    string configFile=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"SimpleCommit","settings.json");
    public MainWindow(bool inTray) {
        Icon=AppVisual.Load(32);
        Text="커밋 알리미"; Font=new Font("맑은 고딕",10); ClientSize=new Size(1120,650); MinimumSize=new Size(1136,630); BackColor=Color.White; StartPosition=FormStartPosition.CenterScreen; AutoScaleMode=AutoScaleMode.Dpi;
        tabs.DrawMode=TabDrawMode.OwnerDrawFixed;tabs.DrawItem+=delegate(object sender,DrawItemEventArgs e){bool active=e.Index==tabs.SelectedIndex;Color fill=AppTheme.Dark?(active?Color.FromArgb(53,74,101):Color.FromArgb(32,38,48)):(active?Color.White:Color.FromArgb(230,234,240));using(var brush=new SolidBrush(fill))e.Graphics.FillRectangle(brush,e.Bounds);TextRenderer.DrawText(e.Graphics,tabs.TabPages[e.Index].Text,Font,e.Bounds,AppTheme.Foreground,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter);if(active)using(var pen=new Pen(AppTheme.Dark?Color.FromArgb(103,176,255):Color.RoyalBlue,3))e.Graphics.DrawLine(pen,e.Bounds.Left+3,e.Bounds.Bottom-2,e.Bounds.Right-3,e.Bounds.Bottom-2);};tabs.Dock=DockStyle.Fill;repositoriesTab.BackColor=Color.White;settingsTab.BackColor=Color.White;tabs.TabPages.AddRange(new[]{repositoriesTab,settingsTab});Controls.Add(tabs);
        Put(new Label{Text="커밋 알리미",Font=new Font("맑은 고딕",20,FontStyle.Bold)},22,15,400,43);
        Put(new Label{Text="여러 저장소의 새 커밋과 내 ZIP 상태를 한눈에 확인하세요."},24,62,1000,27);
        ButtonAt(add,"+ 추가",24,102,82);ButtonAt(bulkAdd,"여러 개 추가",116,102,135);ButtonAt(edit,"수정",261,102,64);ButtonAt(remove,"삭제",335,102,64);
        ButtonAt(check,"전체 지금 확인",414,102,142);interval.Minimum=1;interval.Maximum=10080;interval.Value=180;
        ButtonAt(sortRecent,"최근 커밋순",568,102,150);ButtonAt(download,"체크한 ZIP 다운로드",730,102,356);
        ButtonAt(selectAll,"전체 선택",24,145,110);ButtonAt(selectNone,"전체 취소",144,145,110);Put(selectedCount,270,153,800,25);
        sortRecent.Click+=delegate{config.SortRecent=!config.SortRecent;RefreshList();Save();state.Text=config.SortRecent?"최근 커밋이 위로 오도록 자동 정렬합니다. 드래그하면 수동 정렬로 바뀝니다.":"현재 순서를 유지합니다. 행을 드래그해 순서를 바꿀 수 있습니다.";};
        defaultFolder.Click+=delegate{using(var d=new FolderBrowserDialog{SelectedPath=config.DefaultDownloadFolder,Description="새 저장소에 사용할 기본 다운로드 폴더"})if(d.ShowDialog(this)==DialogResult.OK){config.DefaultDownloadFolder=d.SelectedPath;Save();folderSetting.Text=d.SelectedPath;settingsStatus.Text="기본 폴더 저장 완료 · 새 저장소부터 적용됩니다.";}};
        selectAll.Click+=delegate{foreach(var e in config.Repositories)e.DownloadSelected=true;Save();RefreshList();};
        selectNone.Click+=delegate{foreach(var e in config.Repositories)e.DownloadSelected=false;Save();RefreshList();};
        list.View=View.Details; list.FullRowSelect=true; list.MultiSelect=false; list.HideSelection=false; list.GridLines=false; list.ShowItemToolTips=true;list.CheckBoxes=true;
        list.ItemCheck+=delegate(object sender,ItemCheckEventArgs e){if(refreshing)return;if(busy){e.NewValue=e.CurrentValue;return;}var entry=list.Items[e.Index].Tag as RepoEntry;if(entry!=null){entry.DownloadSelected=e.NewValue==CheckState.Checked;Save();RefreshDetails();}};
        list.AllowDrop=true;list.InsertionMark.Color=Color.RoyalBlue;
        list.ItemDrag+=delegate(object sender,ItemDragEventArgs e){if(busy||e.Button!=MouseButtons.Left)return;var row=e.Item as ListViewItem;if(row==null)return;dragging=true;try{list.DoDragDrop(new DataObject("SimpleCommit.RepositoryId",((RepoEntry)row.Tag).Id),DragDropEffects.Move);}finally{dragging=false;list.InsertionMark.Index=-1;}};
        list.DragEnter+=delegate(object sender,DragEventArgs e){e.Effect=!busy&&dragging&&e.Data.GetDataPresent("SimpleCommit.RepositoryId")?DragDropEffects.Move:DragDropEffects.None;};
        list.DragOver+=delegate(object sender,DragEventArgs e){
            if(busy||!dragging||!e.Data.GetDataPresent("SimpleCommit.RepositoryId")){e.Effect=DragDropEffects.None;return;}e.Effect=DragDropEffects.Move;
            Point point=list.PointToClient(new Point(e.X,e.Y));var target=list.GetItemAt(point.X,point.Y);
            if(target==null&&list.Items.Count>0){target=point.Y<35?list.Items[0]:list.Items[list.Items.Count-1];}
            if(target!=null){list.InsertionMark.Index=target.Index;list.InsertionMark.AppearsAfterItem=point.Y>target.Bounds.Top+target.Bounds.Height/2;}
            if(list.Items.Count>0&&(DateTime.UtcNow-lastDragScroll).TotalMilliseconds>150){int top=list.TopItem==null?0:list.TopItem.Index;if(point.Y<40&&top>0)list.Items[top-1].EnsureVisible();else if(point.Y>list.ClientSize.Height-28&&target!=null&&target.Index<list.Items.Count-1)list.Items[target.Index+1].EnsureVisible();lastDragScroll=DateTime.UtcNow;}
        };
        list.DragLeave+=delegate{list.InsertionMark.Index=-1;};
        list.DragDrop+=delegate(object sender,DragEventArgs e){if(busy||!dragging||!e.Data.GetDataPresent("SimpleCommit.RepositoryId"))return;var item=config.Repositories.FirstOrDefault(x=>x.Id==(e.Data.GetData("SimpleCommit.RepositoryId") as string));if(item==null||!config.Repositories.Contains(item))return;int boundary=list.InsertionMark.Index;if(boundary<0)boundary=config.Repositories.Count;else if(list.InsertionMark.AppearsAfterItem)boundary++;RepoOrder.Move(config.Repositories,item,boundary);config.SortRecent=false;RefreshList();Select(item);Save();state.Text="드래그한 순서를 저장했습니다.";list.InsertionMark.Index=-1;};
        list.Columns.Add("저장소",240); list.Columns.Add("브랜치",90); list.Columns.Add("커밋 시각",105); list.Columns.Add("최신 커밋",270); list.Columns.Add("내 ZIP",175); list.Columns.Add("확인 상태",160);
        Put(list,24,193,1062,253); list.Anchor=AnchorStyles.Top|AnchorStyles.Bottom|AnchorStyles.Left|AnchorStyles.Right;
        Put(state,24,462,1062,25); Put(details,24,491,1062,45); Put(schedule,24,540,1062,24);
        foreach(Control c in new Control[]{list,state,details,schedule}) c.Anchor=AnchorStyles.Left|AnchorStyles.Top;
        repositoriesTab.Resize+=delegate{LayoutRepositoryTab();};
        details.Font=new Font("맑은 고딕",9); details.ForeColor=Color.DimGray; details.AutoEllipsis=true; schedule.ForeColor=Color.DimGray;
        config=new Settings();
        try{if(!testing)config=SettingsStore.Load(configFile);config.Migrate();}catch{storageReadFailed=true;state.Text="설정 또는 저장소 목록을 읽지 못했습니다. 원본 보호를 위해 저장을 중단했습니다.";}
        interval.Value=config.CheckMinutes;
        interval.ValueChanged+=delegate{config.CheckMinutes=(int)interval.Value;foreach(var e in config.Repositories)e.NextUtc=DateTime.UtcNow.AddMinutes(config.CheckMinutes);Save();RefreshSchedule();settingsStatus.Text="자동 확인 간격을 "+config.CheckMinutes+"분으로 저장했습니다.";};
        startup.Text="윈도우 시작 시 트레이에서 자동 실행"; startup.Checked=config.StartWithWindows;
        startup.CheckedChanged+=delegate{config.StartWithWindows=startup.Checked;RegisterStartup();Save();};
        AppTheme.Mode=config.Theme;SetupSettings();ApplyTheme();
        Microsoft.Win32.UserPreferenceChangedEventHandler themeChanged=delegate{if(!IsDisposed&&IsHandleCreated)BeginInvoke((Action)delegate{if(config.Theme=="system")ApplyTheme();});};
        Microsoft.Win32.SystemEvents.UserPreferenceChanged+=themeChanged;FormClosed+=delegate{Microsoft.Win32.SystemEvents.UserPreferenceChanged-=themeChanged;};Shown+=delegate{ApplyTheme();};
        tray.Icon=AppVisual.Load(16);tray.Text="커밋 알리미";tray.Visible=true;
        var menu=new ContextMenuStrip();menu.Items.Add("열기",null,delegate{Restore();});menu.Items.Add("종료",null,delegate{if(busy){Restore();MessageBox.Show(this,"진행 중인 작업이 끝난 뒤 종료해 주세요.");return;}exiting=true;Close();});tray.ContextMenuStrip=menu;tray.DoubleClick+=delegate{Restore();};
        list.SelectedIndexChanged+=delegate{RefreshDetails();};
        list.MouseDoubleClick+=delegate(object sender,MouseEventArgs e){if(e.Button!=MouseButtons.Left)return;var row=list.GetItemAt(e.X,e.Y);if(row==null)return;var entry=row.Tag as RepoEntry;if(entry==null)return;try{Repo.Parse(entry.Url);System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(entry.Url){UseShellExecute=true});}catch(Exception ex){state.Text="링크 열기 실패: "+ex.Message;}};
        var rowMenu=new ContextMenuStrip();
        rowMenu.Items.Add("페이지 이동",null,delegate{var entry=Selected();if(entry==null)return;try{Repo.Parse(entry.Url);System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(entry.Url){UseShellExecute=true});}catch(Exception ex){state.Text="링크 열기 실패: "+ex.Message;}});
        rowMenu.Items.Add("삭제",null,delegate{remove.PerformClick();});
        list.MouseDown+=delegate(object sender,MouseEventArgs e){if(e.Button!=MouseButtons.Right||busy)return;var row=list.GetItemAt(e.X,e.Y);if(row==null)return;row.Selected=true;row.Focused=true;rowMenu.Show(list,e.Location);};
        add.Click+=async delegate{using(var d=new RepoDialog(null,config.DefaultDownloadFolder))if(d.ShowDialog(this)==DialogResult.OK){var e=new RepoEntry{Url=d.RepoUrl,Branch=d.Branch,Folder=d.Folder};if(Duplicate(e,null))return;config.Repositories.Add(e);Save();RefreshList();Select(e);await Run(new List<RepoEntry>{e},false);}};
        bulkAdd.Click+=async delegate{using(var d=new BulkRepoDialog(config.DefaultDownloadFolder))if(d.ShowDialog(this)==DialogResult.OK){
            var added=new List<RepoEntry>();int duplicates=0;
            foreach(string address in d.Urls){var entry=new RepoEntry{Url=address,Folder=d.Folder};if(config.Repositories.Any(e=>e.Identity==entry.Identity)){duplicates++;continue;}config.Repositories.Add(entry);added.Add(entry);}
            Save();RefreshList();if(added.Count>0){Select(added[0]);await Run(added,false);}state.Text="일괄 등록 "+added.Count+"개 · 이미 등록된 저장소 "+duplicates+"개 건너뜀 · 확인 결과는 목록에서 확인하세요.";
        }};
        edit.Click+=delegate{EditSelected();};
        remove.Click+=delegate{var e=Selected();if(e==null)return;config.Repositories.Remove(e);Save();RefreshList();state.Text="목록에서 제거했습니다. 다운로드한 ZIP은 그대로 있습니다.";};
        check.Click+=async delegate{await Run(config.Repositories.ToList(),false);};
        download.Click+=async delegate{await Run(config.Repositories.Where(e=>e.DownloadSelected).ToList(),true);};
        link.Click+=async delegate{var e=Selected();if(e==null)return;using(var d=new OpenFileDialog{Filter="ZIP 파일|*.zip",Title="선택한 저장소에서 받은 ZIP을 연결하세요"})if(d.ShowDialog(this)==DialogResult.OK){try{string sha=ZipState.Identify(d.FileName);e.DownloadedPath=d.FileName;e.DownloadedSha=sha;e.ImportedZip=true;Save();RefreshList();await Run(new List<RepoEntry>{e},false);}catch(Exception ex){state.Text="ZIP 연결 실패: "+ex.Message;}}};
        timer.Interval=15000;timer.Tick+=async delegate{if(dragging)return;RefreshList();if(!testing&&!busy)await Run(config.Repositories.Where(e=>e.NextUtc<=DateTime.UtcNow).ToList(),false);};timer.Start();
        FormClosing+=delegate(object sender,FormClosingEventArgs e){if(e.CloseReason==CloseReason.WindowsShutDown || e.CloseReason==CloseReason.TaskManagerClosing)exiting=true;if(!exiting){e.Cancel=true;Hide();if(!config.TrayHintShown){config.TrayHintShown=true;Save();ShowNotice("커밋 알리미","트레이에서 계속 실행합니다.\n종료는 트레이 아이콘을 우클릭하세요.");}}else{timer.Stop();if(notification!=null&&!notification.IsDisposed)notification.Close();tray.Dispose();}};
        if(inTray){Opacity=0;ShowInTaskbar=false;}
        Shown+=async delegate{LayoutRepositoryTab();if(inTray){Hide();ShowInTaskbar=true;Opacity=1;}if(testing)return;RegisterStartup();Save();await Run(config.Repositories.ToList(),false);};
        RefreshList();if(state.Text=="")state.Text=config.Repositories.Count==0?"‘+ 추가’ 또는 ‘여러 개 추가’를 눌러 시작하세요.":"저장소를 선택하면 정확한 커밋 시각과 ZIP 경로를 볼 수 있습니다.";
    }
    void Put(Control c,int x,int y,int w,int h){c.SetBounds(x,y,w,h);repositoriesTab.Controls.Add(c);}
    void LayoutRepositoryTab(){int width=Math.Max(400,repositoriesTab.ClientSize.Width-48),height=repositoriesTab.ClientSize.Height;list.SetBounds(24,193,width,Math.Max(100,height-343));state.SetBounds(24,height-136,width,25);details.SetBounds(24,height-106,width,45);schedule.SetBounds(24,height-47,width,24);}
    void Setting(Control c,int x,int y,int w,int h){c.SetBounds(x,y,w,h);settingsTab.Controls.Add(c);}
    void ApplyTheme(){AppTheme.Mode=config.Theme;foreach(Form window in Application.OpenForms){if(!(window is ToastWindow))AppTheme.Apply(window);}AppTheme.Apply(this);tabs.Invalidate();RefreshList();}
    void SetupSettings(){
        Setting(new Label{Text="설정",Font=new Font("맑은 고딕",20,FontStyle.Bold)},24,18,500,42);
        Setting(new Label{Text="자동 확인 간격"},24,82,190,26);Setting(interval,220,78,90,30);Setting(new Label{Text="분 · 실행 중에는 항상 자동 확인"},324,82,650,26);
        Setting(startup,24,127,900,28);
        defaultFolder.Text="기본 다운로드 폴더 변경";Setting(defaultFolder,24,183,240,36);folderSetting.Text=config.DefaultDownloadFolder;folderSetting.AutoEllipsis=true;Setting(folderSetting,24,229,1010,28);
        Setting(new Label{Text="새 저장소에 적용됩니다. 기존 저장소의 폴더는 수정 버튼에서 바꾸세요.",ForeColor=Color.DimGray},24,261,1010,26);
        Setting(new Label{Text="자체 알림 표시 시간"},24,323,190,26);noticeSeconds.Minimum=1;noticeSeconds.Maximum=120;noticeSeconds.Value=config.NotificationSeconds;Setting(noticeSeconds,220,319,90,30);Setting(new Label{Text="초"},322,323,40,26);
        var keepNotice=new CheckBox{Text="확인할 때까지 알림 유지",Checked=config.KeepNotificationUntilDismissed};Setting(keepNotice,575,320,360,30);noticeSeconds.Enabled=!keepNotice.Checked;
        keepNotice.CheckedChanged+=delegate{config.KeepNotificationUntilDismissed=keepNotice.Checked;noticeSeconds.Enabled=!keepNotice.Checked;Save();settingsStatus.Text=keepNotice.Checked?"알림을 확인하거나 닫을 때까지 유지합니다.":"설정한 시간이 지나면 알림을 닫습니다.";};
        noticeSeconds.ValueChanged+=delegate{config.NotificationSeconds=(int)noticeSeconds.Value;Save();settingsStatus.Text="알림 표시 시간을 "+config.NotificationSeconds+"초로 저장했습니다.";};
        var preview=new Button{Text="알림 미리보기"};Setting(preview,375,316,170,36);preview.Click+=delegate{ShowNotice("새 커밋 알림","화면 오른쪽 아래에 표시되는 자체 알림입니다.\n클릭하면 저장소 목록을 엽니다.",true);};
        Setting(new Label{Text="오른쪽 아래에 표시 · 클릭하면 목록 열기 · ×로 바로 닫기",ForeColor=Color.DimGray},24,365,1010,26);
        link.Text="선택 저장소 ZIP 수동 연결";Setting(link,24,419,260,36);Setting(linkSetting,300,425,740,28);
        Setting(new Label{Text="기존 ZIP은 자동 탐색합니다. 직접 연결이 필요할 때만 사용하세요.",ForeColor=Color.DimGray},24,467,1010,26);Setting(settingsStatus,24,550,1010,40);
        Setting(new Label{Text="테마"},24,510,190,26);var theme=new ComboBox{DropDownStyle=ComboBoxStyle.DropDownList};theme.Items.AddRange(new object[]{"시스템 설정 따르기","다크","화이트"});theme.SelectedIndex=config.Theme=="dark"?1:config.Theme=="light"?2:0;Setting(theme,220,505,240,30);theme.SelectedIndexChanged+=delegate{config.Theme=new[]{"system","dark","light"}[theme.SelectedIndex];ApplyTheme();Save();settingsStatus.Text="테마를 저장했습니다.";};
    }
    void ButtonAt(Button b,string text,int x,int y,int w){b.Text=text;Put(b,x,y,w,36);}
    void Restore(){Show();WindowState=FormWindowState.Normal;Activate();}
    void ShowNotice(string title,string message,bool preview=false){if(testing&&!preview)return;if(notification!=null&&!notification.IsDisposed)notification.Close();notification=new ToastWindow(title,message,config.KeepNotificationUntilDismissed?0:config.NotificationSeconds,delegate{tabs.SelectedTab=repositoriesTab;Restore();});notification.ShowAt(Screen.FromControl(this));}
    RepoEntry Selected(){return list.SelectedItems.Count==0?null:list.SelectedItems[0].Tag as RepoEntry;}
    void Select(RepoEntry entry){foreach(ListViewItem row in list.Items)if(row.Tag==entry){row.Selected=true;row.EnsureVisible();break;}}
    bool Duplicate(RepoEntry item,RepoEntry except){if(config.Repositories.Any(e=>e!=except&&e.Identity==item.Identity)){MessageBox.Show(this,"이미 등록한 저장소와 브랜치입니다.");return true;}return false;}
    async void EditSelected(){if(busy)return;var e=Selected();if(e==null)return;using(var d=new RepoDialog(e))if(d.ShowDialog(this)==DialogResult.OK){var replacement=new RepoEntry{Url=d.RepoUrl,Branch=d.Branch,Folder=d.Folder,DownloadSelected=e.DownloadSelected};if(Duplicate(replacement,e))return;bool changed=e.Identity!=replacement.Identity;if(changed){config.Repositories[config.Repositories.IndexOf(e)]=replacement;e=replacement;}else{e.Url=replacement.Url;e.Folder=replacement.Folder;}Save();RefreshList();Select(e);if(changed)await Run(new List<RepoEntry>{e},false);}}
    void RefreshList(){
        if(config.SortRecent)RepoOrder.Recent(config.Repositories);sortRecent.Text=config.SortRecent?"최근 커밋순 ▼":"최근 커밋순";
        var selected=Selected();refreshing=true;list.BeginUpdate();
        foreach(ListViewItem old in list.Items.Cast<ListViewItem>().ToArray())if(!config.Repositories.Contains((RepoEntry)old.Tag))list.Items.Remove(old);
        foreach(var e in config.Repositories){
            var row=list.Items.Cast<ListViewItem>().FirstOrDefault(x=>x.Tag==e);
            if(row==null){row=new ListViewItem(new string[]{"","","","","",""});row.Tag=e;list.Items.Add(row);}
            row.Checked=e.DownloadSelected;
            string name=e.Url.Replace("https://","");
            try{var r=Repo.Parse(e.Url);name=r.Name;}catch{}
            row.SubItems[0].Text=name;row.SubItems[1].Text=e.Branch==""?"기본":e.Branch;row.SubItems[2].Text=RelativeTime.Format(e.CommitUtc,DateTime.UtcNow);row.SubItems[3].Text=e.Message==""?"—":e.Message;
            row.SubItems[4].Text=ZipState.Describe(e,File.Exists(e.DownloadedPath));row.SubItems[5].Text=e.Status;
            Color rowBackground=AppTheme.Dark?(config.Repositories.IndexOf(e)%2==0?AppTheme.Surface:Color.FromArgb(44,52,65)):Color.White;foreach(ListViewItem.ListViewSubItem cell in row.SubItems)cell.BackColor=rowBackground;
            row.ForeColor=e.Status.StartsWith("확인 실패")?(AppTheme.Dark?Color.Salmon:Color.Firebrick):AppTheme.Foreground;
            row.SubItems[4].ForeColor=row.SubItems[4].Text=="ZIP 업데이트 필요"?Color.DarkOrange:row.SubItems[4].Text.StartsWith("최신")?(AppTheme.Dark?Color.LightGreen:Color.ForestGreen):(AppTheme.Dark?Color.Silver:Color.DimGray);row.UseItemStyleForSubItems=false;
            row.ToolTipText=e.Url+"\n"+e.Message+"\n커밋: "+Exact(e.CommitUtc)+"\n마지막 확인: "+Exact(e.CheckedUtc)+"\nZIP 상태는 마지막 성공한 확인 기준입니다.";
        }list.EndUpdate();list.ListViewItemSorter=new RowOrder(config.Repositories);list.Sort();refreshing=false;if(selected!=null)Select(selected);RefreshDetails();RefreshSchedule();
    }
    static string Exact(DateTime d){return d==DateTime.MinValue?"—":d.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");}
    void RefreshDetails(){var e=Selected();linkSetting.Text=e==null?"저장소 탭에서 연결할 항목을 먼저 선택하세요.":"선택: "+Repo.Parse(e.Url).Name;edit.Enabled=remove.Enabled=link.Enabled=!busy&&e!=null;int checkedCount=config.Repositories.Count(x=>x.DownloadSelected);download.Enabled=!busy&&checkedCount>0;selectedCount.Text="다운로드 선택 "+checkedCount+" / "+config.Repositories.Count+"개 · 체크한 목록은 자동 저장됩니다.";details.Text=e==null?"목록에서 저장소를 선택하세요. ZIP 최신 여부는 마지막 성공한 확인 기준입니다.":"커밋: "+Exact(e.CommitUtc)+"  |  마지막 성공 확인: "+Exact(e.CheckedUtc)+"  |  "+e.LastSha+"\nZIP: "+(e.DownloadedPath==""?"아직 다운로드하지 않았습니다.":e.DownloadedPath)+(e.ImportedZip?"  (기존 ZIP은 파일명·폴더명 커밋 ID로 추정)":"");}
    void RefreshSchedule(){DateTime next=config.Repositories.Count==0?DateTime.MinValue:config.Repositories.Min(e=>e.NextUtc);schedule.Text="등록 "+config.Repositories.Count+"개 · "+config.CheckMinutes+"분마다 자동 확인 · "+(config.Repositories.Count==0?"저장소 등록 대기":next<=DateTime.UtcNow?"확인 대기":"다음 "+next.ToLocalTime().ToString("MM/dd HH:mm"))+" · 창을 닫아도 트레이에서 실행";}
    bool Save(){if(testing)return true;if(storageReadFailed)return false;try{SettingsStore.Save(configFile,config);return true;}catch(Exception e){state.Text="설정 저장 실패: "+e.Message;return false;}}
    void RegisterStartup(){if(testing)return;try{using(var key=Microsoft.Win32.Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run")){if(config.StartWithWindows)key.SetValue("SimpleCommit","\""+Application.ExecutablePath+"\" --tray");else key.DeleteValue("SimpleCommit",false);}}catch(Exception e){state.Text="자동 시작 등록 실패: "+e.Message;}}
    async Task Run(List<RepoEntry> entries,bool getZip){
        if(busy||entries.Count==0)return;busy=true;foreach(var b in new Control[]{add,bulkAdd,edit,remove,check,interval,download,link,selectAll,selectNone,defaultFolder,sortRecent})b.Enabled=false;int failed=0,updated=0,downloaded=0,skipped=0;string saved=null;
        try{foreach(var e in entries){
            bool checkedOk=false;
            try{
                var repo=Repo.Parse(e.Url);e.Status="확인 중…";state.Text=repo.Path+" · 커밋 확인 중…";RefreshList();
                await AutoConnect(e);
                var info=await Task.Run(()=>Backend.Latest(repo,e.Branch));bool changed=e.LastSha!=""&&e.LastSha!=info.Sha;
                e.LastSha=info.Sha;e.Message=info.Message;e.CommitUtc=info.CommittedUtc;e.CheckedUtc=DateTime.UtcNow;checkedOk=true;e.Status=changed?"새 커밋 있음":"확인 완료";if(changed)updated++;
                await AutoConnect(e);
                if(getZip&&ZipState.CanSkip(e)){e.Status="최신 ZIP 있음 · 건너뜀";skipped++;continue;}
                if(getZip){e.Status="ZIP 다운로드 중";var p=new Progress<long>(n=>{if(busy)state.Text=repo.Path+" · 다운로드 "+(n/1048576.0).ToString("0.0")+" MB";});string previous=e.DownloadedPath;string branch=e.Branch;if(String.IsNullOrWhiteSpace(branch))branch=(await Task.Run(()=>Backend.Branches(repo,System.Threading.CancellationToken.None))).Default;string target=Backend.DownloadPath(repo,branch,e.Folder);if(config.Repositories.Any(other=>other!=e&&!String.IsNullOrWhiteSpace(other.DownloadedPath)&&String.Equals(Path.GetFullPath(other.DownloadedPath),target,StringComparison.OrdinalIgnoreCase)))throw new IOException("다른 저장소의 ZIP과 이름이 같습니다. 다운로드 폴더를 변경해 주세요.");saved=await Task.Run(()=>Backend.Download(repo,info.Sha,e.Folder,n=>((IProgress<long>)p).Report(n),branch));e.DownloadedPath=saved;e.DownloadedSha=info.Sha;e.ImportedZip=false;e.Status="ZIP 저장 완료";downloaded++;if(Save()&&!config.Repositories.Any(other=>other!=e&&String.Equals(other.DownloadedPath,previous,StringComparison.OrdinalIgnoreCase))){try{Backend.DeletePreviousZip(previous,saved);}catch(Exception cleanup){e.Status="ZIP 저장 완료 · 이전 파일 삭제 실패: "+cleanup.Message;}}}
            }catch(Exception ex){failed++;e.Status=(checkedOk?"ZIP 실패: ":"확인 실패: ")+Error(ex);}
            finally{e.NextUtc=DateTime.UtcNow.AddMinutes(config.CheckMinutes);Save();RefreshList();}
        }
        state.Text=(getZip?"ZIP 다운로드 완료 · 저장 "+downloaded+"개 · 최신 파일 건너뜀 "+skipped+"개":"확인 완료 · "+entries.Count+"개 중 새 커밋 "+updated+"개")+(failed>0?" · 실패 "+failed+"개 (목록의 확인 상태 참고)":"");
        if(updated>0)ShowNotice("새 커밋 알림",updated+"개 저장소에 새 커밋이 있습니다.\n목록에서 최신 ZIP 상태를 확인하세요.");
        }finally{busy=false;add.Enabled=bulkAdd.Enabled=check.Enabled=interval.Enabled=selectAll.Enabled=selectNone.Enabled=defaultFolder.Enabled=sortRecent.Enabled=true;RefreshList();}
    }
    async Task AutoConnect(RepoEntry e){
        if(File.Exists(e.DownloadedPath)&&e.DownloadedSha==e.LastSha&&e.LastSha!="")return;
        var registered=config.Repositories.ToArray();
        var found=await Task.Run(()=>{
            var roots=new[]{e.Folder,config.DefaultDownloadFolder,Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),"Downloads")}.Where(x=>!String.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase);
            ZipState.Found fallback=null;
            foreach(string dir in roots){try{
                // A shared folder with same-name repositories cannot establish ownership reliably.
                if(registered.Any(x=>x!=e&&x.Url!=e.Url&&String.Equals(x.Folder,dir,StringComparison.OrdinalIgnoreCase)&&Repo.Parse(x.Url).Name==Repo.Parse(e.Url).Name))continue;
                var item=ZipState.FindExisting(new RepoEntry{Url=e.Url,Branch=e.Branch,Folder=dir,LastSha=e.LastSha});
                if(item==null)continue;if(item.Sha!=""&&e.LastSha!=""&&e.LastSha.StartsWith(item.Sha,StringComparison.OrdinalIgnoreCase))return item;if(fallback==null)fallback=item;
            }catch(IOException){}catch(UnauthorizedAccessException){}}
            return fallback;
        });
        if(found!=null&&(!File.Exists(e.DownloadedPath)||(found.Sha!=""&&e.LastSha!=""&&e.LastSha.StartsWith(found.Sha,StringComparison.OrdinalIgnoreCase)))){e.DownloadedPath=found.Path;e.DownloadedSha=found.Sha;e.ImportedZip=true;e.Status="기존 ZIP 자동 연결";}
    }
    static string Error(Exception e){var w=e as WebException;if(w!=null){var r=w.Response as HttpWebResponse;if(r!=null){int status=(int)r.StatusCode;r.Dispose();if(status==404)return "주소·브랜치 확인";if(status==403||status==429)return "접근 제한 / 요청 한도";}return "네트워크 연결";}return e.Message;}
    [System.Runtime.InteropServices.DllImport("user32.dll")]static extern bool AllowSetForegroundWindow(int processId);
    [STAThread]public static void Main(string[] args){
        ServicePointManager.SecurityProtocol=SecurityProtocolType.Tls12;
        if(args.Contains("--theme-ui-test")){testing=true;Application.EnableVisualStyles();using(var f=new MainWindow(false)){f.Show();foreach(string mode in new[]{"dark","light","system"}){f.config.Theme=mode;f.ApplyTheme();Application.DoEvents();if(f.list.BackColor!=AppTheme.Surface||f.settingsTab.ForeColor!=AppTheme.Foreground)throw new Exception("Theme colors failed");}f.exiting=true;f.Close();}Console.WriteLine("PASS: dark, light and system theme UI");return;}
        if(args.Contains("--test")){Tests.Run();return;}
        if(args.Length>1&&args[0]=="--settings-ui-test"){
            testing=true;Application.EnableVisualStyles();using(var f=new MainWindow(false)){f.Show();f.tabs.SelectedTab=f.settingsTab;Application.DoEvents();f.noticeSeconds.Value=12;
                if(f.config.NotificationSeconds!=12||f.interval.Parent!=f.settingsTab||f.defaultFolder.Parent!=f.settingsTab||f.startup.Parent!=f.settingsTab||f.link.Parent!=f.settingsTab||f.list.Parent!=f.repositoriesTab)throw new Exception("Settings tab wiring failed");
                using(var bmp=new Bitmap(f.Width,f.Height)){f.DrawToBitmap(bmp,new Rectangle(0,0,f.Width,f.Height));bmp.Save(args[1]);}f.tray.Dispose();Console.WriteLine("PASS: settings tab separation and notification duration changes");
            }return;
        }
        if(args.Length>1&&args[0]=="--toast-ui-test"){Application.EnableVisualStyles();ToastWindow.TestUi(args[1]);return;}
        if(args.Length>1&&args[0]=="--order-ui-test"){
            try{testing=true;Application.EnableVisualStyles();using(var f=new MainWindow(false)){
                var old=new RepoEntry{Url="https://github.com/example/older",CommitUtc=DateTime.UtcNow.AddHours(-2),DownloadSelected=true};var recent=new RepoEntry{Url="https://codeberg.org/example/recent",CommitUtc=DateTime.UtcNow.AddMinutes(-3)};
                f.config.Repositories.AddRange(new[]{old,recent});f.RefreshList();f.Show();Application.DoEvents();f.Select(old);f.sortRecent.PerformClick();
                if(f.list.Items[0].Tag!=recent||f.Selected()!=old||!f.list.Items[1].Checked)throw new Exception("Sort UI state failed");
                f.dragging=true;f.list.InsertionMark.Index=0;f.list.InsertionMark.AppearsAfterItem=false;
                var data=new DataObject("SimpleCommit.RepositoryId",old.Id);var drop=new DragEventArgs(data,0,0,0,DragDropEffects.Move,DragDropEffects.Move);
                typeof(Control).GetMethod("OnDragDrop",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).Invoke(f.list,new object[]{drop});f.dragging=false;
                if(f.config.SortRecent||f.config.Repositories[0]!=old||f.list.Items[0].Tag!=old||!old.DownloadSelected)throw new Exception("Drag drop ordering failed");
                using(var bmp=new Bitmap(f.Width,f.Height)){f.DrawToBitmap(bmp,new Rectangle(0,0,f.Width,f.Height));bmp.Save(args[1]);}f.tray.Dispose();Console.WriteLine("PASS: sort button, selection/check preservation, drag-drop handler, manual mode");
            }}catch(Exception error){Console.WriteLine(error.GetType().Name+": "+error.Message+"\n"+error.StackTrace);if(error.InnerException!=null)Console.WriteLine(error.InnerException.Message);Environment.ExitCode=1;}return;
        }
        if(args.Length>1&&args[0]=="--bulk-ui-test"){Application.EnableVisualStyles();BulkRepoDialog.TestUi(args[1]);return;}
        if(args.Length>1&&args[0]=="--branch-ui-test"){Application.EnableVisualStyles();RepoDialog.TestLive(args[1]);return;}
        if(args.Length>1&&args[0]=="--branches"){try{var b=Backend.Branches(Repo.Parse(args[1]),System.Threading.CancellationToken.None);Console.WriteLine("Default: "+b.Default+" | "+String.Join(", ",b.Names));}catch(Exception e){Console.WriteLine(e);Environment.ExitCode=1;}return;}
        if(args.Length>1&&args[0]=="--integration-test"){
            testing=true;Application.EnableVisualStyles();var f=new MainWindow(false);f.ShowInTaskbar=false;
            f.Shown+=async delegate{try{
                var gh=new RepoEntry{Url="https://github.com/octocat/Hello-World",Folder=args[1]};
                var missing=new RepoEntry{Url="https://github.com/octocat/simplecommit-nonexistent-test-20260920"};
                var gg=new RepoEntry{Url="https://gitgud.io/Patcheresu/dartgunsorbust"};
                f.config.Repositories.AddRange(new[]{gh,missing,gg});await f.Run(f.config.Repositories.ToList(),false);
                if(gh.LastSha==""||gg.LastSha==""||!missing.Status.StartsWith("확인 실패")||gh.CommitUtc==DateTime.MinValue||gg.CommitUtc==DateTime.MinValue)throw new Exception("Multi-repo isolation or timestamps failed");
                f.selectAll.PerformClick();if(f.config.Repositories.Any(x=>!x.DownloadSelected)||f.list.CheckedItems.Count!=3)throw new Exception("Select all failed");
                f.selectNone.PerformClick();if(f.config.Repositories.Any(x=>x.DownloadSelected)||f.list.CheckedItems.Count!=0)throw new Exception("Deselect all failed");
                f.list.Items[0].Checked=true;f.list.Items[2].Checked=true;
                if(!gh.DownloadSelected||missing.DownloadSelected||!gg.DownloadSelected)throw new Exception("Checkbox model failed");
                await f.Run(f.config.Repositories.Where(x=>x.DownloadSelected).ToList(),true);
                if(gg.DownloadedSha==""||missing.DownloadedPath!=""||f.state.Text.Contains("실패"))throw new Exception("Selected batch download failed");
                if(!ZipState.CanSkip(gh))throw new Exception("Download freshness failed");
                if(!gh.LastSha.StartsWith(ZipState.Identify(gh.DownloadedPath)))throw new Exception("Downloaded ZIP root mismatch");
                string previousZip=gh.DownloadedPath;await f.Run(new List<RepoEntry>{gh},true);if(gh.DownloadedPath!=previousZip||!gh.Status.Contains("건너뜀"))throw new Exception("Duplicate prevention failed");
                if(f.list.Items.Count!=3||gh.NextUtc<DateTime.UtcNow.AddHours(2.9))throw new Exception("List or schedule failed");
                f.interval.Value=17;if(f.config.CheckMinutes!=17||f.config.Repositories.Any(x=>x.NextUtc<DateTime.UtcNow.AddMinutes(16.9)||x.NextUtc>DateTime.UtcNow.AddMinutes(17.1)))throw new Exception("Interval reschedule failed");
                Console.WriteLine("PASS: live GitHub + GitGud, failure isolation, select all/none, checkbox model, selected batch download, ZIP freshness/import, list rows, scheduling");
            }catch(Exception e){Console.WriteLine(e);Environment.ExitCode=1;}finally{f.exiting=true;f.Close();}};Application.Run(f);return;
        }
        if(args.Length>1&&args[0]=="--probe"){try{var c=Backend.Latest(Repo.Parse(args[1]),"");Console.WriteLine(c.Sha+" | "+c.CommittedUtc.ToString("o")+" | "+RelativeTime.Format(c.CommittedUtc,DateTime.UtcNow));if(args.Length>2)Console.WriteLine(Backend.Download(Repo.Parse(args[1]),c.Sha,args[2],n=>{}));}catch(Exception e){Console.WriteLine(e);Environment.ExitCode=1;}return;}
        if(args.Length>1&&args[0]=="--ui-test"){testing=true;Application.EnableVisualStyles();using(var f=new MainWindow(false)){if(args.Contains("--dark")){f.config.Theme="dark";f.ApplyTheme();}f.config.Repositories.Add(new RepoEntry{Url="https://github.com/example/desktop-app",Message="다운로드 안정성 개선",CommitUtc=DateTime.UtcNow.AddMinutes(-7),LastSha=new string('a',40),DownloadedSha=new string('b',40),DownloadedPath=args[1],Status="새 커밋 있음",CheckedUtc=DateTime.UtcNow});f.config.Repositories.Add(new RepoEntry{Url="https://gitgud.io/example/tools",Message="설정 화면 업데이트",CommitUtc=DateTime.UtcNow.AddHours(-2),Status="확인 완료"});f.RefreshList();f.Show();Application.DoEvents();using(var bmp=new Bitmap(f.Width,f.Height)){f.DrawToBitmap(bmp,new Rectangle(0,0,f.Width,f.Height));bmp.Save(args[1]);}f.tray.Dispose();}return;}
        using(var activate=new System.Threading.EventWaitHandle(false,System.Threading.EventResetMode.AutoReset,@"Local\SimpleCommitActivate")){
            bool created;using(var mutex=new System.Threading.Mutex(true,@"Local\SimpleCommitApp",out created)){
                if(!created){AllowSetForegroundWindow(-1);activate.Set();return;}
                Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
                using(var window=new MainWindow(args.Contains("--tray")))using(var activationTimer=new Timer{Interval=200}){
                    activationTimer.Tick+=delegate{if(activate.WaitOne(0))window.Restore();};
                    window.Shown+=delegate{activationTimer.Start();};
                    Application.Run(window);
                }
            }
        }
    }
}
