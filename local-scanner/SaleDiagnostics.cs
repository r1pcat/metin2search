using System.Text.Json;

namespace Metin2Search.Local;
public sealed partial class LocalStore
{
    public bool SaveSaleDiagnostic(string watchId,JsonElement observation)
    {
        var context=observation.GetProperty("context");
        var server=context.GetProperty("serverInfo").GetString()!;
        if(ServerName!=ServerStorage.Name(server))throw new InvalidDataException("Satış tanısı başka sunucuya ait.");
        var raw=Convert.FromHexString(context.GetProperty("raw").GetString()!);
        var item=TradeDecoder.Decode(raw,context.GetProperty("slot").GetInt32());
        var id=watchId+":"+observation.GetProperty("sequence").GetInt64();
        lock(this)
        {
            if(Query("SELECT id FROM sale_diagnostics WHERE id=?",id).Count>0)return false;
            Exec("BEGIN IMMEDIATE");
            try{
                var payload=Payload(observation.GetRawText());
                Execute("INSERT INTO sale_diagnostics VALUES(?,?,?,?,?,?,?)",id,DateTimeOffset.UtcNow.ToString("O"),item.Seller,item.Vnum,observation.GetProperty("source").GetString(),payload,observation.GetProperty("preparingCandidate").GetBoolean()?1:0);
                Exec("COMMIT");return true;
            }catch{try{Exec("ROLLBACK");}catch{}throw;}
        }
    }
}

public sealed partial class MainWindow
{
    private void StartSaleDiagnostic()
    {
        if(busy||scanTask is{IsCompleted:false})return;
        if(tradeProcesses.SelectedItem is not ProcessOption process)throw new InvalidOperationException("İstemci seç.");
        scanCts?.Dispose();scanCts=new CancellationTokenSource();
        scanTask=WatchSaleDiagnostics(process.Id,scanCts.Token);UpdateButtons();
    }
    private async Task WatchSaleDiagnostics(int pid,CancellationToken token)
    {
        try
        {
            await using var bridge=new NativeTradeBridge(pid);
            var inspection=await bridge.Command("inspect",token);
            await SelectTradeServer(inspection.GetProperty("serverInfo").GetString()!,token);
            var started=await bridge.Command("salestart",token);
            if(started.GetProperty("sendsPurchase").GetBoolean())throw new InvalidDataException("Tanı modu satın alma gönderemez.");
            var watchId=Guid.NewGuid().ToString("N");long last=0;
            var path=Path.Combine(directory,"fake-tani.jsonl");
            using var writer=new StreamWriter(path,true,new System.Text.UTF8Encoding(false)){AutoFlush=true};
            SetStatus("Satış hata izlemesi açık. Yalnızca manuel işlemlerinin sonuçları kaydedilir; otomatik tarama ve satın alma yapılmaz.");
            saleDetails.Text="İzlenen istemci yolları:\n"+started.GetRawText();
            tradeProgress.Text="Tanı dosyası: "+path;
            try{
                while(true){
                    token.ThrowIfCancellationRequested();
                    var result=await bridge.Command("salepoll",token);
                    foreach(var observation in result.GetProperty("events").EnumerateArray()){
                        var sequence=observation.GetProperty("sequence").GetInt64();if(sequence<=last)continue;
                        if(store.SaveSaleDiagnostic(watchId,observation)){
                            writer.WriteLine(System.Text.Json.JsonSerializer.Serialize(new{watchId,observedAt=DateTimeOffset.UtcNow,server=store.ServerName,evidence=observation}));
                            var candidate=observation.GetProperty("preparingCandidate").GetBoolean();
                            var note=candidate?"Premium pazar hazırlık mesajı yakalandı; zaman eşleşmesi, henüz kesin FAKE kanıtı değil.":"Satış işlemi bildirimi kaydedildi.";
                            saleDetails.Text=note+"\n\n"+observation.GetRawText();
                            SetStatus(note);store.Event("SaleDiagnostic",note);events.ItemsSource=store.Events();
                        }
                        last=sequence;
                    }
                    await Task.Delay(200,token);
                }
            }finally{try{await bridge.Command("salestop",CancellationToken.None);}catch(Exception error)when(error is IOException or TimeoutException){} }
        }
        catch(OperationCanceledException)when(token.IsCancellationRequested){SetStatus("Satış hata izlemesi durduruldu; tanı kayıtları korundu.");}
        catch(Exception error){Fail(error);}
        finally{UpdateButtons();_ = Dispatcher.BeginInvoke(()=>UpdateButtons());}
    }
    private async Task SelectTradeServer(string serverInfo,CancellationToken token)
    {
        var targetDirectory=ServerStorage.DirectoryFor(rootDirectory,serverInfo);
        if(directory!=targetDirectory){var next=await Task.Run(()=>new LocalStore(targetDirectory),token);store.Dispose();store=next;directory=targetDirectory;}
        store.ConfigureServer(serverInfo);
        File.WriteAllText(Path.Combine(directory,"sunucu.json"),JsonData.Encode(new{server=ServerStorage.Name(serverInfo),clientServerInfo=serverInfo,channel=1}));
        File.WriteAllText(Path.Combine(rootDirectory,"son-sunucu.json"),JsonData.Encode(serverInfo));
        RefreshSavedServers();storagePath.Text=ServerStorage.Name(serverInfo)+" • "+store.FilePath;
        tradeRunId=null;RefreshTrade();Refresh();
    }
}
