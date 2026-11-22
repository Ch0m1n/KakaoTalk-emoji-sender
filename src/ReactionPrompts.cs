using System;
using System.Linq;
using System.Text.RegularExpressions;

namespace AgitCon {
    // General concepts, identical for every imported image and every user.
    static class ReactionPrompts {
        internal static readonly string[] ShortEnglish={
            "laughter, laughing, funny", "approval, thumbs up, good job", "crying, sadness, tears",
            "love, affection, heart", "confusion, anxiety, concern", "anger, rage, irritation",
            "sleeping, sleepy, tired", "plain text, a written letter, neutral expression",
            "sorry, apology, apologizing", "hello, goodbye, waving", "surprise, shock",
            "thank you, thanks, gratitude", "happiness, joy, excitement", "sick, illness, runny nose, vomiting",
            "eating, food, hungry", "shy, embarrassed, blushing", "not amused, unimpressed, disapproval"
        };
        internal static readonly string[] NegativeEnglish={"not funny, not laughing","bad job, disapproval, rejection","not sad, happy without tears","hate, dislike, not love","confident, not worried","calm, not angry","awake, energetic","an emotional face","not sorry, unapologetic","ignoring someone, not greeting","not surprised, unimpressed","ungrateful, no thanks","unhappy, not excited","healthy, feeling well","not hungry, full","confident, not embarrassed","amused, impressed"};
        internal static readonly string[] NegativeKorean={"안 웃겨, 재미없어","별로야, 잘못했어","안 슬퍼, 안 울어","싫어, 미워, 안 좋아해","괜찮아, 걱정 없어","화 안 났어, 차분해","안 졸려, 깨어있어","감정이 드러나는 표정","안 미안해, 미안하지 않아","인사 안 해, 무시","안 놀랐어, 예상했어","안 고마워, 감사하지 않아","안 기뻐, 안 행복해","안 아파, 건강해","안 배고파, 배불러","안 부끄러워, 당당해","재밌어, 만족해"};
        internal static string Text(ReactionIntent reaction,string mode){
            int index=Array.IndexOf(RecommendationQuery.Reactions,reaction);
            if(mode=="negative.en")return NegativeEnglish[index];
            if(mode=="negative.ko")return NegativeKorean[index];
            if(mode=="search.short-en")return ShortEnglish[index];
            if(mode=="search.short-ko")return reaction.Terms.Length>0?String.Join(", ",reaction.Terms):reaction.Description.Substring(Regex.Match(reaction.Description,@"[가-힣]").Index).Trim();
            if(mode=="search.sticker")return "A reaction sticker expressing "+ShortEnglish[index]+".";
            int korean=Regex.Match(reaction.Description,@"[가-힣]").Index;
            return mode.EndsWith(".en")?reaction.Description.Substring(0,korean).Trim():mode.EndsWith(".ko")?reaction.Description.Substring(korean).Trim():reaction.Description;
        }
    }
}
