const fs=require('node:fs'),path=require('node:path');
const root=__dirname,DISCORD='https://discord.gg/',UPDATED='19 Ekim 2026';
const SUPPORT='destek@example.com',CONTACT='iletisim@example.com';
const mail=a=>`<!--email_off--><a href="mailto:${a}">${a}</a><!--email_on-->`;
const NOTICE='Metin2Search, Metin2 topluluğu için oluşturulmuş resmi olmayan bir platformdur ve Webzen Inc. veya Gameforge ile bağlantısı yoktur. Metin2 ve ilgili ticari markalar, logolar ve varlıklar ilgili sahiplerinin mülküdür.';
const PAGES=[
  {file:'veri-kaynagi.html',url:'/veri-kaynagi',nav:'Veri Kaynağı',title:'Veri Kaynağı',eyebrow:'Verilerimiz nasıl hazırlanıyor',lead:'Gördüğünüz ilanların nereden geldiği, nasıl güncel tutulduğu ve sahte pazarların nasıl ayıklandığı.',meta:true,sections:[
    ['Veriler nereden geliyor?',['Metin2Search’teki ilanlar, oyunun kendi <b>Ticaret Camı</b>’ndan alınır. Fiyatlar, eşya açıklamaları, efsunlar ve taşlar, bir oyuncunun oyunda gördüğü haliyle okunur; elle girilmiş ya da başka sitelerden toplanmış bilgi kullanılmaz.','Bu yüzden eşya açıklamaları oyunun kendi metinleridir ve oyundakiyle birebir örtüşür.']],
    ['Veriler nasıl güncel tutuluyor?',['Ticaret Camı düzenli aralıklarla yeniden taranır. Yeni ilanlar eklenir, fiyatı ya da içeriği değişen ilanlar güncellenir, oyunda artık görünmeyen ilanlar güncel listeden çıkarılır ve geçmiş kaydında kalır.','Her ilanın yanında en son ne zaman görüldüğü yazar. Bu zaman, ilanın oyuna konduğu an değil, sitemizin onu en son gördüğü andır.','Aynı ilanın birebir tekrar eden kayıtları tek kayıtta birleştirilir; böylece aynı eşya listede kat kat görünmez.']],
    ['Sahte (FAKE) pazarlar nasıl doğrulanıyor?',['Ticaret Camı’nda bazı pazarlar listede görünse de gerçekte alışverişe hazır değildir. Bunlara <b>sahte (FAKE) pazar</b> diyoruz.','Her pazar, oyunun kendi sistemine sorularak kontrol edilir. Oyun, pazarın hazır olmadığını bildirirse ilan <b class="tag-fake">FAKE</b> etiketi alır ve son kontrol zamanı etikette gösterilir.','Kontrol zamanla eskiyebilir. Son kontrolün üzerinden uzun süre geçtiyse etiket <b class="tag-stale">FAKE?</b> olarak turuncuya döner; bu, durumun yeniden doğrulanması gerektiği anlamına gelir.','Etiket, pazarın sahibi hakkında bir suçlama değildir; yalnızca o pazarın şu an alışverişe hazır görünmediğini belirtir. Etiketli ilanlar kendiliğinden silinmez, böylece durumu kendiniz de görebilirsiniz.']],
    ['Bilmeniz gerekenler',['Gösterilen fiyatlar ilan fiyatıdır; gerçekleşen satış fiyatı değildir.','Pazar bilgileri satıcıya ait kaydedilmiş ilanları gösterir. Oyundaki anlık durum her zaman birebir yansımayabilir; satın almadan önce oyun içinde kontrol etmenizi öneririz.','Pazar verilerini incelemek için üyelik gerekmez.']]
  ]},
  {file:'cerez-politikasi.html',url:'/cerez-politikasi',nav:'Çerez Politikası',title:'Çerez Politikası',eyebrow:'Çerezler ve benzeri teknolojiler',lead:'Sitede hangi bilgilerin tarayıcınıza kaydedildiği ve bunları nasıl yönetebileceğiniz.',sections:[
    ['Çerez nedir?',['Çerezler ve tarayıcı depolama alanı (yerel depolama), bir siteyi ziyaret ettiğinizde cihazınıza kaydedilen küçük veri parçalarıdır. Sitenin sizi hatırlamasına ve tercihlerinizi korumasına yardımcı olur.']],
    ['Hangilerini kullanıyoruz?',[
      '<b>Zorunlu:</b> Çerez tercihinizin kendisini (kabul/ret seçiminiz) tarayıcınızda saklarız. Bu kayıt olmadan tercihinizi hatırlayamayız. Aynı şekilde, açılır duyuruyu iki saatte bir göstermek için son gösterim zamanını da saklarız. Siteyi çalıştırmak için oturum çerezi kullanmıyoruz; hesap açmanız gerekmez.',
      '<b>Tercihler:</b> Kaydettiğiniz eşyalar, karşılaştırma listeniz, liste/kart görünümü ve ana sayfadaki hareket seçimi gibi kolaylıklar yalnızca cihazınızın tarayıcı depolamasında tutulur. Bu bilgiler sunucumuza gönderilmez. Tercihler kapatılırsa bu kolaylıklar kaydedilmez.',
      '<b>Reklam ve ölçümleme:</b> Şu anda üçüncü taraf reklam veya ölçümleme çerezi kullanılmıyor. İleride eklenirse bu seçenek varsayılan olarak kapalı gelir ve yalnızca onayınızla çalışır.'
    ]],
    ['Tercihlerinizi nasıl yönetirsiniz?',['Sayfa altındaki <button type="button" class="linklike" data-cookie-settings>Çerez ayarları</button> bağlantısından seçimlerinizi istediğiniz zaman değiştirebilirsiniz. Ayrıca tarayıcınızın ayarlarından çerezleri ve site verilerini silebilir ya da engelleyebilirsiniz; bu durumda kaydettiğiniz eşyalar gibi tercihleriniz silinir.']],
    ['Üçüncü taraf içerik',['Sayfalarımız yazı tipleri için Google Fonts’tan, topluluk bağlantısı için Discord’dan içerik çağırabilir. Bu hizmetler kendi gizlilik politikalarına tabidir.']],
    ['Değişiklikler',['Bu politika güncellenebilir. Güncel hali her zaman bu sayfada yayımlanır.']]
  ]},
  {file:'gizlilik-politikasi.html',url:'/gizlilik-politikasi',nav:'Gizlilik Politikası',title:'Gizlilik Politikası',eyebrow:'Kişisel verilerin korunması',lead:'Hangi bilgilerin işlendiği, neden işlendiği ve haklarınız.',sections:[
    ['Sizden ne topluyoruz?',['Metin2Search’i kullanmak için hesap açmanız, ad ya da e-posta vermeniz gerekmez. Arama, filtreleme ve karşılaştırma gibi işlemler için kişisel bilgi istemeyiz.','Siteyi güvenli ve verimli sunabilmek için sunucumuz teknik erişim kayıtları (zaman, istek adresi, tarayıcı türü, IP adresi) tutabilir. Bu kayıtlar yalnızca güvenlik, hata ayıklama ve kötüye kullanımı önleme amacıyla kullanılır.']],
    ['İlanlardaki bilgiler',['Sitede görünen ilanlar, satıcı adları ve pazar adları oyunda herkesin görebildiği bilgilerdir. Bunları oyunun Ticaret Camı’ndan okuruz; ek kişisel veri eklemeyiz.']],
    ['Tarayıcınızda saklananlar',['Kaydettiğiniz eşyalar, karşılaştırma listeniz ve görünüm seçiminiz yalnızca cihazınızda saklanır. Ayrıntılar için <a href="/cerez-politikasi">Çerez Politikası</a>’na bakın.']],
    ['Üçüncü taraflar',['Yazı tipleri için Google Fonts, topluluk iletişimi için Discord bağlantısı kullanılır. Bu hizmetlere bağlandığınızda IP adresiniz ilgili hizmet sağlayıcıya iletilebilir. Verilerinizi satmaz, reklam amacıyla üçüncü kişilerle paylaşmayız.']],
    ['Haklarınız',['6698 sayılı Kişisel Verilerin Korunması Kanunu kapsamında; verilerinizin işlenip işlenmediğini öğrenme, bilgi talep etme, düzeltme ve silinmesini isteme haklarına sahipsiniz. Taleplerinizi '+mail(CONTACT)+' adresine e-postayla ya da Discord topluluğumuz üzerinden iletebilirsiniz.']],
    ['Değişiklikler',['Bu politika güncellenebilir. Güncel hali her zaman bu sayfada yayımlanır.']]
  ]},
  {file:'kullanim-sartlari.html',url:'/kullanim-sartlari',nav:'Kullanım Şartları',title:'Kullanım Şartları',eyebrow:'Siteyi kullanma koşulları',lead:'Metin2Search’i kullanarak aşağıdaki şartları kabul etmiş sayılırsınız.',sections:[
    ['Hizmetin niteliği',['Metin2Search, Metin2 topluluğu için oluşturulmuş, resmi olmayan bir bilgilendirme platformudur. Oyunun yayıncılarıyla ya da işletmecileriyle bir bağı yoktur.','Site, oyundaki pazar ilanlarını incelemenize yardımcı olur; alım satım yapmaz ve alım satıma aracılık etmez.']],
    ['Bilgilerin doğruluğu',['Verileri doğru ve güncel tutmak için çaba gösteririz; ancak kayıtlar sitemizin ilanı en son gördüğü ana aittir ve oyundaki anlık durumla farklılık gösterebilir. Bilgiler “olduğu gibi” sunulur, doğruluğu ve kesintisizliği garanti edilmez.','Alım satım kararlarınızı vermeden önce ilgili ilanı oyun içinde doğrulamanız sizin sorumluluğunuzdadır. Metin2Search, bu bilgilere dayanarak yaptığınız işlemlerden doğan kayıplardan sorumlu tutulamaz.']],
    ['Kullanım kuralları',['Siteyi hukuka ve genel ahlaka uygun biçimde kullanmanız gerekir. Siteye zarar verecek, performansını bozacak, aşırı otomatik istek gönderecek ya da içeriğini toplu olarak kopyalayarak yeniden yayımlayacak kullanımlar yasaktır.','FAKE etiketi, bir pazarın o an alışverişe hazır görünmediğini belirtir; kişilere yönelik bir suçlama değildir ve bu amaçla kullanılmamalıdır.']],
    ['Fikri mülkiyet ve markalar',['Metin2 ve ilgili ticari markalar, logolar ve varlıklar ilgili sahiplerinin mülküdür. Sitede görünen oyun içi görseller ve metinler yalnızca bilgilendirme amacıyla kullanılır. Metin2Search’in kendi tasarımı ve yazılımı kaynak gösterilmeden çoğaltılamaz.']],
    ['Reklamlar ve bağlantılar',['Sitede tanıtım ya da reklam alanları bulunabilir. Reklamverenlerin içeriklerinden ve bağlantı verilen dış sitelerden Metin2Search sorumlu değildir.']],
    ['Değişiklikler ve iletişim',['Şartları dilediğimiz zaman güncelleyebiliriz; güncel hali bu sayfada yayımlanır. Sorularınız için '+mail(SUPPORT)+' adresine yazabilir ya da Discord topluluğumuz üzerinden bize ulaşabilirsiniz.']]
  ]},
  {file:'iletisim.html',url:'/iletisim',nav:'İletişim',title:'İletişim',eyebrow:'Bize ulaşın',lead:'Soru, öneri, hata bildirimi ya da veri talepleriniz için bize e-posta yazabilirsiniz.',sections:[
    ['Destek ve geri bildirim',['Sitenin kullanımıyla ilgili sorularınız, hata bildirimleriniz, eksik ya da yanlış görünen bir eşya veya fiyat için: <b>'+mail(SUPPORT)+'</b>']],
    ['Diğer konular',['İş birliği, reklam ve tanıtım, basın, hukuki bildirimler ile kişisel veri talepleriniz için: <b>'+mail(CONTACT)+'</b>']],
    ['Topluluk',['Hızlı sorular ve diğer oyuncularla sohbet için <a href="'+DISCORD+'" target="_blank" rel="noopener noreferrer">Discord topluluğumuza</a> katılabilirsiniz.','Mesajlarınızı en kısa sürede yanıtlamaya çalışırız. Yanıt süresi için kesin bir söz veremeyiz; yoğun dönemlerde gecikme olabilir.']],
    ['E-posta güvenliği',['Bizden size ilk e-postayla bir şey istemeyiz. Hesap şifresi, ödeme bilgisi ya da oyun içi hesap bilgisi hiçbir zaman istenmez. Böyle bir mesaj alırsanız bize bildirin.']]
  ]}
];
const esc=s=>String(s).replace(/&/g,'&amp;').replace(/</g,'&lt;').replace(/>/g,'&gt;').replace(/"/g,'&quot;');
const footer=`<footer class="foot site-foot">
    <div class="foot-top">
      <span class="foot-brand">METIN2<b>SEARCH</b> © 2026</span>
      <nav class="foot-links" aria-label="Yasal bağlantılar"><a href="/veri-kaynagi">Veri Kaynağı</a><a href="/cerez-politikasi">Çerez Politikası</a><a href="/gizlilik-politikasi">Gizlilik Politikası</a><a href="/kullanim-sartlari">Kullanım Şartları</a><a href="/iletisim">İletişim</a><button type="button" class="linklike" data-cookie-settings>Çerez ayarları</button><a href="${DISCORD}" target="_blank" rel="noopener noreferrer">Discord topluluğu</a></nav>
    </div>
    <p class="foot-legal">${esc(NOTICE)}</p>
  </footer>`;
const head=(p)=>`<!doctype html>
<html lang="tr">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width,initial-scale=1">
<meta name="theme-color" content="#1d1f25">
<title>${esc(p.title)} · Metin2Search</title>
<meta name="description" content="${esc(p.lead)}">
<link rel="preconnect" href="https://fonts.googleapis.com">
<link rel="preconnect" href="https://fonts.gstatic.com" crossorigin>
<link href="https://fonts.googleapis.com/css2?family=Cinzel:wght@700&family=Inter:wght@400;500;600;700&display=swap" rel="stylesheet">
<link rel="stylesheet" href="/taslak.css?v=19">
<link rel="stylesheet" href="/yasal.css?v=1">
<link rel="icon" type="image/svg+xml" href="/favicon.svg"></head>`;
for(const p of PAGES){
  const body=p.sections.map(([h,ps],i)=>`<section class="legal-sec" id="b${i+1}"><h2>${esc(h)}</h2>${ps.map(t=>`<p>${t}</p>`).join('')}</section>`).join('\n      ');
  const html=`${head(p)}
<body class="legal">
<header class="legal-top"><div class="legal-top-in">
  <a class="brand" href="/"><img src="/assets/metin2-logo.png" alt=""><span class="brand-word">METIN2 <b>SEARCH</b><span class="beta">BETA</span></span></a>
  <nav class="legal-nav" aria-label="Sayfalar"><a href="/pazar">Pazar</a>${PAGES.map(q=>`<a href="${q.url}"${q===p?' aria-current="page"':''}>${esc(q.nav)}</a>`).join('')}</nav>
</div></header>
<main class="legal-main">
  <article class="legal-card">
    <span class="eyebrow">${esc(p.eyebrow)}</span>
    <h1>${esc(p.title)}</h1>
    <p class="legal-lead">${esc(p.lead)}</p>
    <aside class="legal-notice" role="note"><b>Önemli bilgi</b><p>${esc(NOTICE)}</p></aside>
    ${p.meta?'<p class="legal-meta" id="about-meta" hidden></p>':''}
    <div class="legal-body">
      ${body}
    </div>
    <p class="legal-date">Son güncelleme: ${UPDATED}</p>
  </article>
  ${footer}
</main>
<script src="/cerez.js?v=1"></script>${p.meta?`
<script>fetch('/api/meta?server=safir').then(r=>r.ok?r.json():null).then(m=>{if(!m)return;const n=v=>new Intl.NumberFormat('tr-TR').format(v),e=document.getElementById('about-meta'),d=m.latest?new Date(m.latest).toLocaleString('tr-TR',{dateStyle:'long',timeStyle:'short'}):'';e.textContent='Şu anda '+n(m.uniqueRows)+' güncel ilan kayıtlı'+(d?'; son güncelleme: '+d+'.':'.');e.hidden=false}).catch(()=>{})</script>`:''}
</body>
</html>
`;
  fs.writeFileSync(path.join(root,p.file),html);
}
fs.writeFileSync(path.join(root,'site-footer.html'),footer+'\n');
console.log(PAGES.length+' yasal sayfa + site-footer.html yazıldı');
