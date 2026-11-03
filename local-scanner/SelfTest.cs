using System.Buffers.Binary;

namespace Metin2Search.Local;

internal static class SelfTest
{
    public static int Run(string root)
    {
        Directory.CreateDirectory(root);
        var run = Path.Combine(root, "run-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(run);
        var results = new List<string>();
        try
        {
            var capture = SampleData.Create(); capture.Validate();
            var row = ItemDecoder.Decode(capture, capture.Slots[0]);
            Assert(row.Vnum == 189 && row.Count == 1 && row.RawPriceA == 2500000 && row.RawPriceB == 2, "Kimlik, adet ve ayrı fiyat alanları", results);
            Assert(row.Bonuses[0] == new Bonus(71, -5, 0xAB), "İşaretli efsun değeri ve bilinmeyen bayt", results);
            Assert(row.Sockets.Length == 7 && row.Sockets[0] == 28030 && row.Raw[127] == 0xFE, "Soketler ve çözümlenmeyen ham bayt", results);
            var liveBytes = Convert.FromHexString("3DC50000C0D8A70001000000000000000000004B000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000");
            var legacyLive = capture with { Source = "LiveUnverified", ProfileVersion = "ghidra-2026-10-14-unverified", ItemLayout = "legacy-v1", PricesVerified = false, Slots = [new(0, 0, liveBytes)] };
            var actual = ItemDecoder.Decode(legacyLive, legacyLive.Slots[0]);
            Assert(actual.Vnum == 50493 && actual.Count == 1 && actual.RawPriceA == 11000000 && actual.RawPriceB == 0 && actual.PricesVerified,
                "Kullanıcının canlı ham kaydı: 1 adet, 11.000.000 yang, 0 won; eski alan karışıklığı giderildi", results);
            Assert(ItemDecoder.Decode(capture with { PricesVerified = false, ItemLayout = "unknown", ProfileVersion = "unknown" }, capture.Slots[0]).Won == "Doğrulanmadı", "Bilinmeyen yerleşim gerçek fiyat gibi gösterilmiyor", results);
            var bad = capture with { Slots = [new SlotData(0, 0, new byte[127])] };
            Reject(bad.Validate, "Eksik ürün kaydı reddediliyor", results);
            Reject((capture with { Channel = 2 }).Validate, "Kanal 1 dışı kayıt reddediliyor", results);
            Reject((capture with { Slots = [capture.Slots[0], capture.Slots[0]] }).Validate, "Tekrarlı yuva reddediliyor", results);
            using (var store = new LocalStore(run))
            {
                Assert(store.Save(capture), "İlk okuma kaydedildi", results);
                Assert(store.CaptureCount == 1 && store.ObservationCount == 120, "Boş yuvalar dahil 120 gözlem", results);
                Assert(!store.Save(capture) && store.CaptureCount == 1, "Aynı içe aktarma idempotent", results);
                Reject(() => store.Save(capture with { ShopLabel = "Çakışan farklı veri" }), "Kimlik çakışması farklı veriyi ezmiyor", results);
                var loaded = store.Load(capture.Id);
                Assert(loaded.Slots.Count == 120 && loaded.Slots[0].Raw.SequenceEqual(capture.Slots[0].Raw), "Ham kayıt yeniden yükleme", results);
                var blobCount = store.BlobCount;
                var repeated = capture with { Id = Guid.NewGuid().ToString("N"), ObservedAt = capture.ObservedAt.AddSeconds(3) };
                store.Save(repeated);
                Assert(store.CaptureCount == 2 && store.ObservationCount == 240 && store.BlobCount == blobCount, "Değişmemiş ürünün her ziyareti korunuyor, ham bayt tek kopya", results);
                store.Execute("CREATE TRIGGER fail_test BEFORE INSERT ON observations WHEN NEW.tab=1 BEGIN SELECT RAISE(ABORT,'test-failure'); END");
                var failed = capture with { Id = Guid.NewGuid().ToString("N") };
                Reject(() => store.Save(failed), "Yazma hatası uygulamaya iletiliyor", results);
                Assert(store.CaptureCount == 2 && store.ObservationCount == 240, "Yarım pazar transaction'ı bütünüyle geri alınıyor", results);
                store.Execute("DROP TRIGGER fail_test");
                Assert(store.Integrity == "ok", "SQLite bütünlük kontrolü", results);
                var diagnosticBytes = new byte[] { 1, 2, 3, 0xFE };
                store.Diagnostic(new MarketReadException("partial", new ReadDiagnostic(0x10000, 128, diagnosticBytes, "test")));
                Assert((long)store.Query("SELECT COUNT(*) FROM diagnostics")[0][0]! == 1 && store.ObservationCount == 240,
                    "Kısmi ham okuma tanı kaydında saklanıyor, ilanlara karışmıyor", results);
                store.Backup(Path.Combine(run, "backup.sqlite"));
                Assert(File.Exists(Path.Combine(run, "backup.sqlite")), "WAL dahil tutarlı yedek oluşturuluyor", results);
                store.Execute("ATTACH DATABASE ? AS backupcheck", Path.Combine(run, "backup.sqlite"));
                Assert((long)store.Query("SELECT COUNT(*) FROM backupcheck.observations")[0][0]! == 240 &&
                    (string)store.Query("PRAGMA backupcheck.integrity_check")[0][0]! == "ok", "Yedek içeriği ve bütünlüğü doğrulandı", results);
                store.Execute("DETACH DATABASE backupcheck");
            }
            using (var reopened = new LocalStore(run))
                Assert(reopened.CaptureCount == 2 && reopened.ObservationCount == 240 && reopened.Integrity == "ok", "Yeniden başlatmada geçmiş korunuyor", results);
            var migration = Path.Combine(run, "migration");
            using (var previous = new LocalStore(migration))
            {
                previous.Save(legacyLive);
                previous.Execute("UPDATE observations SET count=11000000");
            }
            using (var migrated = new LocalStore(migration))
            {
                Assert((long)migrated.Query("SELECT count FROM observations")[0][0]! == 1 &&
                    (long)migrated.Query("SELECT previous_count FROM decoding_history")[0][0]! == 11000000,
                    "Eski veritabanı adedi düzeltildi; önceki çözümleme arşivde korundu", results);
                Assert(migrated.Load(legacyLive.Id).Slots[0].Raw.SequenceEqual(liveBytes), "Veritabanı dönüşümünde ham baytlar değişmedi", results);
            }
            ScannerTests.Run(results);
            TradeTests.Run(run, results);
            CompactTradeTests.Run(run,results);
            PurchaseCheckTests.Run(results);
            NativePurchaseTransportTests.Run(run,results);
            FakeProbeTests.Run(run,results);
            ListingLifecycleTests.Run(run,results);
            CategoryDeliveryTests.Run(run,results);
            IncrementalScanTests.Run(run,results);
            Release39Tests.Run(run,results);
            SessionTests.Run(run,results);
            CharacterCycleTests.Run(run,results);
            UplinkTests.Run(run,results);
            DailyArchiveTests.Run(run,results);
            PerformanceTests.Run(run,results);
            var profileJson = JsonData.Encode(new ReaderProfile());
            var loadedProfile = JsonData.Decode<ReaderProfile>(profileJson); loadedProfile.Validate();
            Assert(loadedProfile.ModuleName == "metin2client.exe" && loadedProfile.ChannelByteOffset == 0x3d289 && loadedProfile.TargetVidOffset == 0x3b8e8,
                "v0.2 profil serileştirmesi otomatik adres/kanal/VID ayarlarını koruyor", results);
            var json = JsonData.Encode(capture);
            var roundTrip = JsonData.Decode<MarketCapture>(json); roundTrip.Validate();
            Assert(roundTrip.Slots[0].Raw.SequenceEqual(capture.Slots[0].Raw), "JSON/Base64 tam veri turu", results);
            File.WriteAllText(Path.Combine(root, "sample-market.json"), json);
            File.WriteAllText(Path.Combine(root, "last-run.json"), JsonData.Encode(new { success = true, checks = results, runDirectory = run }));
            return 0;
        }
        catch (Exception error)
        {
            File.WriteAllText(Path.Combine(root, "last-run.json"), JsonData.Encode(new { success = false, checks = results, error = error.ToString(), runDirectory = run }));
            return 1;
        }
    }
    private static void Assert(bool value, string description, List<string> results)
    {
        if (!value) throw new Exception("FAILED: " + description);
        results.Add("PASS: " + description);
    }
    private static void Reject(Action action, string description, List<string> results)
    {
        try { action(); } catch (Exception) { results.Add("PASS: " + description); return; }
        throw new Exception("FAILED: " + description);
    }
}
