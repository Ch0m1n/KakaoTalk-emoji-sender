using System;using System.Collections.Generic;using System.Linq;using System.IO;using System.Threading;using System.Windows.Automation;
namespace AgitCon {
 internal enum QtTransferKind {Image,File}
 internal sealed class QtTransferState {
  internal string Surface,Send,Header;internal int Sends,Edits,Images;internal bool Visible,Enabled,Empty,ContainsComposer,FileMatches,SingleFile;
 }
 internal static class QtTransferRules {
  internal static string Normal(string value){return new string((value??"").Where(c=>!Char.IsWhiteSpace(c)&&c!='&').ToArray());}
  internal static readonly string[] ImageHeaders={"클립보드 이미지 전송","이미지 전송","사진 전송","사진 보내기","사진/동영상 전송"};
  internal static readonly string[] FileHeaders={"파일 전송","파일 보내기"};
  internal static bool Header(string name,QtTransferKind kind){return (kind==QtTransferKind.Image?ImageHeaders:FileHeaders).Any(x=>Normal(x)==Normal(name));}
  internal static bool Send(string name,QtTransferKind kind){string n=Normal(name);return kind==QtTransferKind.Image?(n=="전송"||n=="보내기"):(n=="1개전송"||n=="전송");}
  internal static bool Shim(bool qt,bool sameProcess,bool sameUiThread,string title,int width,int height){return qt&&sameProcess&&sameUiThread&&(String.IsNullOrEmpty(title)||title=="카카오톡"||title=="KakaoTalk")&&width>0&&width<=8&&height>0&&height<=8;}
  internal static bool FileLabel(string label,string file){string name=Path.GetFileName(file);if(String.IsNullOrEmpty(name)||label==null)return false;if(label==name)return true;return label.StartsWith(name,StringComparison.Ordinal)&&label.Length>name.Length&&(Char.IsWhiteSpace(label[name.Length])||label[name.Length]==','||label[name.Length]=='，');}
  internal static bool Valid(QtTransferState s,QtTransferKind kind){return s!=null&&!String.IsNullOrEmpty(s.Surface)&&!String.IsNullOrEmpty(s.Send)&&s.Visible&&s.Enabled&&!s.ContainsComposer&&s.Empty&&s.Edits<=1&&s.Sends==1&&Header(s.Header,kind)&&(kind==QtTransferKind.Image?(s.Images==1||(s.Images==0&&s.Edits==1)):s.FileMatches&&s.SingleFile);}
  // Reserve the one attempt before invoking the remote control. Exceptions never retry.
  internal static bool InvokeOnce(ref int attempted,Func<bool> current,Action invoke){try{if(!current()||Interlocked.CompareExchange(ref attempted,1,0)!=0)return false;if(!current())return false;invoke();return true;}catch{return false;}}
 }
 public sealed partial class PreviewPaste {
  sealed class QtTransferProof {
   internal IntPtr Window;internal AutomationElement Surface,Send;internal QtTransferKind Kind;internal string SurfaceId,SendId,File;
   internal PreviewChatTarget Target;internal int Attempted;
   internal bool Visible(){try{return QtComposer.Identity(Surface)==SurfaceId&&!Surface.Current.IsOffscreen;}catch{return false;}}
  }
  static bool UiVisible(AutomationElement e){try{return e.Current.IsEnabled&&!e.Current.IsOffscreen;}catch{return false;}}
  static bool UiInside(AutomationElement child,AutomationElement parent){string id=QtComposer.Identity(parent);if(id==null)return false;try{for(int i=0;child!=null&&i<32;i++,child=TreeWalker.ControlViewWalker.GetParent(child))if(QtComposer.Identity(child)==id)return true;}catch{}return false;}
  static List<AutomationElement> UiNodes(AutomationElement root,ControlType type){var list=new List<AutomationElement>();try{foreach(AutomationElement e in root.FindAll(TreeScope.Descendants,new PropertyCondition(AutomationElement.ControlTypeProperty,type)))if(UiVisible(e))list.Add(e);}catch{}return list;}
  static bool UiEmpty(AutomationElement edit){try{object pattern;if(edit.TryGetCurrentPattern(ValuePattern.Pattern,out pattern)){var value=(ValuePattern)pattern;return !value.Current.IsReadOnly&&String.IsNullOrWhiteSpace(value.Current.Value);}if(edit.TryGetCurrentPattern(TextPattern.Pattern,out pattern))return String.IsNullOrWhiteSpace(((TextPattern)pattern).DocumentRange.GetText(1));}catch{}return false;}
  static bool UiPopupBoundary(AutomationElement surface,IntPtr window,PreviewChatTarget target){try{var i=surface.Current;if(i.ProcessId!=target.ProcessId)return false;if(i.ControlType==ControlType.Window)return true;string cls=i.ClassName??"";return cls.IndexOf("Popup",StringComparison.OrdinalIgnoreCase)>=0||cls.IndexOf("Dialog",StringComparison.OrdinalIgnoreCase)>=0;}catch{return false;}}
  QtTransferState QtSurfaceState(QtTransferProof proof){
   try{
    if(!UiPopupBoundary(proof.Surface,proof.Window,proof.Target))return null;
    var state=new QtTransferState{Surface=QtComposer.Identity(proof.Surface),Visible=UiVisible(proof.Surface),Enabled=true,Empty=true,ContainsComposer=UiInside(proof.Target.AccessibleEditor,proof.Surface)};
    if(state.ContainsComposer)return state;
    state.Header=proof.Surface.Current.Name;
    if(!QtTransferRules.Header(state.Header,proof.Kind)){var headings=UiNodes(proof.Surface,ControlType.Text).Where(e=>QtTransferRules.Header(e.Current.Name,proof.Kind)).ToList();if(headings.Count!=1)return state;state.Header=headings[0].Current.Name;}
    var sends=UiNodes(proof.Surface,ControlType.Button).Where(e=>QtTransferRules.Send(e.Current.Name,proof.Kind)).ToList();state.Sends=sends.Count;
    if(sends.Count==1){proof.Send=sends[0];state.Send=QtComposer.Identity(proof.Send);object pattern;state.Enabled=proof.Send.TryGetCurrentPattern(InvokePattern.Pattern,out pattern);state.SingleFile=QtTransferRules.Normal(proof.Send.Current.Name)=="1개전송";}
    var edits=UiNodes(proof.Surface,ControlType.Edit);state.Edits=edits.Count;state.Empty=edits.All(UiEmpty);
    var r=proof.Surface.Current.BoundingRectangle;
    state.Images=UiNodes(proof.Surface,ControlType.Image).Count(e=>{var image=e.Current.BoundingRectangle;return image.Width>=Math.Max(32,r.Width/4)&&image.Height>=Math.Max(32,r.Height/10);});
    if(proof.Kind==QtTransferKind.File){string name=Path.GetFileName(proof.File);var matches=proof.Surface.FindAll(TreeScope.Descendants,new PropertyCondition(AutomationElement.NameProperty,name));state.FileMatches=matches.Cast<AutomationElement>().Any(UiVisible);
     if(!state.FileMatches){var lists=proof.Surface.FindAll(TreeScope.Descendants,new AndCondition(new PropertyCondition(AutomationElement.ControlTypeProperty,ControlType.List),new PropertyCondition(AutomationElement.NameProperty,"보낼 파일")));foreach(AutomationElement list in lists){if(!UiVisible(list))continue;var rows=UiNodes(list,ControlType.ListItem);if(rows.Count==0){var buttons=UiNodes(list,ControlType.Button).Where(e=>QtTransferRules.FileLabel(e.Current.Name,proof.File)).ToList();rows=buttons.Where(e=>!buttons.Any(parent=>parent!=e&&UiInside(e,parent))).ToList();}if(rows.Count==1&&QtTransferRules.FileLabel(rows[0].Current.Name,proof.File))state.FileMatches=true;}}// The caller binds File to one exact filename-field readback; an explicit 2+ send caption still fails the Send rule.
     if(!state.SingleFile)state.SingleFile=UiNodes(proof.Surface,ControlType.ListItem).Count==1||(state.FileMatches&&!String.IsNullOrEmpty(proof.File));}
    return state;
   }catch{return null;}
  }
  string qtTransferProblem="not-inspected";
  List<QtTransferProof> QtPreviews(PreviewChatTarget target,QtTransferKind kind,string file=null){
   qtTransferProblem="header-not-found";var found=new Dictionary<string,QtTransferProof>();var windows=OwnedWindows(target);windows.Add(target.Window);
   foreach(var window in windows){
    try{
     if(!IsWindowVisible(window)||!IsWindowEnabled(window))continue;var root=AutomationElement.FromHandle(window);var candidates=new List<AutomationElement>();
     if(window!=target.Window&&QtTransferRules.Header(Native.Title(window),kind))candidates.Add(root);
     foreach(string caption in kind==QtTransferKind.Image?QtTransferRules.ImageHeaders:QtTransferRules.FileHeaders){
      var headings=root.FindAll(TreeScope.Descendants,new PropertyCondition(AutomationElement.NameProperty,caption));
      foreach(AutomationElement header in headings){if(!UiVisible(header))continue;var parent=TreeWalker.ControlViewWalker.GetParent(header);for(int i=0;parent!=null&&i<7;i++,parent=TreeWalker.ControlViewWalker.GetParent(parent)){if(UiInside(target.AccessibleEditor,parent))break;candidates.Add(parent);}}
     }
     foreach(var surface in candidates){var proof=new QtTransferProof{Window=window,Surface=surface,Target=target,Kind=kind,File=file};var state=QtSurfaceState(proof);if(state!=null&&QtTransferRules.Header(state.Header,kind))qtTransferProblem="popup sends="+state.Sends+",edits="+state.Edits+",empty="+state.Empty+",images="+state.Images+",composer="+state.ContainsComposer+",file="+state.FileMatches+",single="+state.SingleFile;if(!QtTransferRules.Valid(state,kind))continue;proof.SurfaceId=state.Surface;proof.SendId=state.Send;string key=window+":"+proof.SendId;if(!found.ContainsKey(key))found.Add(key,proof);}
    }catch{}
   }if(found.Count>0)qtTransferProblem="confirmed-popups="+found.Count;return found.Values.ToList();
  }
  bool QtProofCurrent(QtTransferProof proof,bool requireFocus=true){
   try{if(proof==null||!SameRoom(proof.Target)||Native.GetForegroundWindow()!=proof.Window||(proof.Window!=proof.Target.Window&&!OwnedByRoom(proof.Window,proof.Target)))return false;
   uint pid;Native.GetWindowThreadProcessId(proof.Window,out pid);if(pid!=proof.Target.ProcessId)return false;
   var state=QtSurfaceState(proof);if(!QtTransferRules.Valid(state,proof.Kind)||state.Surface!=proof.SurfaceId||state.Send!=proof.SendId)return false;
   return !requireFocus||UiInside(AutomationElement.FocusedElement,proof.Surface);}catch{return false;}
  }
  bool ConfirmQtPreview(QtTransferProof proof,Func<bool> context){
   return QtTransferRules.InvokeOnce(ref proof.Attempted,()=>context()&&QtProofCurrent(proof),()=>{
    object pattern;if(!proof.Send.TryGetCurrentPattern(InvokePattern.Pattern,out pattern))throw new InvalidOperationException("Send pattern unavailable");
    if(!context()||!QtProofCurrent(proof))throw new InvalidOperationException("Preview changed");((InvokePattern)pattern).Invoke();
   });
  }
  sealed class QtPicker {
   internal IntPtr Window;internal AutomationElement Surface,Field,Open;internal string SurfaceId,FieldId,OpenId,Path;internal int Attempted;
  }
  static bool QtFilenameField(AutomationElement field){try{var i=field.Current;if(i.ControlType!=ControlType.Edit||!UiVisible(field)||i.IsPassword)return false;string id=i.AutomationId;bool known=new[]{"fileNameEdit","fileNameTextField","fileNameField","fileNameInput","1148"}.Contains(id);var label=field.GetCurrentPropertyValue(AutomationElement.LabeledByProperty) as AutomationElement;string name=label==null?i.Name:label.Current.Name;known|=new[]{"파일이름:","파일이름","Filename:","Filename"}.Contains(QtTransferRules.Normal(name));object value;return known&&field.TryGetCurrentPattern(ValuePattern.Pattern,out value)&&!((ValuePattern)value).Current.IsReadOnly;}catch{return false;}}
  List<QtPicker> QtPickers(PreviewChatTarget target){
   var result=new Dictionary<string,QtPicker>();var windows=OwnedWindows(target);windows.Add(target.Window);
   foreach(IntPtr window in windows){if(Class(window)=="#32770")continue;try{var root=AutomationElement.FromHandle(window);foreach(var open in UiNodes(root,ControlType.Button).Where(e=>FileOpenButton("1",e.Current.Name))){var parent=TreeWalker.ControlViewWalker.GetParent(open);for(int i=0;parent!=null&&i<7;i++,parent=TreeWalker.ControlViewWalker.GetParent(parent)){if(UiInside(target.AccessibleEditor,parent))break;if(!UiPopupBoundary(parent,window,target))continue;var fields=UiNodes(parent,ControlType.Edit).Where(QtFilenameField).ToList();var opens=UiNodes(parent,ControlType.Button).Where(e=>FileOpenButton("1",e.Current.Name)).ToList();if(fields.Count!=1||opens.Count!=1)continue;var picker=new QtPicker{Window=window,Surface=parent,Field=fields[0],Open=opens[0],SurfaceId=QtComposer.Identity(parent),FieldId=QtComposer.Identity(fields[0]),OpenId=QtComposer.Identity(opens[0])};if(picker.SurfaceId!=null&&picker.FieldId!=null&&picker.OpenId!=null)result[window+":"+picker.FieldId]=picker;break;}}}catch{}}
   return result.Values.ToList();
  }
  bool QtPickerCurrent(QtPicker p,PreviewChatTarget target){return p!=null&&SameRoom(target)&&Native.GetForegroundWindow()==p.Window&&(p.Window==target.Window||OwnedByRoom(p.Window,target))&&UiVisible(p.Surface)&&QtComposer.Identity(p.Surface)==p.SurfaceId&&QtComposer.Identity(p.Field)==p.FieldId&&QtComposer.Identity(p.Open)==p.OpenId&&QtFilenameField(p.Field)&&UiVisible(p.Open)&&FileOpenButton("1",p.Open.Current.Name);}
  bool SetQtFilename(QtPicker p,PreviewChatTarget target,string path){try{if(!QtPickerCurrent(p,target))return false;var value=(ValuePattern)p.Field.GetCurrentPattern(ValuePattern.Pattern);if(!String.IsNullOrWhiteSpace(value.Current.Value))return false;value.SetValue(path);p.Path=path;return value.Current.Value==path&&QtPickerCurrent(p,target);}catch{return false;}}
  bool OpenQtPicker(QtPicker p,PreviewChatTarget target,Func<bool> context){return QtTransferRules.InvokeOnce(ref p.Attempted,()=>context()&&QtPickerCurrent(p,target)&&((ValuePattern)p.Field.GetCurrentPattern(ValuePattern.Pattern)).Current.Value==p.Path,()=>{object action;if(!p.Open.TryGetCurrentPattern(InvokePattern.Pattern,out action))throw new InvalidOperationException();if(!QtPickerCurrent(p,target)||!context()||((ValuePattern)p.Field.GetCurrentPattern(ValuePattern.Pattern)).Current.Value!=p.Path)throw new InvalidOperationException();((InvokePattern)action).Invoke();});}
 }
}
