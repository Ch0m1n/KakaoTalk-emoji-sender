using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace AgitCon {
    sealed class AutomaticScore {
        public double Similarity,Value,Margin,RawMargin,ImageValue,ImageMargin;
        public bool Accepted;
    }
    static class AutomaticRanker {
        internal const string PromptMode="search.sticker";
        internal const double MinimumGap=.005;
        internal const float FullImageWeight=.75f;
        internal static bool Accept(double value,double margin,double rawMargin=Double.PositiveInfinity){return value>=MinimumValue&&margin>MinimumGap&&rawMargin>0;}
        internal const double MinimumValue=0;
        static readonly Dictionary<string,string[]> compatible=new Dictionary<string,string[]> {
            {"laugh",new[]{"happy"}}, {"approval",new[]{"happy"}},
            {"sad",new[]{"worry","apology","sick"}}, {"love",new[]{"happy","shy"}},
            {"worry",new[]{"sad","surprise"}}, {"angry",new[]{"disapproval"}},
            {"sleep",new[]{"sick"}}, {"apology",new[]{"sad","worry"}},
            {"greeting",new[]{"happy"}}, {"surprise",new[]{"worry"}},
            {"happy",new[]{"laugh","approval","love","greeting"}},
            {"sick",new[]{"sad","sleep"}}, {"shy",new[]{"love"}}, {"disapproval",new[]{"angry"}}
        };
        internal static bool Competes(string target,string other){
            if(target==other)return false;string[] related;
            return target==null||!compatible.TryGetValue(target,out related)||!related.Contains(other);
        }
        static double Dot(float[] a,float[] b){double total=0;for(int i=0;i<a.Length;i++)total+=(double)a[i]*b[i];return total;}
        internal static List<AutomaticScore> Score(List<float[]> images,List<float[]> documents,float[][] concepts,string[] keys,int intentIndex,float[] freeQuery,CancellationToken cancel,IList<string> contentKeys=null){
            int count=images.Count,columns=concepts.Length+(intentIndex<0?1:0);
            if(count==0)return new List<AutomaticScore>();
            int target=intentIndex<0?columns-1:intentIndex;
            var vectors=concepts.ToList();if(intentIndex<0)vectors.Add(freeQuery);
            var raw=new double[count,columns];var mean=new double[columns];
            var cohort=contentKeys==null?Enumerable.Range(0,count).ToArray():Enumerable.Range(0,count).GroupBy(i=>contentKeys[i]).Select(group=>group.First()).ToArray();
            for(int i=0;i<count;i++){
                cancel.ThrowIfCancellationRequested();
                for(int c=0;c<columns;c++){raw[i,c]=Dot(images[i],vectors[c]);}
            }
            // Unlabelled cohort centering reduces images that match every mood.
            // Shrink this correction for small/new libraries, including one image.
            foreach(int i in cohort)for(int c=0;c<columns;c++)mean[c]+=raw[i,c]/cohort.Length;
            double strength=Math.Min(1,(cohort.Length-1)/7.0);
            // Identical feature rows contain no useful cohort variation. Keep the
            // direct semantic signal instead of subtracting it entirely.
            double variance=0;for(int c=0;c<columns;c++)foreach(int i in cohort)variance=Math.Max(variance,Math.Abs(raw[i,c]-mean[c]));
            if(variance<1e-6)strength=0;
            var competitors=Enumerable.Range(0,concepts.Length).Where(c=>c!=target&&Competes(intentIndex<0?null:keys[target],keys[c])).ToArray();
            var result=new List<AutomaticScore>();
            for(int i=0;i<count;i++){
                cancel.ThrowIfCancellationRequested();
                double value=raw[i,target]-strength*mean[target];
                double other=competitors.Max(c=>raw[i,c]-strength*mean[c]);
                double margin=value-other,rawMargin=raw[i,target]-competitors.Max(c=>raw[i,c]);
                double imageValue=value,imageMargin=margin;
                if(documents[i]!=null){
                    double documentValue=Dot(documents[i],vectors[target])-strength*mean[target];
                    double documentOther=competitors.Max(c=>Dot(documents[i],vectors[c])-strength*mean[c]);
                    double documentRawMargin=Dot(documents[i],vectors[target])-competitors.Max(c=>Dot(documents[i],vectors[c]));
                    value=.85*documentValue+.15*value;margin=.85*(documentValue-documentOther)+.15*margin;rawMargin=.85*documentRawMargin+.15*rawMargin;
                }
                result.Add(new AutomaticScore{Similarity=raw[i,target],Value=value,Margin=margin,RawMargin=rawMargin,ImageValue=imageValue,ImageMargin=imageMargin,Accepted=Accept(value,margin,rawMargin)});
            }
            return result;
        }
    }
}
