using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Metin2Search.Local;

public static class JsonData
{
    public static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true, PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };
    public static string Encode<T>(T value) => JsonSerializer.Serialize(value, Options);
    public static T Decode<T>(string text) => JsonSerializer.Deserialize<T>(text, Options)
        ?? throw new InvalidDataException("JSON içeriği boş.");
}

public sealed record Bonus(ushort Type, short Value, byte Unknown);
public sealed record ExtraBonus(ushort Type, uint RawValue);
public sealed record ReadDiagnostic(ulong Address, int RequestedBytes, byte[] Bytes, string Stage);
public sealed class MarketReadException(string message, params ReadDiagnostic[] diagnostics) : IOException(message)
{
    public ReadDiagnostic[] Diagnostics { get; } = diagnostics;
}
public sealed record SlotData(int Tab, int Slot, byte[] Raw);
public sealed record MarketCapture
{
    public int SchemaVersion { get; init; } = 1;
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public DateTimeOffset ObservedAt { get; init; } = DateTimeOffset.UtcNow;
    public string Source { get; init; } = "Import";
    public string Server { get; init; } = "Belirtilmedi";
    public int Channel { get; init; } = 1;
    public bool ChannelVerified { get; init; }
    public string Map { get; init; } = "Belirtilmedi";
    public string ShopLabel { get; init; } = "Kimliği doğrulanmadı";
    public string? ShopVid { get; init; }
    public string? OwnerName { get; init; }
    public bool OwnerVerified { get; init; }
    public byte[]? RawOwnerName { get; init; }
    public byte[]? RawOwnerNameHeader { get; init; }
    public string? OwnerDiagnostic { get; init; }
    public string ItemLayout { get; init; } = "legacy-v1";
    public string? ClientHash { get; init; }
    public string ProfileVersion { get; init; } = "ghidra-2026-10-14-unverified";
    public bool PricesVerified { get; init; }
    public bool IsComplete { get; init; } = true;
    public string[] Warnings { get; init; } = [];
    public byte[]? RawManager { get; init; }
    public string? ImportedJson { get; init; }
    public List<SlotData> Slots { get; init; } = [];

    public void Validate()
    {
        if (SchemaVersion != 1) throw new InvalidDataException("Desteklenmeyen kayıt sürümü.");
        if (string.IsNullOrWhiteSpace(Id) || Id.Length > 128) throw new InvalidDataException("Geçersiz kayıt kimliği.");
        if (Channel != 1) throw new InvalidDataException("Bu yerel test yalnızca Kanal 1 içindir.");
        if (Slots is null || Slots.Count > 120) throw new InvalidDataException("En fazla 120 yuva kabul edilir.");
        var seen = new HashSet<(int, int)>();
        foreach (var slot in Slots)
        {
            if (slot.Tab < 0 || slot.Tab > 2 || slot.Slot < 0 || slot.Slot > 39 || slot.Raw is null || slot.Raw.Length != 128)
                throw new InvalidDataException("Her yuva 0–2 sekme, 0–39 yuva ve 128 ham bayt içermeli.");
            if (!seen.Add((slot.Tab, slot.Slot))) throw new InvalidDataException("Aynı yuva iki kez verilmiş.");
        }
        if (RawManager?.Length > 0x10000) throw new InvalidDataException("Pazar nesnesi boyutu sınırı aşıldı.");
        if (Warnings is null) throw new InvalidDataException("Uyarı listesi geçersiz.");
    }
}

public sealed record ItemRow
{
    public string CaptureId { get; init; } = "";
    public string Source { get; init; } = "";
    public string Shop { get; init; } = "";
    public string Owner { get; init; } = "Bilinmiyor";
    public string Time { get; init; } = "";
    public int Tab { get; init; }
    public int Slot { get; init; }
    public uint Vnum { get; init; }
    public uint Count { get; init; }
    public uint RawPriceA { get; init; }
    public uint RawPriceB { get; init; }
    public uint ChangeLookVnum { get; init; }
    public uint SetValue { get; init; }
    public uint SungmaScore { get; init; }
    public uint YinYangValue { get; init; }
    public byte YinYangType { get; init; }
    public byte[] RefineElementRaw { get; init; } = [];
    public bool PricesVerified { get; init; }
    public string Won => PricesVerified ? RawPriceB.ToString("N0") : "Doğrulanmadı";
    public string Yang => PricesVerified ? RawPriceA.ToString("N0") : "Doğrulanmadı";
    public string Location => $"{Tab + 1} / {Slot + 1}";
    public uint[] Sockets { get; init; } = [];
    public Bonus[] Bonuses { get; init; } = [];
    public ExtraBonus[] Extras { get; init; } = [];
    public byte[] Raw { get; init; } = [];
    public string BonusSummary => string.Join(" · ", Bonuses.Where(b => b.Type != 0).Select(b => $"{b.Type}: {b.Value:+0;-0;0}"));
}

public static class ItemDecoder
{
    public static ItemRow Decode(MarketCapture capture, SlotData slot)
    {
        var raw = slot.Raw;
        if (raw.Length != 128) throw new InvalidDataException("Ürün kaydı 128 bayt olmalı.");
        var legacyDemo = IsLegacyDemo(capture);
        var mapped = capture.ItemLayout == "shop-v2" || (!legacyDemo && capture.ProfileVersion == "ghidra-2026-10-14-unverified");
        return new ItemRow
        {
            CaptureId = capture.Id, Source = capture.Source, Shop = capture.ShopLabel,
            Owner = capture.OwnerName ?? "Bilinmiyor",
            Time = capture.ObservedAt.ToLocalTime().ToString("dd.MM.yyyy HH:mm:ss"), Tab = slot.Tab, Slot = slot.Slot,
            Vnum = U32(raw, 0), Count = legacyDemo ? U32(raw, 4) : raw[8], RawPriceA = U32(raw, legacyDemo ? 10 : 4), RawPriceB = U32(raw, legacyDemo ? 14 : 10),
            PricesVerified = capture.PricesVerified || mapped,
            ChangeLookVnum = U32(raw, 0x0e), SetValue = U32(raw, 0x78), SungmaScore = U32(raw, 0x7c),
            YinYangValue = U32(raw, 0x53), YinYangType = raw[0x57], RefineElementRaw = raw.AsSpan(0x58, 8).ToArray(),
            Sockets = Enumerable.Range(0, 7).Select(i => U32(raw, 0x14 + i * 4)).ToArray(),
            Bonuses = Enumerable.Range(0, 7).Select(i => new Bonus(U16(raw, 0x30 + i * 5), I16(raw, 0x32 + i * 5), raw[0x34 + i * 5])).ToArray(),
            Extras = Enumerable.Range(0, 4).Select(i => new ExtraBonus(U16(raw, 0x60 + i * 6), U32(raw, 0x62 + i * 6))).ToArray(),
            Raw = raw.ToArray()
        };
    }
    public static uint U32(byte[] raw, int offset) => BinaryPrimitives.ReadUInt32LittleEndian(raw.AsSpan(offset, 4));
    public static bool IsLegacyDemo(MarketCapture capture)
    {
        if (capture.ItemLayout != "legacy-v1") return false;
        if (capture.Source == "Demo") return true;
        if (capture.ImportedJson is null) return false;
        try
        {
            using var original = JsonDocument.Parse(capture.ImportedJson);
            return original.RootElement.EnumerateObject().Any(p => p.Name.Equals("Source", StringComparison.OrdinalIgnoreCase) && p.Value.ValueKind == JsonValueKind.String && p.Value.GetString() == "Demo");
        }
        catch (JsonException) { return false; }
    }
    public static ushort U16(byte[] raw, int offset) => BinaryPrimitives.ReadUInt16LittleEndian(raw.AsSpan(offset, 2));
    public static short I16(byte[] raw, int offset) => BinaryPrimitives.ReadInt16LittleEndian(raw.AsSpan(offset, 2));
    public static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
}

public sealed record CaptureRow(string Id, string Observed, string Source, string Shop, string Owner, int Items, int Slots, string Status);
public sealed record EventRow(string Time, string Kind, string Message);

public sealed record ReaderProfile
{
    public string Version { get; init; } = "ghidra-2026-10-14-shop-v2";
    public string ManagerPointerAddress { get; init; } = "0x035F1D34";
    public string? ModuleName { get; init; } = "metin2client.exe";
    public string DumpImageBase { get; init; } = "0x00710000";
    public string? ExpectedExecutableSha256 { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool PricesVerified { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ChannelAddress { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ShopOpenAddress { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ShopVidAddress { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? OpenGenerationAddress { get; init; }
    public int PollSeconds { get; init; } = 3;
    public string CharacterManagerPointerAddress { get; init; } = "0x035ED980";
    public string PlayerPointerAddress { get; init; } = "0x035ED9E4";
    public string ViewMatrixAddress { get; init; } = "0x03627528";
    public string ProjectionMatrixAddress { get; init; } = "0x03627568";
    public string WorldMatrixAddress { get; init; } = "0x03627628";
    public string ViewportAddress { get; init; } = "0x035F5854";
    public int TargetVidOffset { get; init; } = 0x3b8e8;
    public int ChannelByteOffset { get; init; } = 0x3d289;
    public int OwnerNameCodePage { get; init; } = 1254;
    public int RevisitSeconds { get; init; } = 60;
    public List<RoutePoint> ExplorationRoute { get; init; } = [];

    public void Validate()
    {
        _ = Address(ManagerPointerAddress);
        _ = Address(DumpImageBase);
        foreach (var value in new[] { CharacterManagerPointerAddress, PlayerPointerAddress, ViewMatrixAddress, ProjectionMatrixAddress, WorldMatrixAddress, ViewportAddress }) _ = Address(value);
        if (TargetVidOffset < 0 || TargetVidOffset > 0x100000 || ChannelByteOffset < 0 || ChannelByteOffset > 0x100000)
            throw new InvalidDataException("Oyuncu alan ofseti geçersiz.");
        if (RevisitSeconds is < 5 or > 86400) throw new InvalidDataException("Yeniden ziyaret süresi 5–86400 saniye olmalı.");
        if (ExplorationRoute is null || ExplorationRoute.Count > 1000 || ExplorationRoute.Any(p => !float.IsFinite(p.X) || !float.IsFinite(p.Y)))
            throw new InvalidDataException("Keşif rotası geçersiz.");
        foreach (var value in new[] { ChannelAddress, ShopOpenAddress, ShopVidAddress, OpenGenerationAddress })
            if (!string.IsNullOrWhiteSpace(value)) _ = Address(value);
        if (PollSeconds is < 1 or > 3600) throw new InvalidDataException("Okuma aralığı 1–3600 saniye olmalı.");
    }
    public static ulong Address(string value) => Convert.ToUInt64(value.Trim().Replace("0x", "", StringComparison.OrdinalIgnoreCase), 16);
}
public sealed record RoutePoint(float X, float Y);

public static class SampleData
{
    public static MarketCapture Create()
    {
        var slots = new List<SlotData>();
        for (var t = 0; t < 3; t++) for (var s = 0; s < 40; s++) slots.Add(new(t, s, new byte[128]));
        Set(slots[0].Raw, 189, 1, 2500000, 2);
        Set(slots[1].Raw, 11299, 1, 750000, 0);
        Set(slots[40].Raw, 27992, 200, 1200000, 0);
        return new MarketCapture
        {
            Source = "Demo", ShopLabel = "ÖRNEK VERİ • gerçek pazar değil", Server = "TEST", Map = "Test haritası",
            PricesVerified = true, ChannelVerified = false, Slots = slots, ItemLayout = "shop-v2", ProfileVersion = "synthetic-shop-v2",
            OwnerName = "ÖrnekOyuncu", OwnerVerified = false,
            Warnings = ["Sentetik test verisi. Canlı oyun veya gerçek fiyat değildir."]
        };
    }
    private static void Set(byte[] raw, uint vnum, uint count, uint yang, uint won)
    {
        BinaryPrimitives.WriteUInt32LittleEndian(raw.AsSpan(0), vnum);
        raw[8] = checked((byte)count);
        BinaryPrimitives.WriteUInt32LittleEndian(raw.AsSpan(4), yang);
        BinaryPrimitives.WriteUInt32LittleEndian(raw.AsSpan(10), won);
        BinaryPrimitives.WriteUInt32LittleEndian(raw.AsSpan(20), 28030);
        BinaryPrimitives.WriteUInt16LittleEndian(raw.AsSpan(48), 71);
        BinaryPrimitives.WriteInt16LittleEndian(raw.AsSpan(50), -5);
        raw[52] = 0xAB;
        raw[127] = 0xFE;
    }
}
