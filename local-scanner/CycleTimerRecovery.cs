using System.Text.Json;

namespace Metin2Search.Local;

internal static class CycleTimerRecovery
{
    internal static bool IsPremature(string root,CycleJournal journal,int minutes)
    {
        var file=Path.Combine(ServerStorage.DirectoryFor(root,journal.Server),"market.sqlite");
        if(!File.Exists(file))return false;
        try{
            using var reader=LocalStore.OpenReadOnly(file);
            return Matches(journal,minutes,reader.Query("SELECT occurred,message FROM events WHERE kind='CycleState' ORDER BY id DESC LIMIT 2")
                .Select(row=>((string)row[0]!, (string)row[1]!)).ToArray());
        }catch(Exception error)when(error is IOException or InvalidOperationException or JsonException or FormatException or KeyNotFoundException){return false;}
    }
    internal static bool Matches(CycleJournal journal,int minutes,(string Occurred,string Message)[] rows)
    {
        if(journal.Phase!="Depositing"||!journal.ForceFull||rows.Length!=2||minutes<1)return false;
        using var depositing=JsonDocument.Parse(rows[0].Message);using var scanning=JsonDocument.Parse(rows[1].Message);
        bool Same(JsonElement item,string phase)=>item.GetProperty("Phase").GetString()==phase
            &&item.GetProperty("CurrentSlot").GetInt32()==journal.CurrentSlot&&item.GetProperty("NextSlot").GetInt32()==journal.NextSlot
            &&item.GetProperty("InventorySlot").GetInt32()==journal.InventorySlot&&item.GetProperty("StorageSlot").GetInt32()==journal.StorageSlot
            &&item.GetProperty("ForceFull").GetBoolean();
        if(!Same(depositing.RootElement,"Depositing")||!Same(scanning.RootElement,"Scanning"))return false;
        var started=DateTimeOffset.Parse(rows[1].Occurred);var attempted=DateTimeOffset.Parse(rows[0].Occurred);
        return started-journal.StartedAt>=TimeSpan.FromMinutes(minutes)&&attempted>=started&&attempted<started.AddMinutes(minutes);
    }
}
