using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows.Forms;

namespace AgitCon {
    internal static class UiZoom {
        static readonly ConditionalWeakTable<Control,object> applied=new ConditionalWeakTable<Control,object>();
        internal static int Percent=100;
        internal static float Factor {get{return Percent/100f;}}
        internal static int Normalize(int value){return new[]{75,100,125,150,175,200}.Contains(value)?value:100;}
        internal static int Px(int value){return (int)Math.Round(value*Factor);}
        internal static bool Installed(Control control){object marker;return applied.TryGetValue(control,out marker);}
        static Size Scaled(Size value){return new Size(Px(value.Width),Px(value.Height));}
        static Padding Scaled(Padding value){return new Padding(Px(value.Left),Px(value.Top),Px(value.Right),Px(value.Bottom));}
        internal static void Start(int percent){Percent=Normalize(percent);if(Percent!=100)Application.Idle+=(s,e)=>{foreach(Form form in Application.OpenForms.Cast<Form>().ToArray())Install(form);};}
        internal static void Install(Control control){
            if(Percent==100||Installed(control))return;applied.Add(control,new object());control.SuspendLayout();
            try{
                var children=control.Controls.Cast<Control>().ToArray();
                var form=control as Form;if(form!=null){form.AutoScaleMode=AutoScaleMode.None;if(!System.ComponentModel.TypeDescriptor.GetProperties(form)["Font"].ShouldSerializeValue(form))form.Font=new Font(form.Font.FontFamily,form.Font.Size*Factor,form.Font.Style);form.ClientSize=Scaled(form.ClientSize);}else if(!control.AutoSize)control.Bounds=new Rectangle(Px(control.Left),Px(control.Top),Px(control.Width),Px(control.Height));
                control.Padding=Scaled(control.Padding);control.Margin=Scaled(control.Margin);control.MinimumSize=Scaled(control.MinimumSize);if(!control.MaximumSize.IsEmpty)control.MaximumSize=Scaled(control.MaximumSize);
                var table=control as TableLayoutPanel;if(table!=null){foreach(RowStyle row in table.RowStyles)if(row.SizeType==SizeType.Absolute)row.Height*=Factor;foreach(ColumnStyle column in table.ColumnStyles)if(column.SizeType==SizeType.Absolute)column.Width*=Factor;}
                foreach(var child in children)Install(child);
                control.ControlAdded+=(s,e)=>Install(e.Control);
            }finally{control.ResumeLayout(true);}
        }
        internal static void AddSettings(Form form,bool floating,Action<bool> changed,int scale,Action<int> scaleChanged,Action reset){
            var tabs=form.Controls.OfType<TabControl>().Single();tabs.Font=Ui.Regular(9);var page=new TabPage("화면"){BackColor=Ui.Paper};tabs.TabPages.Add(page);
            var panel=new FlowLayoutPanel{Dock=DockStyle.Fill,Padding=new Padding(22),FlowDirection=FlowDirection.TopDown,WrapContents=false,AutoScroll=true};page.Controls.Add(panel);
            var title=Ui.Label("창 표시와 배율",17,true);title.Size=new Size(470,46);panel.Controls.Add(title);
            var toggle=new CheckBox{Text="플로팅 ON · 카톡 입력창 따라가기",Font=Ui.Regular(10),AutoSize=true,Checked=floating};panel.Controls.Add(toggle);toggle.CheckedChanged+=(s,e)=>changed(toggle.Checked);
            var description=Ui.Label("켜면 카톡 입력창 옆을 따라가고 다른 앱에서는 숨겨요.\n끄면 일반 창으로 유지해요. 선택은 다시 실행해도 기억해요.",9);description.Size=new Size(470,60);panel.Controls.Add(description);
            var position=Ui.Button("플로팅 위치 자동 배치로 초기화",reset);position.Width=300;panel.Controls.Add(position);
            var label=Ui.Label("아지트콘 UI 배율",11,true);label.Size=new Size(470,42);label.Padding=new Padding(0,14,0,0);panel.Controls.Add(label);
            var zoom=new ComboBox{DropDownStyle=ComboBoxStyle.DropDownList,Font=Ui.Regular(10),Width=160};zoom.Items.AddRange(new object[]{"75%","100%","125%","150%","175%","200%"});zoom.SelectedItem=Normalize(scale)+"%";panel.Controls.Add(zoom);
            var selectedScale=Ui.Label("저장된 배율: "+Normalize(scale)+"% · 현재 적용: "+Percent+"%",10);selectedScale.Size=new Size(470,30);panel.Controls.Add(selectedScale);
            var note=Ui.Label("글자·버튼·콘 칸을 함께 조절해요.\n저장한 배율은 다음 앱 실행부터 적용해요.\nWindows의 해상도·배율 설정은 바꾸지 않아요.",9);note.Size=new Size(470,90);panel.Controls.Add(note);
            zoom.SelectedIndexChanged+=(s,e)=>{int value=Int32.Parse(zoom.SelectedItem.ToString().TrimEnd('%'));scaleChanged(value);selectedScale.Text="저장된 배율: "+value+"% · 현재 적용: "+Percent+"%";note.Text="배율을 저장했어요. 앱을 종료한 뒤 다시 켜면 적용돼요.\nWindows 배율은 바꾸지 않았어요.";};
        }
        internal static void Test(){int original=Percent;int checks=0;try{
            foreach(int scale in new[]{75,100,125,150,175,200}){Percent=scale;int height;var entries=Enumerable.Range(0,24).Select(i=>new Entry{DisplayName=i.ToString(),Path="local-"+i}).ToList();var cells=LibraryModel.RankedLayout(entries,Px(368),out height);if(cells.Where(c=>c.Entry!=null).Any(c=>c.Bounds.Width!=Px(92)||c.Bounds.Bottom>height))throw new Exception("Scaled grid bounds");checks++;
                using(var form=new Form{ClientSize=new Size(400,300),Font=Ui.Regular(10)}){var button=Ui.Button("확인",()=>{});button.SetBounds(20,20,100,36);form.Controls.Add(button);Install(form);if(form.ClientSize.Width!=Px(400)||button.Width!=Px(100))throw new Exception("Scaled form/button");Install(form);if(button.Width!=Px(100))throw new Exception("Double scaling");checks++;}
            }
            Percent=150;using(var form=new SemanticSettingsForm(false,value=>{})){
                form.AddThumbnailSettings(false,v=>{},()=>{},()=>{},()=>"test",()=>0,()=>false);int toggles=0,selected=0;AddSettings(form,false,v=>toggles++,150,v=>selected=v,()=>{});Install(form);
                var tabs=form.Controls.OfType<TabControl>().Single();tabs.SelectedIndex=tabs.TabCount-1;var panel=(FlowLayoutPanel)tabs.SelectedTab.Controls[0];panel.Controls.OfType<CheckBox>().Single().Checked=true;panel.Controls.OfType<ComboBox>().Single().SelectedItem="175%";if(toggles!=1||selected!=175)throw new Exception("Display settings callbacks");
                form.Controls.Remove(tabs);using(var surface=new UserControl{Size=form.ClientSize,BackColor=Ui.Paper}){surface.Controls.Add(tabs);surface.CreateControl();surface.PerformLayout();using(var bitmap=new Bitmap(surface.Width,surface.Height)){surface.DrawToBitmap(bitmap,surface.ClientRectangle);bitmap.Save(System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"display-settings-150.png"));}}
            }
            System.IO.File.WriteAllText(System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"ui-zoom-test.txt"),"PASS: "+checks+" 75~200% form/grid geometry, one-time scaling and display-settings callbacks. Own controls only; no OS display settings or Kakao interaction.");
        }catch(Exception ex){System.IO.File.WriteAllText(System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"ui-zoom-test.txt"),"FAIL: "+ex);Environment.ExitCode=1;}finally{Percent=original;}}
    }
}
