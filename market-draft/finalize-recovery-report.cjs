const fs=require('node:fs'),path=require('node:path'),{DatabaseSync}=require('node:sqlite');
(async()=>{const root=process.cwd(),reportFile=path.join(root,'market-draft/data/performance/full-recovery-validation.json'),r=JSON.parse(fs.readFileSync(reportFile,'utf8')),m=JSON.parse(fs.readFileSync(path.join(r.localBackup,'manifest.json'),'utf8')),kit=JSON.parse(fs.readFileSync(path.join(r.localBackup,'recovery-kit-validation.json'),'utf8'));const baseDB=new DatabaseSync(path.join(r.localBackup,'snapshot/.archive-state/request-budget.sqlite'),{readOnly:true}),base=baseDB.prepare('SELECT SUM(reads) reads,SUM(writes) writes,SUM(bytes) bytes FROM usage').get();baseDB.close();const currentDB=new DatabaseSync(path.join(root,'.archive-state/request-budget.sqlite'),{readOnly:true}),current=currentDB.prepare('SELECT SUM(reads) reads,SUM(writes) writes,SUM(bytes) bytes FROM usage').get();currentDB.close();r.recoveryKit=kit;r.operationDeltaSinceSnapshot={reads:current.reads-base.reads,writes:current.writes-base.writes,uploadedBytes:current.bytes-base.bytes};r.compressionSavingPercent=Math.round((1-r.compressedBytes/r.rawBytes)*1000)/10;r.verificationNote='First restore passed file/DB/API validation; image test URL was corrected and a clean R2 restore was repeated without reuploading data.';const sourceFile=m.files.find(f=>f.path.startsWith('scanner-data/Sunucular/')&&f.path.endsWith('/market.sqlite')),source=new DatabaseSync(path.join(r.restoredRoot,sourceFile.path),{readOnly:true});r.restoredSourceVersions=source.prepare('SELECT COUNT(*) n FROM compact_versions').get().n;source.close();const web=m.files.find(f=>f.path==='market-draft/data/live/safir.sqlite');r.restoredWebObservations=web.database.tables.observations;r.restoredWebItems=web.database.tables.items;r.restoredBonusRows=web.database.tables.item_bonuses;fs.writeFileSync(reportFile,JSON.stringify(r,null,2));fs.writeFileSync(path.join(r.localBackup,'restore-validation.json'),JSON.stringify(r,null,2));const md=`# R2 tam yedek ve geri yükleme provası — 16 Ekim 2026

Sonuç: **BAŞARILI**. Gerçek R2 deposundan yeni klasöre geri yüklenen site izole localhost portunda açıldı. Canlı yerel site durdurulmadı ve canlı veri dosyalarının üzerine yazılmadı.

| Kontrol | Sonuç |
|---|---:|
| Dosya | ${r.files} |
| SQLite veritabanı | ${r.databases.length} |
| Sıkıştırılmamış dosyalar | ${r.rawBytes.toLocaleString('tr-TR')} byte |
| Sıkıştırılmış yedek | ${r.compressedBytes.toLocaleString('tr-TR')} byte |
| Alan tasarrufu | %${r.compressionSavingPercent.toLocaleString('tr-TR')} |
| 4 MiB arşiv parçası | ${r.parts} |
| Geri yüklenen Safir kaynak sürümü | ${r.restoredSourceVersions.toLocaleString('tr-TR')} |
| Web geçmiş gözlemi | ${r.restoredWebObservations.toLocaleString('tr-TR')} |
| Web eşya kaydı (aktif ve pasif) | ${r.restoredWebItems.toLocaleString('tr-TR')} |
| Efsun indeks satırı | ${r.restoredBonusRows.toLocaleString('tr-TR')} |
| API karşılaştırması | ${r.api.length} / ${r.api.length} |
| HTTP üzerinden örnek görsel | ${r.imagesVerified} / ${r.imagesVerified} |

Bütün ${r.files} dosyanın SHA256 ve boyutu geri yükleme sırasında eşleşti. Altı SQLite için integrity_check, şema hash'i ve tüm tablo sayıları doğrulandı. API kontrolleri silah/zırh listeleri, Leylak araması, simya, pet filtresi, eşya/oyuncu önerileri ve tek/çok gözlemli geçmişi kapsar. İzole siteye gerçek R2 anahtarı verilmedi; geçmişi geri yüklenen kaynak DB üzerinden açtı. Test sunucusu prova sonunda kapatıldı.

İlk geri yüklemede dosya/DB/API doğrulaması geçti; görsel kontrolü yanlış URL kurduğu için son adım başarısız oldu. URL düzeltilip aynı R2 paketinden temiz geri yükleme tekrarlandı. Yedek yeniden yüklenmedi. İkinci tam geri yükleme + uygulama kontrolü ${r.durationSeconds} saniye sürdü; ilk snapshot/sıkıştırma/yükleme süresi buna dahil değildir.

## Konumlar

- Yedek kimliği: ${r.backupId}
- Yerel yedek: ${r.localBackup}
- Başarılı geri yüklenen kopya: ${r.restoredRoot}
- Bucket: metin2-history
- Bucket içindeki manifest: metin2-history-v1/${r.remoteManifest}
- Anahtarsız kurtarma araç paketi: metin2-history-v1/${kit.key}
- Araç paketi SHA256: ${kit.sha256}
- Ayrıntılı kanıt: market-draft/data/performance/full-recovery-validation.json
- Kurtarma yönergesi: RECOVERY_RUNBOOK.md

## İşlem tüketimi

Bu prova ve küçük kurtarma araç paketi sırasında uygulamanın yerel sayacında ${r.operationDeltaSinceSnapshot.writes} yazma ve ${r.operationDeltaSinceSnapshot.reads} okuma girişimi arttı. İkinci doğrulama indirmesi okuma sayısına dahildir. Bunlar hesap genelindeki Cloudflare panel işlemlerini içermez. Yüklenen veri yaklaşık ${(r.operationDeltaSinceSnapshot.uploadedBytes/1000000).toFixed(1)} MB; yedek her taramada tekrar alınmıyor. Başarılı geri yükleme onay nesnesi de R2'de saklandı.

## Kapsam ve kalanlar

Güncel uygulama/görseller/tanımlar, aktif kaynak DB'leri, eski kök market.sqlite ve web/istatistik DB'leri kapsanır. Gerçek erişim anahtarları, Ghidra analiz projesi, eski sürüm geçiş yedekleri ve test DB'leri dahil değildir; orijinal konumlarında korunur. Bu, işletim sistemi veya oyun istemcisinin tam makine yedeği değildir.

Bu bir kurtarma noktasıdır. Otomatik tam yedek sıklığı, saklama/disk politikası ve daha yeni scanner ile eski web/ACK durumunu karıştırmadan kurtarma prosedürü ayrıca uygulanmalıdır. Yerel kullanım sayacı yedek anına aittir; R2 upload açılmadan önce gerçek Cloudflare kullanımıyla uzlaştırılır. Windows–VDS güvenli canlı veri kanalı ve gerçek VDS kurulumu bu provayla tamamlanmış sayılmaz.
`;fs.writeFileSync(path.join(root,'KURTARMA_PROVASI_2026-10-16.md'),md);const health=await(await fetch('http://127.0.0.1:4180/api/health')).json();console.log(JSON.stringify({success:r.success,files:r.files,databases:r.databases.length,sourceVersions:r.restoredSourceVersions,webObservations:r.restoredWebObservations,compressionSavingPercent:r.compressionSavingPercent,operations:r.operationDeltaSinceSnapshot,liveSiteReady:health.ready,readWorkers:health.readWorkers,queue:health.queue},null,2))})().catch(e=>{console.error(e.message);process.exitCode=1});
