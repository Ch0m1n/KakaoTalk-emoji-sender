using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace AgitCon {
    public static class Ui {
        // ToolStripManager still uses the dropdown after Closed returns. Never
        // destroy its handle inside that callback or a row-disposal callback.
        public static void DisposeMenuLater(ContextMenuStrip menu){
            if(menu==null||menu.IsDisposed)return;
            if(!menu.IsHandleCreated){menu.Dispose();return;}
            menu.BeginInvoke((MethodInvoker)(()=>{if(!menu.IsDisposed)menu.Dispose();}));
        }
        [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern IntPtr SendMessage(IntPtr h,int message,IntPtr wParam,string text);
        public static readonly Color Paper=Color.FromArgb(250,250,248), Surface=Color.White;
        public static readonly Color Ink=Color.FromArgb(36,43,46), Muted=Color.FromArgb(111,120,125);
        public static readonly Color Line=Color.FromArgb(229,232,232), Accent=Color.FromArgb(249,218,91);
        public static readonly Color Soft=Color.FromArgb(240,242,241);
        public static Font Regular(float size){return new Font("맑은 고딕",size*UiZoom.Factor,FontStyle.Regular);}
        public static Font Bold(float size){return new Font("맑은 고딕",size*UiZoom.Factor,FontStyle.Bold);}
        public static Icon AppIcon(int size=32){using(var stream=System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream("AgitCon.AppIcon.ico")){if(stream==null)throw new InvalidOperationException("App icon resource missing");using(var icon=new Icon(stream,new Size(size,size)))return (Icon)icon.Clone();}}
        public static void Cue(TextBox box,string text){if(box.IsHandleCreated)SendMessage(box.Handle,0x1501,new IntPtr(1),text);}
        public static GraphicsPath Round(Rectangle r,int radius) {
            var p=new GraphicsPath();int d=radius*2;
            if(d==0){p.AddRectangle(r);return p;}
            p.AddArc(r.Left,r.Top,d,d,180,90);p.AddArc(r.Right-d,r.Top,d,d,270,90);
            p.AddArc(r.Right-d,r.Bottom-d,d,d,0,90);p.AddArc(r.Left,r.Bottom-d,d,d,90,90);p.CloseFigure();return p;
        }
        public static UiButton Button(string text,Action action,bool primary=false){var b=new UiButton{Text=text,Primary=primary,Size=new Size(96,36),Font=Bold(9)};b.Click+=(s,e)=>action();return b;}
        public static Label Label(string text,float size,bool bold=false){return new Label{Text=text,Font=bold?Bold(size):Regular(size),ForeColor=Ink,BackColor=Color.Transparent,AutoEllipsis=true};}
        public static RoundedPanel Field(TextBox box,string accessibleName) {
            var frame=new RoundedPanel{BackColor=Surface,BorderColor=Line,Radius=10,Height=40};
            box.BorderStyle=BorderStyle.None;box.Font=Regular(10);box.BackColor=Surface;box.ForeColor=Ink;box.AccessibleName=accessibleName;
            box.HandleCreated+=(s,e)=>SendMessage(box.Handle,0x1501,new IntPtr(1),accessibleName);
            frame.Controls.Add(box);frame.SizeChanged+=(s,e)=>box.SetBounds(UiZoom.Px(12),Math.Max(1,(frame.Height-box.PreferredHeight)/2),Math.Max(40,frame.Width-UiZoom.Px(24)),box.PreferredHeight);
            box.GotFocus+=(s,e)=>{frame.BorderColor=Ink;frame.Invalidate();};box.LostFocus+=(s,e)=>{frame.BorderColor=Line;frame.Invalidate();};return frame;
        }
    }
    public class RoundedPanel:Panel {
        public int Radius=10;public Color BorderColor=Color.Transparent;
        public RoundedPanel(){DoubleBuffered=true;ResizeRedraw=true;}
        protected override void OnPaintBackground(PaintEventArgs e){e.Graphics.Clear(Parent==null?Ui.Paper:Parent.BackColor);e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;using(var path=Ui.Round(new Rectangle(0,0,Width-1,Height-1),Radius)){using(var brush=new SolidBrush(BackColor))e.Graphics.FillPath(brush,path);if(BorderColor.A>0)using(var pen=new Pen(BorderColor))e.Graphics.DrawPath(pen,path);}}
    }
    public class UiButton:Button {
        public bool Primary;public bool Active;public bool Quiet,TabStyle,LeftAlign;bool hover,pressed;
        public UiButton(){FlatStyle=FlatStyle.Flat;FlatAppearance.BorderSize=0;BackColor=Ui.Soft;ForeColor=Ui.Ink;Cursor=Cursors.Hand;DoubleBuffered=true;UseVisualStyleBackColor=false;}
        protected override void OnMouseEnter(EventArgs e){hover=true;Invalidate();base.OnMouseEnter(e);}
        protected override void OnMouseLeave(EventArgs e){hover=false;pressed=false;Invalidate();base.OnMouseLeave(e);}
        protected override void OnMouseDown(MouseEventArgs e){pressed=true;Invalidate();base.OnMouseDown(e);}
        protected override void OnMouseUp(MouseEventArgs e){pressed=false;Invalidate();base.OnMouseUp(e);}
        protected override void OnPaint(PaintEventArgs e){
            e.Graphics.Clear(Parent==null?Ui.Paper:Parent.BackColor);e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;
            Color fill=!Enabled?Ui.Soft:(Primary?Ui.Accent:TabStyle?(hover?Ui.Soft:(Parent==null?Ui.Paper:Parent.BackColor)):Active?Ui.Ink:(hover?Ui.Soft:Quiet?(Parent==null?Ui.Paper:Parent.BackColor):Ui.Soft));
            if(pressed&&Enabled)fill=ControlPaint.Dark(fill,0.05f);
            using(var path=Ui.Round(new Rectangle(0,0,Width-1,Height-1),7))using(var brush=new SolidBrush(fill))e.Graphics.FillPath(brush,path);
            Color ink=!Enabled?Ui.Muted:(TabStyle?Active?Ui.Ink:Ui.Muted:Active&&!Primary?Color.White:Ui.Ink);
            TextRenderer.DrawText(e.Graphics,Text,Font,new Rectangle(LeftAlign?8:5,pressed?1:0,Width-(LeftAlign?16:10),Height),ink,(LeftAlign?TextFormatFlags.Left:TextFormatFlags.HorizontalCenter)|TextFormatFlags.VerticalCenter|TextFormatFlags.EndEllipsis);
            if(TabStyle&&Active)using(var pen=new Pen(Ui.Ink,2))e.Graphics.DrawLine(pen,10,Height-2,Width-10,Height-2);
            if(Focused&&ShowFocusCues)ControlPaint.DrawFocusRectangle(e.Graphics,new Rectangle(4,4,Width-9,Height-9),ink,fill);
        }
    }
    public class PackRow:RoundedPanel {
        public readonly PackRecord Pack;public readonly UiButton Action;
        public readonly PictureBox Picture=new PictureBox();
        public readonly Label Caption=Ui.Label("",9),TitleLabel=Ui.Label("",10,true);
        public bool PreviewStarted;
        public PackRow(PackRecord pack,string subtitle,string buttonText,Action click,bool primary) {
            Pack=pack;BackColor=Ui.Surface;Height=106;Margin=new Padding(0,0,0,10);Radius=10;TabStop=false;
            Picture.BackColor=Ui.Soft;Picture.SizeMode=PictureBoxSizeMode.Zoom;Picture.AccessibleName=pack.Title+" 대표 콘";
            TitleLabel.Text=pack.Title;Caption.Text=subtitle;Caption.ForeColor=Ui.Muted;
            Action=Ui.Button(buttonText,click,primary);Controls.AddRange(new Control[]{Picture,TitleLabel,Caption,Action});
            SizeChanged+=(s,e)=>Arrange();Arrange();
            Picture.Disposed+=(s,e)=>{if(Picture.Image!=null)Picture.Image.Dispose();if(Picture.Tag is System.IO.Stream)((System.IO.Stream)Picture.Tag).Dispose();};
        }
        void Arrange(){Picture.SetBounds(14,14,78,78);Action.SetBounds(Math.Max(104,Width-108),59,94,32);TitleLabel.SetBounds(108,17,Math.Max(60,Width-124),28);Caption.SetBounds(108,49,Math.Max(30,Width-225),24);}
    }
}
