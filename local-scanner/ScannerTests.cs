using System.Buffers.Binary;
using System.Numerics;
using System.Text;

namespace Metin2Search.Local;

internal static class ScannerTests
{
    public static void Run(List<string> results)
    {
        void Check(bool ok, string label) { if (!ok) throw new Exception("FAILED: " + label); results.Add("PASS: " + label); }
        var projection = new ProjectionState(Matrix4x4.Identity, Matrix4x4.Identity, Matrix4x4.Identity, 0, 0, 800, 600, 0, 1);
        Check(MarketGeometry.Project(new Vector3(0, 0, .5f), projection) == new Vector2(400, 300), "Dünya/ekran izdüşümü ve Y yönü");
        Check(MarketGeometry.Project(new Vector3(5, 0, .5f), projection) == null && MarketGeometry.Project(new Vector3(0, 0, 2), projection) == null,
            "Görüş alanı ve derinlik dışındaki konuma tıklama üretilmiyor");
        var memory = new Dictionary<ulong, byte[]>();
        var manager = new byte[8]; Put(manager, 0, 0x20000); Put(manager, 4, 1); memory[0x10020] = manager;
        var head = new byte[24]; head[13] = 1; Put(head, 4, 0x30000); memory[0x20000] = head;
        var node = new byte[24]; Put(node, 0, 0x20000); Put(node, 8, 0x20000); Put(node, 16, 123); Put(node, 20, 0x40000); memory[0x30000] = node;
        var type = new byte[4]; Put(type, 0, 12); memory[0x40744] = type;
        var position = new byte[12]; BinaryPrimitives.WriteSingleLittleEndian(position.AsSpan(8), .5f); memory[0x407c4] = position;
        var name = new byte[24]; Encoding.ASCII.GetBytes("PazarSahibi").CopyTo(name, 0); Put(name, 16, 11); Put(name, 20, 15); memory[0x40018] = name;
        byte[] Read(ulong address, int length) { if (!memory.TryGetValue(address, out var raw) || raw.Length != length) throw new IOException("Unmapped fixture"); return raw.ToArray(); }
        var actors = ActorCatalog.Read(Read, 0x10000, Encoding.ASCII);
        Check(actors.Count == 1 && actors[0].Name == "PazarSahibi" && actors[0].Vid == 123, "Karakter ağacından pazar VID ve oyuncu ismi okunuyor");
        var validName = name.ToArray();
        Put(name, 16, 0);
        var empty = ActorCatalog.Read(Read, 0x10000, Encoding.ASCII);
        Check(empty.Count == 1 && !empty[0].NameKnown && empty[0].NameDiagnostic != null, "Boş oyuncu adı bütün pazar keşfini engellemiyor");
        Put(name, 16, 99999);
        var malformed = ActorCatalog.Read(Read, 0x10000, Encoding.ASCII);
        Check(malformed.Count == 1 && !malformed[0].NameKnown && malformed[0].RawNameHeader.SequenceEqual(name),
            "Eşleşmeyen ad alanı pazar VID'sini koruyor ve ham tanıyı kaydediyor");
        memory.Remove(0x40018);
        var unreadable = ActorCatalog.Read(Read, 0x10000, Encoding.ASCII);
        Check(unreadable.Count == 1 && !unreadable[0].NameKnown && unreadable[0].NameDiagnostic!.Contains("okunamadı"),
            "Okunamayan isteğe bağlı oyuncu adı pazar keşfini durdurmuyor");
        memory[0x40018] = name = validName;
        Put(type, 0, 1); Check(ActorCatalog.Read(Read, 0x10000, Encoding.ASCII).Count == 0, "Normal NPC pazar olarak seçilmiyor"); Put(type, 0, 12);
        Put(node, 0, 0x30000);
        var rejected = false; try { ActorCatalog.Read(Read, 0x10000, Encoding.ASCII); } catch (InvalidDataException) { rejected = true; }
        Check(rejected, "Bozuk/döngülü karakter ağacı tıklamadan önce reddediliyor");
        var simulatedProjection = projection with { World = Matrix4x4.CreateScale(1, 1, .01f) };
        var client = new FakeClient(simulatedProjection);
        var input = new FakeInput(client); using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(10)); var saved = 0;
        var scanner = new AutomaticScanner(client, input, c => { saved++; stop.Cancel(); }, (_, _) => { }, _ => { }, _ => { });
        try { scanner.Run("TEST", "TEST", stop.Token).GetAwaiter().GetResult(); } catch (OperationCanceledException) { }
        Check(saved == 1 && input.Clicks == 1 && input.ClosedBeforeFirstClick && input.FocusCount == 1,
            "Otomatik döngü açık pazarı kapatıyor, sıradaki pazarı açıyor ve tek kayıt üretiyor (sahte istemci)");
        var wrong = new FakeClient(simulatedProjection) { WrongOwner = true }; var wrongInput = new FakeInput(wrong); var wrongSaved = 0;
        var wrongScanner = new AutomaticScanner(wrong, wrongInput, _ => wrongSaved++, (_, _) => { }, _ => { }, _ => { });
        using var wrongTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        rejected = false;
        try { wrongScanner.Run("TEST", "TEST", wrongTimeout.Token).GetAwaiter().GetResult(); } catch (InvalidDataException) { rejected = true; }
        Check(rejected && wrongSaved == 0, "Yanlış VID/sahip eşleşen pazar kaydedilmiyor (sahte istemci)");
        var nameless = new FakeClient(simulatedProjection) { NameMissing = true }; var namelessInput = new FakeInput(nameless);
        using var namelessTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(10)); MarketCapture? namelessCapture = null;
        var namelessScanner = new AutomaticScanner(nameless, namelessInput, c => { namelessCapture = c; namelessTimeout.Cancel(); }, (_, _) => { }, _ => { }, _ => { });
        try { namelessScanner.Run("TEST", "TEST", namelessTimeout.Token).GetAwaiter().GetResult(); } catch (OperationCanceledException) { }
        Check(namelessCapture is { ShopVid: "123", OwnerName: null, OwnerVerified: false } && namelessInput.Clicks == 1,
            "Sahibi okunamayan pazar doğru VID ile otomatik okunuyor; isim uydurulmuyor");
    }
    private static void Put(byte[] raw, int at, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(raw.AsSpan(at), value);
    private sealed class FakeClient(ProjectionState projection) : IMarketClient
    {
        public bool NativeLayoutValidated => true;
        public ReaderProfile Profile { get; } = new();
        public NativeShopState State = new(1, 1, 1, 999);
        public bool WrongOwner;
        public bool NameMissing;
        public NativeShopState ReadNativeState() => State;
        public List<ShopActor> ReadShops() => [new(123, 0x40000, NameMissing ? "" : "PazarSahibi", Encoding.ASCII.GetBytes("PazarSahibi"), new Vector3(0, 0, .5f))];
        public Vector3 PlayerPosition() => new(0, 0, .5f);
        public ProjectionState Projection() => projection;
        public Task<MarketCapture> CaptureAsync(string server, string map, string label, bool monitored, CancellationToken token) =>
            Task.FromResult(SampleData.Create() with { ShopVid = WrongOwner ? "999" : "123", OwnerName = NameMissing ? null : "PazarSahibi", OwnerVerified = !NameMissing });
    }
    private sealed class FakeInput(FakeClient client) : IGameInteraction
    {
        public int Clicks, FocusCount;
        public bool ClosedBeforeFirstClick;
        public void Focus() => FocusCount++;
        public void CheckActive() { }
        public void Escape() => client.State = new(1, 0, 0, 0);
        public void Click(Vector2 point, ProjectionState projection)
        {
            ClosedBeforeFirstClick = client.State.Open == 0;
            if (!ClosedBeforeFirstClick) throw new Exception("Clicked into an open shop!");
            Clicks++; client.State = new(1, 1, 1, 123);
        }
    }
}
