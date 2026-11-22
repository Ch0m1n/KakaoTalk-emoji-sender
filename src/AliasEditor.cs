using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace AgitCon {
    sealed class AliasEditor:Form {
        readonly TextBox aliases=new TextBox();internal readonly TableLayoutPanel LayoutRoot;
        public string Value{get{return aliases.Text.Trim();}}
        public AliasEditor(Entry entry){Icon=Ui.AppIcon();Text="아지트콘 · 별칭 편집";ClientSize=new Size(400,225);Font=Ui.Regular(10);BackColor=Ui.Paper;StartPosition=FormStartPosition.CenterParent;FormBorderStyle=FormBorderStyle.FixedDialog;MaximizeBox=false;MinimizeBox=false;
            LayoutRoot=new TableLayoutPanel{Dock=DockStyle.Fill,Padding=new Padding(20),ColumnCount=1,RowCount=4};foreach(int height in new[]{30,44,48,42})LayoutRoot.RowStyles.Add(new RowStyle(SizeType.Absolute,height));Controls.Add(LayoutRoot);
            var title=Ui.Label(entry.DisplayName??Path.GetFileName(entry.Path),12,true);title.Dock=DockStyle.Fill;LayoutRoot.Controls.Add(title,0,0);
            var help=Ui.Label("쉼표로 나눠 별칭을 추가해요.\n원본 이미지 이름은 바뀌지 않아요.",9);help.Dock=DockStyle.Fill;help.ForeColor=Ui.Muted;LayoutRoot.Controls.Add(help,0,1);
            aliases.Text=entry.Aliases??"";aliases.MaxLength=4096;var field=Ui.Field(aliases,"예: 웃음, 신남, ㅋㅋㅋ");field.Dock=DockStyle.Fill;LayoutRoot.Controls.Add(field,0,2);
            var actions=new FlowLayoutPanel{Dock=DockStyle.Fill,FlowDirection=FlowDirection.RightToLeft,Margin=Padding.Empty};var save=Ui.Button("별칭 저장",()=>DialogResult=DialogResult.OK,true);var cancel=Ui.Button("취소",()=>DialogResult=DialogResult.Cancel);actions.Controls.Add(save);actions.Controls.Add(cancel);LayoutRoot.Controls.Add(actions,0,3);AcceptButton=save;CancelButton=cancel;
        }
        public static void Render(){Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);using(var dialog=new AliasEditor(new Entry{DisplayName="웃는 콘",Aliases="웃음, 신남, ㅋㅋㅋ"}))using(var surface=new UserControl{Size=dialog.ClientSize,Font=dialog.Font,BackColor=Ui.Paper}){dialog.Controls.Remove(dialog.LayoutRoot);surface.Controls.Add(dialog.LayoutRoot);surface.CreateControl();using(var bitmap=new Bitmap(surface.Width,surface.Height)){surface.DrawToBitmap(bitmap,new Rectangle(Point.Empty,bitmap.Size));bitmap.Save(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"alias-editor-preview.png"));}}}
    }
}
