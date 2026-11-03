using System.Text;
using System.Text.RegularExpressions;

namespace Metin2Search.Local;
public static class ServerStorage
{
    public static string Name(string info)
    {
        var name=Regex.Replace(info.Trim(),@"\s*[,\-/|]\s*(?:CH|CHANNEL|KANAL)\s*\d+\s*$","",RegexOptions.IgnoreCase).Trim();
        if(string.IsNullOrWhiteSpace(name))throw new InvalidDataException("Sunucu adı boş; tarama durdu.");return name.Normalize();
    }
    public static string DirectoryFor(string root,string info)
    {
        var name=Name(info);var clean=new string(name.Normalize().Select(c=>Path.GetInvalidFileNameChars().Contains(c)?'_':c).ToArray()).Trim(' ','.');
        if(clean.Length>80)clean=clean[..80];if(clean.Length==0)clean="Sunucu";
        var suffix=ItemDecoder.Hash(Encoding.UTF8.GetBytes(name.Normalize()))[..10];
        return Path.Combine(root,"Sunucular",clean+"-"+suffix);
    }
}
