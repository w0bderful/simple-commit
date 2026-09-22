using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

public class BulkInput {
    public List<string> Urls=new List<string>(),Errors=new List<string>();
    public int Duplicates;
    public static BulkInput Parse(string text){
        var result=new BulkInput();var seen=new HashSet<string>(StringComparer.Ordinal);
        string[] lines=text.Replace("\r","").Split('\n');
        for(int i=0;i<lines.Length;i++){
            string value=lines[i].Trim();if(value=="")continue;
            try{var repo=Repo.Parse(value);string normalized="https://"+repo.Host+"/"+repo.Path;if(seen.Add(normalized))result.Urls.Add(normalized);else result.Duplicates++;}
            catch(Exception){result.Errors.Add((i+1)+"행: 올바른 저장소 링크를 입력해 주세요.");}
        }return result;
    }
}
public class BulkRepoDialog:Form {
    TextBox links=new TextBox(),folder=new TextBox(),problems=new TextBox();Label summary=new Label();Button register=new Button();
    public List<string> Urls=new List<string>();
    public string Folder {get{return folder.Text;}}
    public BulkRepoDialog(string defaultFolder){Shown+=delegate{AppTheme.Apply(this);};
        Text="저장소 여러 개 추가";Icon=AppVisual.Load(32);Font=new Font("맑은 고딕",10);ClientSize=new Size(650,530);MinimumSize=new Size(550,500);StartPosition=FormStartPosition.CenterParent;
        Add(new Label{Text="저장소 링크를 한 줄에 하나씩 붙여 넣으세요."},20,16,610,26);
        Add(new Label{Text="GitHub · GitGud · Codeberg · GitLab / 브랜치는 각 저장소의 기본값"},20,44,610,26);
        links.Multiline=true;links.ScrollBars=ScrollBars.Vertical;links.WordWrap=false;links.AcceptsReturn=true;Add(links,20,78,610,218);links.Anchor=AnchorStyles.Left|AnchorStyles.Right|AnchorStyles.Top|AnchorStyles.Bottom;
        Add(summary,20,308,610,25);summary.Anchor=AnchorStyles.Left|AnchorStyles.Right|AnchorStyles.Bottom;
        problems.Multiline=true;problems.ReadOnly=true;problems.BorderStyle=BorderStyle.None;problems.BackColor=BackColor;problems.ForeColor=Color.Firebrick;problems.ScrollBars=ScrollBars.Vertical;Add(problems,20,338,610,50);problems.Anchor=AnchorStyles.Left|AnchorStyles.Right|AnchorStyles.Bottom;
        var label=new Label{Text="이번에 등록할 저장소의 다운로드 폴더"};Add(label,20,396,610,25);label.Anchor=AnchorStyles.Left|AnchorStyles.Bottom;
        folder.ReadOnly=true;folder.Text=defaultFolder;Add(folder,20,425,493,28);folder.Anchor=AnchorStyles.Left|AnchorStyles.Right|AnchorStyles.Bottom;
        var browse=new Button{Text="폴더 선택"};Add(browse,523,423,107,32);browse.Anchor=AnchorStyles.Right|AnchorStyles.Bottom;
        browse.Click+=delegate{using(var d=new FolderBrowserDialog{SelectedPath=folder.Text})if(d.ShowDialog(this)==DialogResult.OK)folder.Text=d.SelectedPath;};
        Add(register,378,479,140,34);register.Anchor=AnchorStyles.Right|AnchorStyles.Bottom;
        var cancel=new Button{Text="취소",DialogResult=DialogResult.Cancel};Add(cancel,528,479,102,34);cancel.Anchor=AnchorStyles.Right|AnchorStyles.Bottom;CancelButton=cancel;
        // Enter inserts another URL line; registration is an explicit button action.
        links.TextChanged+=delegate{UpdateInput();};
        register.Click+=delegate{var input=BulkInput.Parse(links.Text);if(input.Errors.Count>0||input.Urls.Count==0)return;Urls=input.Urls;DialogResult=DialogResult.OK;};
        UpdateInput();
    }
    void UpdateInput(){var input=BulkInput.Parse(links.Text);summary.Text="등록 가능 "+input.Urls.Count+"개 · 입력 중복 "+input.Duplicates+"개 제외";problems.Text=String.Join(Environment.NewLine,input.Errors);register.Enabled=input.Errors.Count==0&&input.Urls.Count>0;register.Text=input.Urls.Count+"개 등록";}
    void Add(Control c,int x,int y,int w,int h){c.SetBounds(x,y,w,h);Controls.Add(c);}
    public static void TestUi(string path){using(var d=new BulkRepoDialog("C:\\Downloads")){
        d.links.Text="https://github.com/octocat/Hello-World\r\nhttps://gitgud.io/Patcheresu/dartgunsorbust\r\nhttps://codeberg.org/Codeberg/Documentation\r\nhttps://gitlab.com/gitlab-examples/python-getting-started";
        if(!d.register.Enabled||d.Folder!="C:\\Downloads")throw new Exception("Bulk dialog validation failed");
        d.Show();Application.DoEvents();using(var bitmap=new Bitmap(d.Width,d.Height)){d.DrawToBitmap(bitmap,new Rectangle(0,0,d.Width,d.Height));bitmap.Save(path);}
        d.links.AppendText("\r\ninvalid");if(d.register.Enabled)throw new Exception("Invalid URL must block registration");
        d.links.Text="https://github.com/a/b";d.register.PerformClick();if(d.Urls.Count!=1||d.DialogResult!=DialogResult.OK)throw new Exception("Bulk registration result failed");
        Console.WriteLine("PASS: bulk dialog validation, default folder, save result, visual rendering");
    }}
}
