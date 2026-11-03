using System.Numerics;
using System.Text;

namespace Metin2Search.Local;

public sealed record ShopActor(uint Vid, ulong Instance, string Name, byte[] RawName, Vector3 Position)
{
    public bool NameKnown => !string.IsNullOrWhiteSpace(Name);
    public string DisplayName => NameKnown ? Name : "Bilinmiyor";
    public byte[] RawNameHeader { get; init; } = [];
    public string? NameDiagnostic { get; init; }
}
public sealed record NativeShopState(uint Channel, uint Open, uint Private, uint TargetVid);
public sealed record ProjectionState(Matrix4x4 World, Matrix4x4 View, Matrix4x4 Projection, int X, int Y, int Width, int Height, float MinZ, float MaxZ);

public static class MarketGeometry
{
    public static Vector2? Project(Vector3 position, ProjectionState state)
    {
        var clip = Vector4.Transform(new Vector4(position, 1), state.World * state.View * state.Projection);
        if (!float.IsFinite(clip.W) || clip.W <= 0.00001f) return null;
        var x = clip.X / clip.W; var y = clip.Y / clip.W; var z = clip.Z / clip.W;
        if (!float.IsFinite(x) || !float.IsFinite(y) || !float.IsFinite(z) || z < 0 || z >= 1 || Math.Abs(x) > 1 || Math.Abs(y) > 1) return null;
        return new Vector2(state.X + (x + 1) * state.Width / 2, state.Y + (1 - y) * state.Height / 2);
    }
    public static Matrix4x4 Matrix(byte[] raw) => new(
        F(raw, 0), F(raw, 4), F(raw, 8), F(raw, 12), F(raw, 16), F(raw, 20), F(raw, 24), F(raw, 28),
        F(raw, 32), F(raw, 36), F(raw, 40), F(raw, 44), F(raw, 48), F(raw, 52), F(raw, 56), F(raw, 60));
    public static float F(byte[] raw, int at) => BitConverter.Int32BitsToSingle(unchecked((int)ItemDecoder.U32(raw, at)));
}

public static class ActorCatalog
{
    public static List<ShopActor> Read(Func<ulong, int, byte[]> read, ulong manager, Encoding encoding)
    {
        var info = read(manager + 0x20, 8);
        var head = ItemDecoder.U32(info, 0); var size = ItemDecoder.U32(info, 4);
        if (head < 0x10000 || size > 20000) throw new InvalidDataException("Karakter ağacı geçersiz; tarama başlatılmadı.");
        var sentinel = read(head, 24);
        if (sentinel[13] != 1) throw new InvalidDataException("Karakter ağacı sentinel alanı eşleşmedi.");
        var stack = new Stack<uint>(); stack.Push(ItemDecoder.U32(sentinel, 4));
        var seen = new HashSet<uint>(); var actors = new List<ShopActor>();
        while (stack.TryPop(out var node))
        {
            if (node == head) continue;
            if (node < 0x10000 || !seen.Add(node) || seen.Count > size + 8) throw new InvalidDataException("Karakter ağacı okuma sırasında değişti veya döngü içeriyor.");
            var entry = read(node, 24);
            if (entry[13] != 0) throw new InvalidDataException("Karakter ağacı düğümü geçersiz.");
            stack.Push(ItemDecoder.U32(entry, 0)); stack.Push(ItemDecoder.U32(entry, 8));
            var vid = ItemDecoder.U32(entry, 16); var instance = ItemDecoder.U32(entry, 20);
            if (instance < 0x10000) throw new InvalidDataException("Karakter işaretçisi geçersiz.");
            if (ItemDecoder.U32(read(instance + 0x744, 4), 0) != 12) continue;
            var positionRaw = read(instance + 0x7c4, 12);
            var position = new Vector3(MarketGeometry.F(positionRaw, 0), MarketGeometry.F(positionRaw, 4), MarketGeometry.F(positionRaw, 8));
            if (!float.IsFinite(position.X) || !float.IsFinite(position.Y) || !float.IsFinite(position.Z)) throw new InvalidDataException("Pazar konumu geçersiz.");
            var nameInfo = Array.Empty<byte>(); var rawName = Array.Empty<byte>(); var name = ""; string? nameDiagnostic = null;
            try
            {
                nameInfo = read(instance + 0x18, 24);
                var length = ItemDecoder.U32(nameInfo, 16); var capacity = ItemDecoder.U32(nameInfo, 20);
                if (length == 0) nameDiagnostic = "Oyuncu adı boş; pazar VID ile izleniyor.";
                else if (length > 64 || capacity < length || capacity > 1024 * 1024 || (capacity <= 15 && length > 15))
                    nameDiagnostic = $"Oyuncu adı yerleşimi eşleşmedi: length={length}, capacity={capacity}; VID={vid}.";
                else
                {
                    var namePointer = ItemDecoder.U32(nameInfo, 0);
                    if (capacity > 15 && namePointer < 0x10000) nameDiagnostic = "Oyuncu adı işaretçisi geçersiz.";
                    else
                    {
                        rawName = capacity <= 15 ? nameInfo.AsSpan(0, (int)length).ToArray() : read(namePointer, (int)length);
                        var decoded = encoding.GetString(rawName);
                        if (decoded.Any(char.IsControl)) nameDiagnostic = "Oyuncu adı kontrol karakterleri içeriyor.";
                        else name = decoded;
                    }
                }
            }
            catch (IOException error)
            {
                nameDiagnostic = "Oyuncu adı okunamadı: " + error.Message;
                if (error is MarketReadException partial && nameInfo.Length == 0) nameInfo = partial.Diagnostics.FirstOrDefault()?.Bytes ?? [];
            }
            actors.Add(new ShopActor(vid, instance, name, rawName, position) { RawNameHeader = nameInfo, NameDiagnostic = nameDiagnostic });
        }
        if (seen.Count != size || !info.AsSpan().SequenceEqual(read(manager + 0x20, 8)))
            throw new InvalidDataException("Pazar listesi okuma sırasında değişti; yeniden denenmeli.");
        return actors;
    }
}

public sealed partial class ExternalReader
{
    public bool NativeLayoutValidated { get; private set; }
    public IntPtr WindowHandle { get { process.Refresh(); return process.MainWindowHandle; } }
    public ReaderProfile Profile => profile;
    private bool VerifyNativeLayout()
    {
        var entries = new (string Entry, string Function)[]
        {
            ("0x031EE2CC", "0x02DFA6F0"),
            ("0x031EE2DC", "0x02DFA890"),
            ("0x031EE38C", "0x02DFA670")
        };
        return entries.All(e => U32(e.Entry) == Resolve(e.Function));
    }
    public NativeShopState ReadNativeState()
    {
        if (!NativeLayoutValidated) throw new InvalidOperationException("Çalışan istemci metot tablosu araştırılan sürümle eşleşmedi.");
        var player = U32(profile.PlayerPointerAddress);
        if (player < 0x10000) throw new InvalidDataException("Oyuncu henüz yüklenmedi.");
        var channel = Read(player + (ulong)profile.ChannelByteOffset, 1)[0];
        if (channel != 1) throw new InvalidOperationException($"Kanal 1 gerekli; istemcide okunan kanal {channel}. Tarama durdu.");
        var target = ItemDecoder.U32(Read(player + (ulong)profile.TargetVidOffset, 4), 0);
        var flags = Read(Pointer() + 0x34, 12);
        var open = ItemDecoder.U32(flags, 0); var priv = ItemDecoder.U32(flags, 4);
        if (open > 1 || priv > 1) throw new InvalidDataException("Pazar açık/özel bayrakları geçersiz.");
        return new NativeShopState(channel, open, priv, target);
    }
    public List<ShopActor> ReadShops()
    {
        _ = ReadNativeState();
        var manager = U32(profile.CharacterManagerPointerAddress);
        if (manager < 0x10000) throw new InvalidDataException("Karakter yöneticisi henüz yüklenmedi.");
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return ActorCatalog.Read(Read, manager, Encoding.GetEncoding(profile.OwnerNameCodePage));
    }
    public Vector3 PlayerPosition()
    {
        var manager = U32(profile.CharacterManagerPointerAddress);
        var instance = ItemDecoder.U32(Read(manager + 0x14, 4), 0);
        if (instance < 0x10000) throw new InvalidDataException("Ana karakter yüklenmedi.");
        var raw = Read(instance + 0x7c4, 12);
        var position = new Vector3(MarketGeometry.F(raw, 0), MarketGeometry.F(raw, 4), MarketGeometry.F(raw, 8));
        if (!float.IsFinite(position.X) || !float.IsFinite(position.Y) || !float.IsFinite(position.Z)) throw new InvalidDataException("Ana karakter konumu geçersiz.");
        return position;
    }
    public ProjectionState Projection()
    {
        var viewport = Read(Resolve(profile.ViewportAddress), 24);
        var width = checked((int)ItemDecoder.U32(viewport, 8)); var height = checked((int)ItemDecoder.U32(viewport, 12));
        if (width < 320 || height < 240 || width > 16384 || height > 16384) throw new InvalidDataException("Oyun görüntü alanı geçersiz.");
        return new ProjectionState(MarketGeometry.Matrix(Read(Resolve(profile.WorldMatrixAddress), 64)),
            MarketGeometry.Matrix(Read(Resolve(profile.ViewMatrixAddress), 64)), MarketGeometry.Matrix(Read(Resolve(profile.ProjectionMatrixAddress), 64)),
            (int)ItemDecoder.U32(viewport, 0), (int)ItemDecoder.U32(viewport, 4), width, height, MarketGeometry.F(viewport, 16), MarketGeometry.F(viewport, 20));
    }
    private async Task<MarketCapture> CaptureNativeAsync(string server, string map, string label, CancellationToken token)
    {
        var state = ReadNativeState();
        if (state.Open != 1 || state.Private != 1) throw new InvalidOperationException("Açık oyuncu pazarı bulunamadı; eski ürün tamponu kaydedilmedi.");
        var actor = ReadShops().SingleOrDefault(a => a.Vid == state.TargetVid);
        var pointer = Pointer(); var before = Read(pointer, 0x44dd);
        await Task.Delay(200, token);
        var after = Read(pointer, 0x44dd);
        if (pointer != Pointer() || state != ReadNativeState() || !before.AsSpan().SequenceEqual(after))
            throw new MarketReadException("Pazar değişti; tutarsız anlık görüntü ilan olarak kaydedilmedi.",
                new(pointer, before.Length, before, "first-snapshot"), new(pointer, after.Length, after, "second-snapshot"));
        var tabs = after[0x40];
        if (tabs is < 1 or > 3) throw new MarketReadException("Geçersiz sekme sayısı.", new ReadDiagnostic(pointer, after.Length, after, "invalid-tabs"));
        var slots = new List<SlotData>();
        for (var t = 0; t < tabs; t++) for (var s = 0; s < 40; s++) slots.Add(new(t, s, after.AsSpan(0x60 + t * 0x141c + s * 128, 128).ToArray()));
        return new MarketCapture
        {
            Source = "Live", Server = server, Map = map, ShopLabel = label,
            ShopVid = actor?.Vid.ToString(), OwnerName = actor?.NameKnown == true ? actor.Name : null, OwnerVerified = actor?.NameKnown == true, RawOwnerName = actor?.RawName,
            RawOwnerNameHeader = actor?.RawNameHeader, OwnerDiagnostic = actor?.NameDiagnostic,
            ChannelVerified = true, PricesVerified = true, IsComplete = true, ItemLayout = "shop-v2", ProfileVersion = profile.Version,
            ClientHash = ExecutableHash, RawManager = after, Slots = slots,
            Warnings = actor == null ? ["Açık pazarın hedef VID'si karakter listesinde bulunamadı; pazar sahibi bilinmiyor."] :
                actor.NameKnown ? [] : [actor.NameDiagnostic ?? "Oyuncu adı bulunamadı; ürünler VID ile kaydedildi."]
        };
    }
}
