namespace Metin2Search.Local;
public sealed record TradeCoverageRow(string Category,string Status,long Pages,long Expected,string Message)
{
    public string DisplayStatus=>Status switch{"Pending"=>"Bekliyor","Running"=>"Taranıyor","Complete"=>"Tamamlandı","Failed"=>"Hata","Interrupted"=>"Kesildi",_=>Status};
}
