using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Automation;

namespace AgitCon {
    sealed class ReactionIntent {
        public readonly string Key,Description;public readonly string[] Terms;
        public ReactionIntent(string key,string description,params string[] terms){Key=key;Description=description;Terms=terms;}
    }
    static class RecommendationQuery {
        internal static readonly ReactionIntent[] Reactions={
            new ReactionIntent("laugh","A character laughing out loud with an open mouth, laughing hysterically, hilarious, LOL. 크게 웃는 캐릭터, 빵 터진 웃음, 폭소.","웃음","폭소","ㅋㅋ","ㅎㅎ","재밌어","웃겨"),
            new ReactionIntent("approval","A character giving a thumbs up, good job, approval, well done. 엄지를 치켜들며 칭찬하는 캐릭터, 엄지척, 좋아요, 잘했어, 최고.","굳","굿","엄지척","좋아요","최고","잘했어","good","nice"),
            new ReactionIntent("sad","A sticker of someone weeping with visible tears running down their face, crying, sobbing. 눈물이 흐르는 슬픈 울음, 엉엉, 슬픔.","슬픔","울음","슬퍼","ㅠㅠ","ㅜㅜ"),
            new ReactionIntent("love","A sticker with heart symbols, expressing affection and a romantic confession. 사랑해, 좋아해, 하트 두 개, 애정.","사랑","사랑해","좋아해","하트"),
            new ReactionIntent("worry","A confused character with a question mark, anxious and uncertain expression. 당황한 표정, 물음표, 걱정, 어리둥절.","당황","걱정","무슨일","당황했어"),
            new ReactionIntent("angry","An enraged character shouting angrily, furrowed eyebrows, an angry expression. 화난 얼굴, 분노, 짜증, 격노.","화나","분노","짜증","빡침"),
            new ReactionIntent("sleep","A sleeping or bored character, tired and lying down. 잠자는 모습, 지루함, 피곤함.","졸려","피곤","잠","졸림"),
            new ReactionIntent("neutral","A plain handwritten letter or neutral character holding an object without emotion. 단순한 글자, 무표정, 물건을 들고 있는 캐릭터."),
            new ReactionIntent("apology","A sticker that says sorry, an apologetic expression, asking for forgiveness. 미안해요, 죄송해요, 사과.","미안","미안해","죄송","사과"),
            new ReactionIntent("greeting","A character waving hello or goodbye, greeting someone. 안녕, 손을 흔들며 인사하는 모습.","안녕","반가워","잘가","인사"),
            new ReactionIntent("surprise","A shocked or surprised character, eyes wide open, unexpected news. 깜짝 놀란 표정, 충격, 놀람.","놀람","깜짝","충격","실화냐"),
            new ReactionIntent("thanks","A sticker that says thank you, gratitude, a grateful bow. 고마워요, 감사합니다, 감사 인사.","고마워","감사","고맙"),
            new ReactionIntent("happy","A joyful character smiling happily, cheerful and excited. 행복한 미소, 기쁜 표정, 신남.","행복","신남","기쁨","미소"),
            new ReactionIntent("sick","A sick character with a runny nose or vomiting, feeling unwell. 감기, 콧물, 구토, 아픈 모습.","아파","감기","콧물","구토"),
            new ReactionIntent("food","A character eating food or holding a fish to eat, hungry. 음식 먹기, 식사, 배고픔.","먹기","식사","배고파","생선"),
            new ReactionIntent("shy","A shy character blushing and looking embarrassed. 부끄러움, 수줍음, 머쓱함.","수줍음","부끄러움","머쓱"),
            new ReactionIntent("disapproval","An unimpressed character staring silently with a straight face, not amused. 정색, 어이없음, 재미없어.","정색","안웃겨","재미없어","어이없음")
        };
        static string ShortForm(string text){return Regex.Replace((text??"").Trim().ToLowerInvariant(),@"[\s.!~?]+","");}
        internal static ReactionIntent Intent(string text){
            string shortText=ShortForm(text);
            string slangKey=null;
            if(new[]{"ㅎㅇ","ㅂㅂ"}.Contains(shortText))slangKey="greeting";
            else if(shortText=="ㄱㅅ")slangKey="thanks";
            else if(shortText=="ㅈㅅ")slangKey="apology";
            else if(new[]{"ㅇㅋ","ㅇㅇ","ㄱㄱ","ㅅㄱ","오케이","수고했어"}.Contains(shortText))slangKey="approval";
            else if(new[]{"ㅊㅋ","축하해","축하"}.Contains(shortText))slangKey="happy";
            else if(new[]{"ㄴㄴ","노노"}.Contains(shortText))slangKey="disapproval";
            if(slangKey!=null)return Reactions.First(r=>r.Key==slangKey);
            if(Regex.IsMatch(shortText,@"[ㅋㅎ]")&&Regex.IsMatch(shortText,@"[ㅠㅜ]"))return null;
            if(Regex.IsMatch(shortText,@"^[ㅋㅎ]+$"))return Reactions[0];
            if(Regex.IsMatch(shortText,@"^[ㅠㅜ]+$"))return Reactions[2];
            if(Approval(text))return Reactions[1];
            if(Regex.IsMatch(shortText,@"^(?:정색|어이없어|어이없네|어이없음|재미없어|안웃겨|안좋아|별로야|불가능|거절|안돼|안됨|싫어|싫다|아니야|아니요)(?:요)?$"))return Reactions.First(r=>r.Key=="disapproval");
            if(Regex.IsMatch(shortText,@"^(?:안녕|안녕하세요|안녕히가세요)$"))return Reactions.First(r=>r.Key=="greeting");
            // Keep negation, quotations and mixed clauses as full sentences.
            if(Negated(shortText)||Regex.IsMatch(shortText,@"[“”\""'‘’]|근데|하지만|그런데|라고|다며|이라니"))return null;
            string phrase=Regex.Replace(shortText,@"[ㅋㅎㅠㅜ]+$","");
            phrase=Regex.Replace(phrase,@"^(?:(?:아|와|진짜|너무|완전|정말|엄청|나|오늘|좀|ㅋㅋ|ㅎㅎ))+","");
            string key=null;
            if(Regex.IsMatch(phrase,@"^(?:웃겨|웃기네|웃기다|빵터짐|빵터졌어|재밌어|재미있어|lol|lmao)(?:요)?$"))key="laugh";
            else if(Approval(phrase))key="approval";
            else if(Regex.IsMatch(phrase,@"^(?:슬퍼|슬프다|눈물나|울고싶어)(?:요)?$"))key="sad";
            else if(Regex.IsMatch(phrase,@"^(?:사랑해|좋아해)(?:요)?$"))key="love";
            else if(Regex.IsMatch(phrase,@"^(?:당황했어|걱정돼|어쩌지)(?:요)?$"))key="worry";
            else if(Regex.IsMatch(phrase,@"^(?:화나|화난다|짜증나|빡쳐|열받네)(?:요)?$"))key="angry";
            else if(Regex.IsMatch(phrase,@"^(?:졸려|피곤해|잘게|잘자|자러갈게)(?:요)?$"))key="sleep";
            else if(Regex.IsMatch(phrase,@"^(?:미안|미안해|죄송해|죄송합니다)(?:요)?$"))key="apology";
            else if(Regex.IsMatch(phrase,@"^(?:안녕|반가워|잘가|하이|바이|hello|bye)(?:요)?$"))key="greeting";
            else if(Regex.IsMatch(phrase,@"^(?:깜짝이야|놀랐어|실화냐|대박|헐)(?:요)?$"))key="surprise";
            else if(Regex.IsMatch(phrase,@"^(?:고마워|고맙다|고맙습니다|감사해|감사합니다|땡큐|thanks)(?:요)?$"))key="thanks";
            if(key==null){
                var signals=new System.Collections.Generic.List<string>();
                string[] keys={"approval","sad","love","worry","angry","sleep","apology","greeting","surprise","thanks","laugh","happy","sick","food","shy","disapproval"};
                string[] patterns={@"엄지척|잘했|잘한|잘하네|최고|칭찬",@"슬퍼|슬프|슬픈|슬픔|우는|울고|눈물",@"사랑|좋아해|좋아하는|하트",@"걱정|곤란|어쩌지|당황|어리둥절",@"화난|화나|짜증|분노|빡쳐|열받",@"졸려|졸린|졸음|잠자는|잠든|잠자|수면|취침|피곤|잘자|잘게",@"미안|죄송|사과하|사과드|사과할",@"안녕|반가|인사|손흔",@"놀란|놀라|놀랐|깜짝|충격|실화",@"고마|고맙|감사|땡큐",@"웃겨|웃기|폭소|빵.?터|[ㅋㅎ]{2,}",@"행복|신나|기뻐|기쁘|기쁜|기쁨|미소|웃는|웃고",@"아파|아픈|감기|콧물|구토|토하는|몸살",@"배고|먹는|먹고|먹을|먹으|맛있|식사|음식|생선",@"부끄|수줍|머쓱|민망",@"정색|별로|불가능|가짜|거절|싫어|싫다"};
                for(int i=0;i<keys.Length;i++)if(Regex.IsMatch(shortText,patterns[i]))signals.Add(keys[i]);
                if(signals.Contains("thanks"))signals.Remove("greeting");
                if(signals.Count==2&&signals.Contains("laugh")&&signals.Contains("happy"))signals.Remove("happy");
                if(signals.Count==1)key=signals[0];
            }
            if(key!=null)return Reactions.First(r=>r.Key==key);
            return null;
        }
        internal static bool AvoidGuessing(string text){return Negated(text)||(Regex.IsMatch(text??"",@"[ㅋㅎ]")&&Regex.IsMatch(text??"",@"[ㅠㅜ]"));}
        internal static bool Negated(string text){return Regex.IsMatch(text??"",@"^안(?!녕)|안(?:웃|좋|슬|미안|고맙|감사|피곤|졸|놀|되)|않|못|아니|없");}
        internal static bool Contradicts(string intent,string other){
            if((intent=="laugh"&&other=="happy")||(intent=="happy"&&other=="laugh"))return false;
            if(intent=="approval")return new[]{"laugh","sad","love","worry","angry","sleep","neutral"}.Contains(other);
            if(intent=="sad")return new[]{"laugh","approval","love","angry","sleep","neutral"}.Contains(other);
            if(intent=="love")return new[]{"laugh","sad","worry","angry","sleep","neutral"}.Contains(other);
            if(intent=="worry")return new[]{"laugh","approval","love","angry","sleep","neutral"}.Contains(other);
            if(intent=="apology")return new[]{"laugh","approval","love","angry","sleep","neutral"}.Contains(other);
            if(intent=="surprise")return new[]{"laugh","approval","love","sleep","neutral"}.Contains(other);
            return true;
        }
        internal static bool Approval(string text){return Regex.IsMatch(ShortForm(text),@"^(?:굳|굿)+$|^(?:good|nice|나이스|엄지척|최고|좋아|좋아요|잘했어|잘했어요)$");}
        public static string Meaning(string text){var intent=Intent(text);return intent==null?(text??"").Trim():intent.Description;}
        internal static double MetadataBonus(Entry entry,string query){
            var intent=Intent(query);var terms=intent==null?new[]{Catalog.Normalize(query).Trim()}:intent.Terms;
            var aliases=(entry.Aliases??"").Split(',');string name=Catalog.Normalize(entry.DisplayName??"");double bonus=0;
            foreach(string rawTerm in terms){
                string term=Catalog.Normalize(rawTerm);
                if(String.IsNullOrWhiteSpace(term)||Regex.IsMatch(term,@"^[\d\W_]+$"))continue;
                if(aliases.Any(alias=>Catalog.Normalize(alias).Trim()==term)||name==term)bonus=Math.Max(bonus,0.16);
                else if(term.Length>=2&&(aliases.Any(alias=>!Negated(alias)&&Catalog.Normalize(alias).Contains(term))||(!Negated(name)&&name.Contains(term))))bonus=Math.Max(bonus,0.08);
            }
            return bonus;
        }
    }
    sealed class DraftResult {public string Text,Reason;public bool Supported;}
    sealed class DraftEditorInfo {public IntPtr Handle;public int Process;public bool Editable,Focused,Password,ReadOnly;}
    static class FocusedDraft {
        static int reading;
        // No tree enumeration, ancestor text, WM_GETTEXT or keyboard hooks.
        internal static DraftResult GuardedRead(PreviewChatTarget target,DraftEditorInfo info,Func<bool> stillFocused,Func<string> read){
            if(target==null||info.Handle!=target.Editor||info.Process!=target.ProcessId||!info.Editable||!info.Focused||info.Password||info.ReadOnly||!stillFocused())return new DraftResult{Reason="입력창을 확인하지 못했어요 · 직접 검색해주세요."};
            string text=read()??"";
            if(!stillFocused())return new DraftResult{Reason="포커스가 바뀌어 읽은 문장을 사용하지 않았어요."};
            if(text.Length>2048)return new DraftResult{Reason="긴 문장은 직접 검색해주세요."};
            return new DraftResult{Supported=true,Text=text.TrimEnd('\r','\n')};
        }
        static DraftResult Read(PreviewChatTarget target){try{
            if(!PreviewPaste.IsFocused(target))return new DraftResult{Reason="카톡 메시지 입력창에 포커스를 두세요."};
            var element=AutomationElement.FocusedElement;if(element==null)return new DraftResult{Reason="접근성 입력창을 제공하지 않아요 · 직접 검색해주세요."};
            var current=element.Current;
            var info=new DraftEditorInfo{Handle=new IntPtr(current.NativeWindowHandle),Process=current.ProcessId,Editable=current.ControlType==ControlType.Edit||current.ControlType==ControlType.Document,Focused=current.HasKeyboardFocus,Password=current.IsPassword,ReadOnly=true};
            // Check identity before requesting any content pattern.
            if(info.Handle!=target.Editor||info.Process!=target.ProcessId||!info.Editable||!info.Focused||info.Password)return new DraftResult{Reason="카톡 입력창 접근성이 지원되지 않아요 · 직접 검색해주세요."};
            object pattern;
            if(element.TryGetCurrentPattern(TextPattern.Pattern,out pattern)){
                var text=(TextPattern)pattern;var range=text.DocumentRange;object readOnly=range.GetAttributeValue(TextPattern.IsReadOnlyAttribute);info.ReadOnly=!(readOnly is bool)||((bool)readOnly);
                return GuardedRead(target,info,()=>PreviewPaste.IsFocused(target),()=>range.GetText(2049));
            }
            if(element.TryGetCurrentPattern(ValuePattern.Pattern,out pattern)){
                var value=(ValuePattern)pattern;info.ReadOnly=value.Current.IsReadOnly;
                return GuardedRead(target,info,()=>PreviewPaste.IsFocused(target),()=>value.Current.Value);
            }
            return new DraftResult{Reason="카톡이 작성 문장을 제공하지 않아요 · 직접 검색해주세요."};
        }catch{return new DraftResult{Reason="카톡 입력창을 읽지 못했어요 · 직접 검색해주세요."};}}
        public static async Task<DraftResult> TryRead(PreviewChatTarget target){
            if(Interlocked.CompareExchange(ref reading,1,0)!=0)return new DraftResult{Reason="입력창 접근성 응답 대기 중 · 직접 검색할 수 있어요."};
            var result=new TaskCompletionSource<DraftResult>();var thread=new Thread(()=>{try{result.TrySetResult(Read(target));}finally{Interlocked.Exchange(ref reading,0);}}){IsBackground=true,Name="AgitCon focused composer accessibility"};thread.SetApartmentState(ApartmentState.MTA);thread.Start();
            if(await Task.WhenAny(result.Task,Task.Delay(350))!=result.Task)return new DraftResult{Reason="접근성 응답이 늦어요 · 직접 검색해주세요."};return await result.Task;
        }
        public static void Test(){try{int reads=0,count=0;Action<bool> check=ok=>{if(!ok)throw new Exception("Draft privacy check "+count);count++;};var target=new PreviewChatTarget{Editor=new IntPtr(42),ProcessId=10};var info=new DraftEditorInfo{Handle=target.Editor,Process=10,Editable=true,Focused=true};Func<string> read=()=>{reads++;return "ㅋㅋㅋ";};check(GuardedRead(target,info,()=>true,read).Text=="ㅋㅋㅋ"&&reads==1);info.ReadOnly=true;check(!GuardedRead(target,info,()=>true,read).Supported&&reads==1);info.ReadOnly=false;info.Password=true;check(!GuardedRead(target,info,()=>true,read).Supported&&reads==1);info.Password=false;info.Handle=new IntPtr(99);check(!GuardedRead(target,info,()=>true,read).Supported&&reads==1);info.Handle=target.Editor;info.Process=99;check(!GuardedRead(target,info,()=>true,read).Supported&&reads==1);info.Process=10;info.Editable=false;check(!GuardedRead(target,info,()=>true,read).Supported&&reads==1);info.Editable=true;check(!GuardedRead(target,info,()=>false,read).Supported&&reads==1);int focusChecks=0;check(!GuardedRead(target,info,()=>++focusChecks==1,read).Supported&&reads==2);check(!GuardedRead(target,info,()=>true,()=>new string('가',2049)).Supported);check(RecommendationQuery.Meaning("ㅋㅋㅋㅋ").Contains("웃음"));check(RecommendationQuery.Meaning("ㅎㅎ").Contains("웃음"));check(RecommendationQuery.Meaning("ㅠㅠ").Contains("슬픔"));check(RecommendationQuery.Meaning("고마워요").Contains("감사"));File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"draft-test.txt"),"PASS: "+count+" focused-input privacy/query checks. No UI Automation access, KakaoTalk, input, clipboard or GPU used.\r\n");}catch(Exception ex){File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"draft-test.txt"),"FAIL: "+ex.Message);Environment.ExitCode=1;}}
    }
}
