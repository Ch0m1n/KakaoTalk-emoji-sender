using System;using System.Collections.Generic;using System.IO;using System.Linq;using System.Threading;
namespace AgitCon {
 // Read-only PNG bytes: keep broad preload coverage without decoding every poster.
 internal static class EncodedPreviews {
  sealed class Item{internal byte[] Bytes;internal long Touch;}
  internal const long Limit=32L*1024*1024;const int CountLimit=2048;
  static readonly object sync=new object();static readonly Dictionary<string,Item> items=new Dictionary<string,Item>(StringComparer.OrdinalIgnoreCase);static long bytes,clock;
  internal static bool TryGet(string key,out byte[] value){lock(sync){Item item;if(items.TryGetValue(key,out item)){item.Touch=++clock;value=item.Bytes;return true;}value=null;return false;}}
  internal static void Store(string key,byte[] value){lock(sync){RemoveCore(key);if(value.Length>Limit)return;items[key]=new Item{Bytes=value,Touch=++clock};bytes+=value.Length;if(bytes<=Limit&&items.Count<=CountLimit)return;foreach(var old in items.OrderBy(p=>p.Value.Touch).ToArray()){if(bytes<=Limit&&items.Count<=CountLimit)break;RemoveCore(old.Key);}}}
  static void RemoveCore(string key){Item old;if(items.TryGetValue(key,out old)){bytes-=old.Bytes.Length;items.Remove(key);}}
  internal static void Remove(string key){lock(sync)RemoveCore(key);}
  internal static long Bytes{get{lock(sync)return bytes;}}
  internal static string Status{get{lock(sync)return "압축 "+items.Count+"개 · "+(bytes/1048576.0).ToString("0.0")+" / 32 MB";}}
  internal static void Test(){string prefix="fixture-"+Guid.NewGuid().ToString("N");int checks=0;Action<bool> check=ok=>{if(!ok)throw new Exception("Thumbnail memory check "+checks);checks++;};try{
   for(int i=0;i<40;i++)Store(prefix+i,new byte[1024*1024]);check(Bytes<=Limit);byte[] found;check(!TryGet(prefix+"0",out found));check(TryGet(prefix+"39",out found)&&found.Length==1024*1024);check(TryGet(prefix+"8",out found));Store(prefix+"40",new byte[1024*1024]);check(TryGet(prefix+"8",out found)&&!TryGet(prefix+"9",out found));Store(prefix+"39",new byte[10]);check(TryGet(prefix+"39",out found)&&found.Length==10);Remove(prefix+"39");check(!TryGet(prefix+"39",out found));
   File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"thumbnail-memory-test.txt"),"PASS "+checks+" bounded compressed cache / LRU / replacement / explicit invalidation checks. No UI, model, clipboard or network.");
  }catch(Exception ex){File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"thumbnail-memory-test.txt"),"FAIL "+ex);Environment.ExitCode=1;}finally{for(int i=0;i<=40;i++)Remove(prefix+i);}}
 }
}
