using System;
using System.Drawing;
using System.Windows.Forms;
public static class NotificationSound {
    static readonly Lazy<System.Media.SoundPlayer> sound=new Lazy<System.Media.SoundPlayer>(()=>{
        var stream=typeof(NotificationSound).Assembly.GetManifestResourceStream("ding.wav");
        if(stream==null)throw new System.IO.FileNotFoundException("Notification sound resource is missing.");
        var player=new System.Media.SoundPlayer(stream);player.Load();return player;
    });
    public static void Play(){try{sound.Value.Play();}catch{ /* Audio unavailable must not interrupt notifications. */ }}
}
public sealed class ToastWindow:Form {
    readonly Timer life=new Timer();DateTime expires;readonly int seconds;
    protected override bool ShowWithoutActivation {get{return true;}}
    protected override CreateParams CreateParams {get{var p=base.CreateParams;p.ExStyle|=0x08000000|0x80;return p;}}
    public ToastWindow(string title,string message,int duration,Action open){
        seconds=Math.Max(1,duration);FormBorderStyle=FormBorderStyle.None;ShowInTaskbar=false;TopMost=true;StartPosition=FormStartPosition.Manual;AutoScaleMode=AutoScaleMode.Dpi;
        ClientSize=new Size(390,140);BackColor=Color.FromArgb(24,32,48);ForeColor=Color.White;Font=new Font("맑은 고딕",10);AccessibleName="커밋 알리미 알림";
        var stripe=new Panel{BackColor=Color.FromArgb(53,218,166),Bounds=new Rectangle(0,0,5,140),Anchor=AnchorStyles.Top|AnchorStyles.Bottom|AnchorStyles.Left};Controls.Add(stripe);
        var symbol=new PictureBox{Bounds=new Rectangle(19,20,32,32),SizeMode=PictureBoxSizeMode.StretchImage};using(var icon=AppVisual.Load(32))symbol.Image=icon.ToBitmap();Controls.Add(symbol);
        var heading=new Label{Text=title,Bounds=new Rectangle(64,16,279,28),Font=new Font("맑은 고딕",11,FontStyle.Bold),AutoEllipsis=true};Controls.Add(heading);
        var body=new Label{Text=message,Bounds=new Rectangle(64,47,302,48),ForeColor=Color.FromArgb(222,230,240),AutoEllipsis=true};Controls.Add(body);
        var hint=new Label{Text="클릭하여 열기",Bounds=new Rectangle(64,107,240,22),Font=new Font("맑은 고딕",9),ForeColor=Color.FromArgb(103,222,185)};Controls.Add(hint);
        var dismiss=new Button{Text="×",Bounds=new Rectangle(350,8,30,30),FlatStyle=FlatStyle.Flat,ForeColor=Color.FromArgb(200,210,225),BackColor=BackColor,TabStop=false,AccessibleName="알림 닫기"};dismiss.FlatAppearance.BorderSize=0;Controls.Add(dismiss);dismiss.Click+=delegate{Close();};
        foreach(Control c in new Control[]{this,stripe,symbol,heading,body,hint}){c.Cursor=Cursors.Hand;c.Click+=delegate{Close();if(open!=null)open();};}
        life.Interval=100;life.Tick+=delegate{if(DateTime.UtcNow>=expires)Close();};
        FormClosed+=delegate{life.Stop();life.Dispose();symbol.Image.Dispose();};
    }
    public void ShowAt(Screen screen){Opacity=0;Show();var area=screen.WorkingArea;Location=new Point(Math.Max(area.Left,area.Right-Width-16),Math.Max(area.Top,area.Bottom-Height-16));Opacity=1;expires=DateTime.UtcNow.AddSeconds(seconds);life.Start();}
    public static void TestUi(string path){using(var toast=new ToastWindow("새 커밋 알림","2개 저장소에 새 커밋이 있습니다.\n목록에서 최신 ZIP 상태를 확인하세요.",7,null)){
        toast.ShowAt(Screen.PrimaryScreen);Application.DoEvents();
        if(toast.ShowInTaskbar||!toast.TopMost||!Screen.PrimaryScreen.WorkingArea.Contains(toast.Bounds))throw new Exception("Toast position/style failed");
        using(var bitmap=new Bitmap(toast.Width,toast.Height)){toast.DrawToBitmap(bitmap,new Rectangle(0,0,toast.Width,toast.Height));bitmap.Save(path);}
        if((toast.expires-DateTime.UtcNow).TotalSeconds<5)throw new Exception("Notification duration failed");
        toast.expires=DateTime.UtcNow.AddMilliseconds(150);Application.Run(toast);if(!toast.IsDisposed)throw new Exception("Auto-dismiss failed");Console.WriteLine("PASS: custom notification positioning, rendering, configured duration, automatic dismissal");
    }}
}
