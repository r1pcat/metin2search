const SITE_NAME='Metin2Search';
const esc=s=>String(s).replace(/&/g,'&amp;').replace(/</g,'&lt;').replace(/>/g,'&gt;').replace(/"/g,'&quot;');
const LANGS=[];
const LANG_NAMES={tr:'Türkçe',en:'English',de:'Deutsch',fr:'Français',es:'Español',pt:'Português',it:'Italiano',ru:'Русский',pl:'Polski',ro:'Română',ar:'العربية'};
const OG_LOCALE={tr:'tr_TR',en:'en_US',de:'de_DE',fr:'fr_FR',es:'es_ES',pt:'pt_PT',it:'it_IT',ru:'ru_RU',pl:'pl_PL',ro:'ro_RO',ar:'ar_AR'};

const TR={
 home:{title:'Metin2 Ticaret Camı ve Gelişmiş Ticaret Camı · Pazar Eşya Fiyatları · Metin2Search',description:'Metin2 Gelişmiş Ticaret Camı ve Pazar ilanlarını tek yerden ara: eşya, efsun, ekipman ve malzeme fiyatları, fiyat geçmişi ve ilan karşılaştırma. Safir, Nite, Star, Lucifer ve diğer sunucular.'},
 market:{title:'Metin2 Pazar ve Gelişmiş Ticaret Camı · Eşya Fiyatı Ara · Metin2Search',description:'Metin2 Pazar ve Gelişmiş Ticaret Camı ilanlarında eşya, efsun, element ve fiyata göre ara. Ucuz ilanı bul, fiyat geçmişine bak, ilanları yan yana karşılaştır.'},
 server:(n)=>({title:`Metin2 ${n} Pazarı ve Ticaret Camı Fiyatları · Metin2Search`,description:`Metin2 ${n} sunucusu Pazar ve Gelişmiş Ticaret Camı fiyatları: eşya, efsun, ekipman ve malzeme ilanlarını anlık ara, en uygun fiyatı bul, fiyat geçmişini incele.`}),
 'veri-kaynagi':{title:'Veri Kaynağı · Metin2Search',description:'Metin2Search pazar verisini nasıl topladığını, ne kadar güncel olduğunu ve sınırlarını açıklar.'},
 'cerez-politikasi':{title:'Çerez Politikası · Metin2Search',description:'Metin2Search çerez kullanımı ve çerez tercihlerinin yönetimi.'},
 'gizlilik-politikasi':{title:'Gizlilik Politikası · Metin2Search',description:'Metin2Search gizlilik politikası: hangi veriler işlenir, nasıl korunur.'},
 'kullanim-sartlari':{title:'Kullanım Şartları · Metin2Search',description:'Metin2Search kullanım şartları ve sorumluluk sınırları.'},
 'iletisim':{title:'İletişim · Metin2Search',description:'Metin2Search ile iletişime geç: destek, öneri, hata bildirimi, iş birliği ve veri talepleri için e-posta adresleri.'}
};

const L={
 en:{title:'Metin2 Market & Trade Window Price Search · Turkish (TR) Servers · Metin2Search',description:'Search the Metin2 market (trade window) on the Turkish TR servers: item, bonus and equipment prices, price history and side-by-side listing comparison. Safir, Nite, Star, Lucifer and more.',h1:'Metin2 Market & Trade Window price search',lead:'Metin2Search lets you search shop listings on the Turkish (TR) Metin2 servers in one place: find an item, check the bonuses, compare prices and see how the price changed over time.',points:['Search by item name, bonus, element, level and price','Compare up to three listings side by side','Price history and unit price for stackable items','Listings from several servers, updated continuously'],cta:'Open the market',serversH:'Servers',note:'The market itself is shown in Turkish because these are Turkish Metin2 servers; item names follow the Turkish game client.',more:'Metin2 market, Metin2 trade window, Metin2 item prices and Metin2 auction price check for the Turkish servers.'},
 de:{title:'Metin2 Marktpreise & Handelsfenster · Türkische (TR) Server · Metin2Search',description:'Durchsuche den Metin2-Markt (Handelsfenster) der türkischen TR-Server: Preise für Items, Boni und Ausrüstung, Preisverlauf und Vergleich von Angeboten. Safir, Nite, Star, Lucifer und mehr.',h1:'Metin2 Marktpreise & Handelsfenster durchsuchen',lead:'Metin2Search bündelt die Shop-Angebote der türkischen (TR) Metin2-Server: Item finden, Boni prüfen, Preise vergleichen und den Preisverlauf ansehen.',points:['Suche nach Itemname, Bonus, Element, Level und Preis','Bis zu drei Angebote nebeneinander vergleichen','Preisverlauf und Stückpreis bei stapelbaren Items','Angebote mehrerer Server, laufend aktualisiert'],cta:'Markt öffnen',serversH:'Server',note:'Der Markt selbst ist auf Türkisch, da es türkische Metin2-Server sind; die Itemnamen folgen dem türkischen Spielclient.',more:'Metin2 Markt, Metin2 Handelsfenster, Metin2 Itempreise und Preisvergleich für die türkischen Server.'},
 fr:{title:'Metin2 prix du marché et fenêtre d’échange · serveurs turcs (TR) · Metin2Search',description:'Recherchez le marché Metin2 (fenêtre d’échange) des serveurs turcs TR : prix des objets, bonus et équipements, historique des prix et comparaison d’annonces. Safir, Nite, Star, Lucifer et plus.',h1:'Prix du marché Metin2 et fenêtre d’échange',lead:'Metin2Search réunit les annonces des boutiques des serveurs turcs (TR) de Metin2 : trouvez un objet, vérifiez ses bonus, comparez les prix et suivez leur évolution.',points:['Recherche par nom, bonus, élément, niveau et prix','Comparez jusqu’à trois annonces côte à côte','Historique des prix et prix à l’unité pour les objets empilables','Annonces de plusieurs serveurs, mises à jour en continu'],cta:'Ouvrir le marché',serversH:'Serveurs',note:'Le marché est affiché en turc, car ce sont des serveurs turcs de Metin2 ; les noms d’objets suivent le client de jeu turc.',more:'Marché Metin2, fenêtre d’échange Metin2, prix des objets Metin2 pour les serveurs turcs.'},
 es:{title:'Metin2 precios del mercado y ventana de comercio · servidores turcos (TR) · Metin2Search',description:'Busca en el mercado de Metin2 (ventana de comercio) de los servidores turcos TR: precios de objetos, bonificaciones y equipo, historial de precios y comparación de anuncios. Safir, Nite, Star, Lucifer y más.',h1:'Precios del mercado de Metin2 y ventana de comercio',lead:'Metin2Search reúne los anuncios de las tiendas de los servidores turcos (TR) de Metin2: encuentra un objeto, revisa sus bonificaciones, compara precios y mira cómo han cambiado.',points:['Búsqueda por nombre, bonificación, elemento, nivel y precio','Compara hasta tres anuncios lado a lado','Historial de precios y precio por unidad en objetos apilables','Anuncios de varios servidores, actualizados de forma continua'],cta:'Abrir el mercado',serversH:'Servidores',note:'El mercado se muestra en turco porque son servidores turcos de Metin2; los nombres de objetos siguen el cliente turco del juego.',more:'Mercado de Metin2, ventana de comercio de Metin2 y precios de objetos para los servidores turcos.'},
 pt:{title:'Metin2 preços do mercado e janela de troca · servidores turcos (TR) · Metin2Search',description:'Pesquise o mercado do Metin2 (janela de troca) nos servidores turcos TR: preços de itens, bônus e equipamentos, histórico de preços e comparação de anúncios. Safir, Nite, Star, Lucifer e mais.',h1:'Preços do mercado Metin2 e janela de troca',lead:'O Metin2Search reúne os anúncios das lojas dos servidores turcos (TR) do Metin2: encontre um item, veja os bônus, compare preços e acompanhe a variação ao longo do tempo.',points:['Busca por nome, bônus, elemento, nível e preço','Compare até três anúncios lado a lado','Histórico de preços e preço unitário para itens empilháveis','Anúncios de vários servidores, atualizados continuamente'],cta:'Abrir o mercado',serversH:'Servidores',note:'O mercado é exibido em turco porque são servidores turcos de Metin2; os nomes dos itens seguem o cliente turco do jogo.',more:'Mercado Metin2, janela de troca Metin2 e preços de itens para os servidores turcos.'},
 it:{title:'Metin2 prezzi del mercato e finestra di scambio · server turchi (TR) · Metin2Search',description:'Cerca nel mercato di Metin2 (finestra di scambio) dei server turchi TR: prezzi di oggetti, bonus ed equipaggiamento, storico dei prezzi e confronto degli annunci. Safir, Nite, Star, Lucifer e altri.',h1:'Prezzi del mercato Metin2 e finestra di scambio',lead:'Metin2Search raccoglie gli annunci dei negozi dei server turchi (TR) di Metin2: trova un oggetto, controlla i bonus, confronta i prezzi e guarda come sono cambiati.',points:['Ricerca per nome, bonus, elemento, livello e prezzo','Confronta fino a tre annunci affiancati','Storico dei prezzi e prezzo unitario per gli oggetti impilabili','Annunci di più server, aggiornati di continuo'],cta:'Apri il mercato',serversH:'Server',note:'Il mercato è mostrato in turco perché sono server turchi di Metin2; i nomi degli oggetti seguono il client turco del gioco.',more:'Mercato Metin2, finestra di scambio Metin2 e prezzi degli oggetti per i server turchi.'},
 ru:{title:'Metin2 рынок и торговое окно: цены на предметы · турецкие (TR) серверы · Metin2Search',description:'Поиск по рынку Metin2 (торговое окно) на турецких серверах TR: цены на предметы, бонусы и экипировку, история цен и сравнение объявлений. Safir, Nite, Star, Lucifer и другие.',h1:'Рынок Metin2 и торговое окно: поиск цен',lead:'Metin2Search собирает объявления магазинов на турецких (TR) серверах Metin2: найдите предмет, проверьте бонусы, сравните цены и посмотрите, как они менялись.',points:['Поиск по названию, бонусу, элементу, уровню и цене','Сравнение до трёх объявлений рядом','История цен и цена за штуку для стопок','Объявления с нескольких серверов, обновляются постоянно'],cta:'Открыть рынок',serversH:'Серверы',note:'Рынок показан на турецком языке, так как это турецкие серверы Metin2; названия предметов соответствуют турецкому клиенту игры.',more:'Рынок Metin2, торговое окно Metin2, цены на предметы Metin2 для турецких серверов.'},
 pl:{title:'Metin2 ceny rynkowe i okno handlu · tureckie serwery (TR) · Metin2Search',description:'Przeszukuj rynek Metin2 (okno handlu) na tureckich serwerach TR: ceny przedmiotów, bonusów i ekwipunku, historia cen i porównanie ofert. Safir, Nite, Star, Lucifer i inne.',h1:'Ceny rynkowe Metin2 i okno handlu',lead:'Metin2Search zbiera oferty sklepów z tureckich (TR) serwerów Metin2: znajdź przedmiot, sprawdź bonusy, porównaj ceny i zobacz, jak się zmieniały.',points:['Wyszukiwanie po nazwie, bonusie, żywiole, poziomie i cenie','Porównanie do trzech ofert obok siebie','Historia cen i cena za sztukę dla przedmiotów w stosach','Oferty z wielu serwerów, stale aktualizowane'],cta:'Otwórz rynek',serversH:'Serwery',note:'Rynek jest wyświetlany po turecku, ponieważ to tureckie serwery Metin2; nazwy przedmiotów pochodzą z tureckiego klienta gry.',more:'Rynek Metin2, okno handlu Metin2 i ceny przedmiotów na tureckich serwerach.'},
 ro:{title:'Metin2 prețuri piață și fereastra de comerț · servere turcești (TR) · Metin2Search',description:'Caută pe piața Metin2 (fereastra de comerț) de pe serverele turcești TR: prețuri pentru obiecte, bonusuri și echipament, istoricul prețurilor și compararea anunțurilor. Safir, Nite, Star, Lucifer și altele.',h1:'Prețuri pe piața Metin2 și fereastra de comerț',lead:'Metin2Search adună anunțurile magazinelor de pe serverele turcești (TR) de Metin2: găsește un obiect, verifică bonusurile, compară prețurile și vezi cum s-au schimbat.',points:['Căutare după nume, bonus, element, nivel și preț','Compară până la trei anunțuri alăturate','Istoricul prețurilor și prețul pe bucată pentru obiecte în stivă','Anunțuri de pe mai multe servere, actualizate continuu'],cta:'Deschide piața',serversH:'Servere',note:'Piața este afișată în turcă, fiind servere turcești de Metin2; numele obiectelor urmează clientul turcesc al jocului.',more:'Piața Metin2, fereastra de comerț Metin2 și prețurile obiectelor pentru serverele turcești.'},
 ar:{title:'ميتين2: أسعار السوق ونافذة التجارة · الخوادم التركية (TR) · Metin2Search',description:'ابحث في سوق ميتين2 (نافذة التجارة) على الخوادم التركية TR: أسعار العناصر والإضافات والمعدات، وتاريخ الأسعار ومقارنة العروض. Safir وNite وStar وLucifer وغيرها.',h1:'سوق ميتين2 ونافذة التجارة: البحث عن الأسعار',lead:'يجمع Metin2Search عروض المتاجر على خوادم ميتين2 التركية (TR): ابحث عن عنصر، وتحقق من إضافاته، وقارن الأسعار، وتابع تغيّرها مع الوقت.',points:['بحث بالاسم والإضافة والعنصر والمستوى والسعر','قارن حتى ثلاثة عروض جنبًا إلى جنب','تاريخ الأسعار وسعر الوحدة للعناصر المتراكمة','عروض من عدة خوادم، تُحدَّث باستمرار'],cta:'افتح السوق',serversH:'الخوادم',note:'يُعرض السوق بالتركية لأنها خوادم ميتين2 التركية؛ وأسماء العناصر تتبع عميل اللعبة التركي.',more:'سوق ميتين2، نافذة التجارة في ميتين2، وأسعار العناصر للخوادم التركية.'}
};

function origin(){return String(process.env.SITE_ORIGIN||'http://localhost:4180').replace(/\/+$/,'')}
const langPath=l=>l==='tr'?'/':'/'+l;
function alternates(base){if(!LANGS.length)return '';return [['tr','/'],...LANGS.map(l=>[l,'/'+l]),['x-default','/en']].map(([l,p])=>`<link rel="alternate" hreflang="${l}" href="${base}${p}">`).join('\n')}

function decide({pathname,searchParams,servers=[]}){
 const base=origin(),serverId=searchParams.get('server'),server=servers.find(s=>s.id===serverId);
 const params=[...searchParams.keys()].filter(k=>k!=='server');
 const market=pathname==='/pazar'||pathname==='/market'||(pathname==='/'&&[...searchParams.keys()].length>0);
 if(market){
  const meta=server?TR.server(server.name):TR.market,canonical=base+'/pazar'+(server?'?server='+server.id:'');
  return {...meta,canonical,robots:(params.length||(serverId&&!server)||pathname==='/')?'noindex,follow':'index,follow',type:'market',lang:'tr',server}
 }
 if(pathname==='/')return {...TR.home,canonical:base+'/',robots:'index,follow',type:'home',lang:'tr'};
 const key=pathname.replace(/^\//,'');if(TR[key]&&typeof TR[key]==='object')return {...TR[key],canonical:base+pathname,robots:'index,follow',type:'legal',lang:'tr'};
 return null}

function jsonLd(seo,servers){
 const base=origin(),graph=[{'@type':'WebSite','@id':base+'/#site',url:base+'/',name:SITE_NAME,inLanguage:'tr',potentialAction:{'@type':'SearchAction',target:{'@type':'EntryPoint',urlTemplate:base+'/pazar?q={search_term_string}'},'query-input':'required name=search_term_string'}},
  {'@type':'Organization','@id':base+'/#org',name:SITE_NAME,url:base+'/',logo:base+'/assets/metin2-logo.png',contactPoint:[{'@type':'ContactPoint',contactType:'customer support',email:'destek@example.com',availableLanguage:'Turkish'},{'@type':'ContactPoint',contactType:'business inquiries',email:'iletisim@example.com',availableLanguage:'Turkish'}]}];
 if(seo.type==='home')graph.push({'@type':'FAQPage',mainEntity:[
  {'@type':'Question',name:'Metin2 Ticaret Camı nedir?',acceptedAnswer:{'@type':'Answer',text:'Ticaret Camı, Metin2’de oyuncuların pazar kurup eşya sattığı ve diğer oyuncuların ilanları görüntülediği alandır. Metin2Search bu ilanları tek yerden aramanı, fiyatları karşılaştırmanı ve fiyat geçmişine bakmanı sağlar.'}},
  {'@type':'Question',name:'Pazar fiyatları ne kadar güncel?',acceptedAnswer:{'@type':'Answer',text:'Her ilanın yanında en son ne zaman görüldüğü yazar. Gösterilenler oyun içinde görülen ilanlardır; gerçekleşen satış fiyatı değildir.'}}]});
 if(seo.type==='market')graph.push({'@type':'BreadcrumbList',itemListElement:[{'@type':'ListItem',position:1,name:SITE_NAME,item:base+'/'},{'@type':'ListItem',position:2,name:seo.server?`${seo.server.name} Pazarı`:'Pazar',item:seo.canonical}]});
 return JSON.stringify({'@context':'https://schema.org','@graph':graph}).replace(/</g,'\\u003c')}

function headTags(seo,servers){
 const base=origin(),image=base+'/assets/metin2-logo.png',t=esc(seo.title),d=esc(seo.description);
 return [`<meta name="description" content="${d}">`,`<meta name="robots" content="${seo.robots}">`,`<link rel="canonical" href="${esc(seo.canonical)}">`,
  ...(LANGS.length&&(seo.type==='home'||seo.type==='market')?[alternates(base)]:[]),
  `<meta property="og:type" content="website">`,`<meta property="og:site_name" content="${SITE_NAME}">`,`<meta property="og:locale" content="${OG_LOCALE[seo.lang]||'tr_TR'}">`,`<meta property="og:title" content="${t}">`,`<meta property="og:description" content="${d}">`,`<meta property="og:url" content="${esc(seo.canonical)}">`,`<meta property="og:image" content="${image}">`,
  `<meta name="twitter:card" content="summary">`,`<meta name="twitter:title" content="${t}">`,`<meta name="twitter:description" content="${d}">`,`<meta name="twitter:image" content="${image}">`,
  `<script type="application/ld+json">${jsonLd(seo,servers)}</script>`].join('\n')}

function injectSeo(html,ctx){const seo=decide(ctx);if(!seo)return html;
 let out=html.replace(/<title>[\s\S]*?<\/title>/i,`<title>${esc(seo.title)}</title>`).replace(/<meta name="(?:description|robots)"[^>]*>\s*/gi,'');
 return out.replace('</head>',headTags(seo,ctx.servers||[])+'\n</head>')}

function renderLanding(lang,servers){const p=LANGS.includes(lang)?L[lang]:null;if(!p)return null;const base=origin(),canonical=base+'/'+lang,rtl=lang==='ar',list=servers.filter(s=>s.available!==false);
 const seo={title:p.title,description:p.description,canonical,robots:'index,follow',type:'landing',lang};
 const ld=JSON.stringify({'@context':'https://schema.org','@graph':[{'@type':'WebPage',url:canonical,name:p.title,description:p.description,inLanguage:lang,isPartOf:{'@id':base+'/#site'}},{'@type':'WebSite','@id':base+'/#site',url:base+'/',name:SITE_NAME}]}).replace(/</g,'\\u003c');
 const head=[`<meta name="description" content="${esc(p.description)}">`,`<meta name="robots" content="index,follow">`,`<link rel="canonical" href="${canonical}">`,alternates(base),`<meta property="og:type" content="website">`,`<meta property="og:site_name" content="${SITE_NAME}">`,`<meta property="og:locale" content="${OG_LOCALE[lang]}">`,`<meta property="og:title" content="${esc(p.title)}">`,`<meta property="og:description" content="${esc(p.description)}">`,`<meta property="og:url" content="${canonical}">`,`<meta property="og:image" content="${base}/assets/metin2-logo.png">`,`<meta name="twitter:card" content="summary">`,`<script type="application/ld+json">${ld}</script>`].join('\n');
 const switcher=['tr',...LANGS].map(l=>l===lang?`<b>${LANG_NAMES[l]}</b>`:`<a href="${langPath(l)}" hreflang="${l}" lang="${l}">${LANG_NAMES[l]}</a>`).join('');
 return `<!doctype html>
<html lang="${lang}"${rtl?' dir="rtl"':''}>
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width,initial-scale=1">
<meta name="theme-color" content="#1d1f25">
<title>${esc(p.title)}</title>
${head}
<link rel="preconnect" href="https://fonts.googleapis.com">
<link rel="preconnect" href="https://fonts.gstatic.com" crossorigin>
<link href="https://fonts.googleapis.com/css2?family=Cinzel:wght@700&family=Inter:wght@400;500;600;700&display=swap" rel="stylesheet">
<link rel="stylesheet" href="/dil.css?v=1">
<link rel="icon" type="image/svg+xml" href="/favicon.svg">
</head>
<body>
<header class="lp-top"><a class="lp-brand" href="/"><img src="/assets/metin2-logo.png" alt="" width="36" height="36"><span>METIN2 <b>SEARCH</b></span></a><a class="lp-btn" href="/pazar?server=safir">${esc(p.cta)}</a></header>
<main class="lp">
<h1>${esc(p.h1)}</h1>
<p class="lp-lead">${esc(p.lead)}</p>
<ul class="lp-points">${p.points.map(x=>`<li>${esc(x)}</li>`).join('')}</ul>
<p><a class="lp-btn big" href="/pazar?server=safir">${esc(p.cta)}</a></p>
${list.length?`<section><h2>${esc(p.serversH)}</h2><ul class="lp-servers">${list.map(s=>`<li><a href="/pazar?server=${esc(s.id)}">${esc(s.name)}</a></li>`).join('')}</ul></section>`:''}
<p class="lp-note">${esc(p.note)}</p>
<p class="lp-more">${esc(p.more)}</p>
</main>
<footer class="lp-foot"><nav class="lp-langs" aria-label="Language">${switcher}</nav><p>METIN2SEARCH © 2026 · <a href="/veri-kaynagi">Veri Kaynağı</a> · <a href="/gizlilik-politikasi">Gizlilik</a> · <a href="/kullanim-sartlari">Kullanım Şartları</a></p><p class="lp-legal">Metin2Search is an unofficial community tool and is not affiliated with Webzen Inc. or Gameforge. Metin2 and related trademarks belong to their respective owners.</p></footer>
</body>
</html>
`}

function sitemap(servers){const base=origin(),langLinks=!LANGS.length?'':[['tr','/'],...LANGS.map(l=>[l,'/'+l]),['x-default','/en']].map(([l,p])=>`<xhtml:link rel="alternate" hreflang="${l}" href="${base}${p}"/>`).join('');
 const url=(p,extra='',pri='0.6')=>` <url><loc>${base}${p.replace(/&/g,'&amp;')}</loc>${extra}<priority>${pri}</priority></url>`;
 const lines=[url('/',langLinks,'1.0'),url('/pazar','','0.9'),...servers.filter(s=>s.available!==false).map(s=>url('/pazar?server='+s.id,'','0.8')),...LANGS.map(l=>url('/'+l,langLinks,'0.7')),...['veri-kaynagi','cerez-politikasi','gizlilik-politikasi','kullanim-sartlari'].map(k=>url('/'+k,'','0.2')),url('/iletisim','','0.3')];
 return '<?xml version="1.0" encoding="UTF-8"?>\n<urlset xmlns="http://www.sitemaps.org/schemas/sitemap/0.9" xmlns:xhtml="http://www.w3.org/1999/xhtml">\n'+lines.join('\n')+'\n</urlset>\n'}
function robots(){const base=origin();return ['User-agent: *','Allow: /','Disallow: /api/','Disallow: /local/','Disallow: /ingest/','Disallow: /dogrula','Disallow: /bonus-color-editor','','Sitemap: '+base+'/sitemap.xml',''].join('\n')}
module.exports={LANGS,L,origin,decide,injectSeo,renderLanding,sitemap,robots,headTags};
