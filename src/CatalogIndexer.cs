using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace AgitCon {
    // One image at a time lets foreground queries use the model between images.
    sealed class CatalogIndexer:IDisposable {
        readonly Func<Entry,CancellationToken,Task> prepare;
        readonly Action<Entry,Entry> ready;
        readonly Action<int,int> completed;readonly bool continueOnErrors;
        CancellationTokenSource request;
        public Action<int,int,int> Progress;
        public Task Pending{get;private set;}
        public CatalogIndexer(Func<Entry,CancellationToken,Task> prepare,Action<Entry,Entry> ready,Action<int,int> completed,bool continueOnErrors=false){this.continueOnErrors=continueOnErrors;this.prepare=prepare;this.ready=ready;this.completed=completed;Pending=Task.FromResult(0);}
        public void Update(IEnumerable<Entry> entries,bool enabled){
            Stop();if(!enabled)return;
            var next=new CancellationTokenSource();request=next;
            Pending=Run(entries.ToList(),next);
        }
        async Task Run(List<Entry> entries,CancellationTokenSource owner){
            int done=0,failed=0,consecutiveFailures=0;var token=owner.Token;
            try{foreach(var source in entries){
                token.ThrowIfCancellationRequested();
                var copy=new Entry{Path=source.Path,OriginalPath=source.OriginalPath,SourceUrl=source.SourceUrl,RemoteId=source.RemoteId,PackId=source.PackId,DisplayName=source.DisplayName,Aliases=source.Aliases};
                try{await prepare(copy,token);token.ThrowIfCancellationRequested();if(request!=owner)return;ready(source,copy);done++;consecutiveFailures=0;}
                catch(OperationCanceledException){throw;}
                catch(Exception ex){failed++;consecutiveFailures++;if(!continueOnErrors&&(consecutiveFailures>=3||!(ex is IOException||ex is UnauthorizedAccessException||ex is LocalImageException||ex is System.Net.WebException)))break;}
                if(request==owner&&!token.IsCancellationRequested&&Progress!=null)Progress(done,failed,entries.Count);
                await Task.Yield();
            }
            if(request==owner&&!token.IsCancellationRequested)completed(done,failed);
            }catch(OperationCanceledException){}finally{if(request==owner)request=null;owner.Dispose();}
        }
        public void Stop(){var old=request;request=null;if(old!=null)old.Cancel();}
        public void Dispose(){Stop();}
    }
}
