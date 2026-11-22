using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace AgitCon {
    // General reaction vocabulary only; no file IDs, hashes, packs or examples.
    static class AnalysisEvidence {
        internal static bool GentleLaughter(string query){return Regex.IsMatch(query??"",@"^\s*[ㅋㅎ][ㅋㅎ\s.!~…]*$");}
        internal static bool GentleSmile(StickerDescription item){
            if(item==null||item.TextFirst)return false;string written=WrittenReaction(item);if(written!=null&&written!="happy")return false;
            string description=item.Description??"";
            return Has(description,@"\b(smil\w*|content|cheerful)\b")&&!Has(description,@"\b(embarrass\w*|shy|awkward|affection\w*|lov\w*|heart\w*|hugg?ing|kiss\w*|cry\w*|sad|anger|angry|distress\w*|worried|confused|sleep\w*|asleep|annoyed|frown\w*|holding|carrying|camera|weapon|thumb\w*|okay gesture|peace sign|wink\w*|wav\w*|bowing|lying|laying|resting|lifting)\b");
        }
        internal static bool RelatedSmile(StickerDescription item,AutomaticScore score){return GentleSmile(item)&&score.ImageMargin>=-AutomaticRanker.MinimumGap&&Corroborated(item,"happy",score);}
        internal static double TextBonus(StickerDescription item,string query){
            if(item==null)return 0;string needle=Catalog.Normalize(query??"").Trim(),text=Catalog.Normalize(item.Text??"");
            return needle.Length>=2&&needle.Length<=80&&text.Contains(needle)?.24:0;
        }
        static bool Has(string text,string pattern){return Regex.IsMatch(text,pattern,RegexOptions.IgnoreCase|RegexOptions.CultureInvariant);}
        internal static bool CertainClaim(string text,string pattern){
            foreach(string clause in Regex.Split(text??"",@"[.,;\n]|\bbut\b|\bhowever\b")){
                if(!Has(clause,pattern))continue;
                if(Has(clause,@"\b(possibly|perhaps|maybe|might|could|seems?|appears?|appearing|apparently)\b|\b(asleep|sleeping)\s+or\b"))continue;
                if(Has(clause,@"\b(no|not|without)\s+(?:[a-z]+\s+){0,3}("+pattern+")"))continue;
                return true;
            }return false;
        }
        static bool ConcreteFood(string text){return CertainClaim(text,@"\b(cup|mug|glass|bottle|plate|bowl|beer|sandwich|bread|rice|noodles?|carrots?|apple|banana|cake|cookie|pizza|fish)\b");}
        internal static string WrittenReaction(StickerDescription item){
            string text=item.Text??"";var written=RecommendationQuery.Intent(text);if(written!=null)return written.Key;
            if(RecommendationQuery.Negated(text))return null;
            if(Has(text,@"즐거운.{0,16}보내|좋은\s*(하루|아침|저녁|주말)|잘\s*(자|가)|반갑|안녕"))return "greeting";
            if(Has(text,@"천만(?:에요|의\s*말씀)|별\s*말씀(?:을|요)"))return "thanks";
            if(Has(text,@"[ㅋ]{2,}|[ㅎ]{2,}|크크|하하"))return "laugh";return null;
        }
        internal static bool Supports(StickerDescription item,string intent,AutomaticScore score){
            if(!item.TextFirst)return Corroborated(item,intent,score);
            string written=WrittenReaction(item);if(written!=null)return written==intent;
            if(RecommendationQuery.Negated(item.Text)&&new[]{"happy","laugh","love","approval","greeting","thanks"}.Contains(intent))return false;
            if(intent=="greeting")return false; // Unread/unknown writing cannot establish an actual greeting.
            // Unknown OCR is not an explicit emotional instruction. A visible
            // expression supported independently by the image can still qualify.
            if(score.ImageMargin>=AutomaticRanker.MinimumGap&&score.RawMargin>0&&Corroborated(item,intent,score))return true;
            return score.Accepted&&(score.ImageValue>=0||score.Margin>=.03);
        }
        internal static double Rank(StickerDescription item,string intent,AutomaticScore score,double bonus){
            double value=.08+bonus+(item.TextFirst?score.Margin:.45*score.ImageMargin+.35*score.Margin+.20*score.ImageValue);
            return value+(WrittenReaction(item)==intent?.06:0);
        }
        internal static bool Corroborated(StickerDescription item,string intent,AutomaticScore score){
            if(!Reactions(item).Contains(intent))return false;
            string text=item.Text??"",description=item.Description??"";
            // Clear written Korean reactions can be useful without a facial cue.
            if(text.Count(c=>c>='가'&&c<='힣'||c>='ㄱ'&&c<='ㅣ')>=2){
                var written=RecommendationQuery.Intent(text);
                if(written!=null&&written.Key==intent)return true;
                if(intent=="greeting"&&Has(text,@"즐거운.{0,16}보내|좋은\s*(하루|아침|저녁|주말)|잘\s*(자|가)|반갑|안녕"))return true;
            }
            // A raised paw/thumb is easily captioned as "waving". Require an
            // explicit greeting or visible open-hand/bowing cue, not that verb alone.
            if(intent=="greeting"){
                if(text.Contains("\uFFFD"))return false;
                if(!CertainClaim(description,@"\b(hello|goodbye|greet\w*|bowing|open[ -]?palms?|spread(?:ing)? (?:its |the |their )?fingers|fingers (?:spread|splayed))\b"))return false;
                return score.ImageValue>=0&&score.ImageMargin>0;
            }
            if(intent=="approval"&&Has(description,@"\b(thumbs?[ -]?up|raised thumb|raising (a |the |its )?thumb)\b"))return true;
            if(intent=="love"&&Has(description,@"\b(hugg?ing|hugs?|cuddl\w*|kiss\w*|floating hearts?|heart symbols?|heart[ -]shaped cheeks)\b"))return true;
            if(intent=="food"&&ConcreteFood(description))return true;
            if(intent=="angry"&&(item.Reactions??new string[0]).Contains("disapproval")&&!Has(description,@"\b(rage|furious|enraged|shout\w*|yell\w*|scream\w*|clench\w*|steam|frustrat\w*)\b"))return false;
            if(intent=="surprise"&&!Has(description,@"\b(shock\w*|startl\w*|gasp\w*|exclamation|radial|spiky|impact lines)\b")&&score.ImageMargin<=0)return false;
            if(intent=="worry"&&!Has(description,@"\b(worr\w*|anxious|anxiety|fear\w*|fright\w*|confus\w*|concern\w*|uncertain|panic\w*|nervous)\b")&&score.ImageMargin<0)return false;
            // "Slightly sleepy" is a common misread of happy closed eyes.
            // A sleepy-only claim needs both relative document and image support.
            if(intent=="sleep"&&!CertainClaim(description,@"\b(sleeping|asleep|dozing|yawning|yawns?|zzz)\b")&&(score.Margin<=AutomaticRanker.MinimumGap||score.RawMargin<=0))return false;
            // Mood guesses need independent support from the image embedding.
            // Allow a secondary mood rather than demanding a single winner.
            if(score.ImageValue>=0&&score.ImageMargin>=-.035)return true;
            // A near-zero centered image score is not a hard contradiction.
            // Recover only a concrete expression with accepted document evidence.
            return StrongExpression(description,intent)&&score.Margin>AutomaticRanker.MinimumGap&&score.RawMargin>0&&score.ImageValue>=-.02&&score.ImageMargin>=-.025;
        }
        static bool StrongExpression(string description,string intent){
            string pattern;
            switch(intent){
                case "happy":pattern=@"\b(smil\w*|happy|joyful|cheerful)\b";break;
                case "sleep":pattern=@"\b(sleepy|sleeping|asleep|dozing|yawning|zzz)\b";break;
                default:return false;
            }
            return CertainClaim(description,pattern);
        }
        internal static HashSet<string> Reactions(StickerDescription item){
            var found=new HashSet<string>(item.Reactions??new string[0]);string description=item.Description??"",text=item.Text??"";
            string written=WrittenReaction(item);if(written!=null)return new HashSet<string>{written};
            bool shyContext=Has(description,@"\b(shy|bashful|embarrass\w*|fluster\w*|awkward|coy)\b");
            if(shyContext)found.Add("shy");else found.Remove("shy");
            if(Has(description,@"\b(hugg?ing|hugs?|cuddl\w*|kiss\w*|affection\w*|floating hearts?|heart symbols?|heart[ -]shaped cheeks)\b"))found.Add("love");
            if(Has(description,@"\b(eating|drinking|food|hungry|meal|beer|mug|cup of|holding a (cup|drink|fish))\b"))found.Add("food");
            if(Has(description,@"\b(thumbs?[ -]?up|raised thumb|raising (a |the |its )?thumb|okay gesture)\b"))found.Add("approval");
            if(Has(description,@"\b(smiling|smiles?|happy|joy\w*|cheer\w*|content|playful)\b")&&!Has(description,@"\b(not (happy|smiling)|unhappy|annoyed|displeas\w*|frown\w*)\b"))found.Add("happy");
            bool strongJoy=Has(description,@"\b(happy|joy\w*|cheer\w*|excited|celebrat\w*|laugh\w*|delighted)\b");
            if(shyContext&&!strongJoy)found.Remove("happy");
            if(Has(description,@"\b(annoyed|displeas\w*|frown\w*|unhappy|not smiling)\b")&&!strongJoy)found.Remove("happy");
            if(CertainClaim(description,@"\b(sleepy|sleeping|asleep|dozing|yawning|zzz)\b"))found.Add("sleep");
            if(CertainClaim(description,@"\b(surprised|startled|shocked|gasping)\b"))found.Add("surprise");
            if(CertainClaim(description,@"\b(unimpressed|displeased|disapprov\w*)\b"))found.Add("disapproval");
            if(ConcreteFood(description))found.Add("food");
            // Small models often mistake ordinary cartoon eyes for shock, and
            // closed smiling eyes for sleep. Demand an actual supporting cue.
            if(!Has(description,@"\b(surpris\w*|shock\w*|startl\w*|gasp\w*|exclamation|radial|spiky|impact lines|mouth (wide )?open in (shock|surprise))\b"))found.Remove("surprise");
            if(!CertainClaim(description,@"\b(sleepy|sleeping|asleep|dozing|yawning|yawns?|zzz)\b|\b(lying|laying)\b.{0,35}\b(tired|exhausted)\b"))found.Remove("sleep");
            // A supported smile is not evidence of laughter.
            if(!Has(description,@"\b(laugh\w*|giggl\w*|chuckl\w*|hilarious)\b"))found.Remove("laugh");
            if(!Has(description,@"\b(thumb\w*|okay gesture|approval|prais\w*)\b"))found.Remove("approval");
            if(!Has(description,@"\b(wav\w*|greeting|hello|goodbye|well[ -]?wish\w*)\b"))found.Remove("greeting");
            if(!Has(description,@"\b(thank\w*|gratitude|grateful)\b"))found.Remove("thanks");
            if(!Has(description,@"\b(sorry|apolog\w*|forgiveness)\b"))found.Remove("apology");
            if(!Has(description,@"\b(anger|angry|rage|frustrat\w*|furrow\w*|irritat\w*|scowl\w*)\b"))found.Remove("angry");
            if(Has(description,@"\b(crying|distress\w*|fear\w*|frighten\w*)\b")&&!Has(description,@"\b(kiss\w*|heart symbols?|floating hearts?)\b"))found.Remove("love");
            if(found.Count>1)found.Remove("neutral");return found;
        }
    }
}
