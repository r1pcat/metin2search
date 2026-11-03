const fs=require('node:fs'),path=require('node:path');
const dir=path.join(__dirname,'assets','ads');fs.mkdirSync(dir,{recursive:true});
const FONT="'Segoe UI','Inter','Helvetica Neue',Arial,sans-serif",GOLD='#dcbd7f';
const DISCORD='M40.575 0C39.9562 1.09866 39.4006 2.2352 38.8954 3.397C34.0967 2.67719 29.2096 2.67719 24.3982 3.397C23.9057 2.2352 23.3374 1.09866 22.7186 0C18.2104 0.770324 13.8157 2.12155 9.64839 4.02841C1.38951 16.2652 -0.845688 28.1863 0.265599 39.9432C5.10222 43.517 10.5197 46.2447 16.2909 47.9874C17.5916 46.2447 18.7407 44.3883 19.7257 42.4562C17.8568 41.7616 16.0509 40.8903 14.3208 39.88C14.7755 39.5517 15.2175 39.2107 15.6468 38.8824C25.7873 43.6559 37.5316 43.6559 47.6847 38.8824C48.1141 39.236 48.5561 39.577 49.0107 39.88C47.2806 40.9029 45.4748 41.7616 43.5931 42.4688C44.5781 44.4009 45.7273 46.2573 47.028 48C52.7991 46.2573 58.2167 43.5422 63.0533 39.9684C64.3666 26.3299 60.8055 14.5099 53.6452 4.04104C49.4905 2.13418 45.0959 0.782952 40.5876 0.0252565L40.575 0ZM21.1401 32.7072C18.0209 32.7072 15.4321 29.8785 15.4321 26.3804C15.4321 22.8824 17.9199 20.041 21.1275 20.041C24.3351 20.041 26.886 22.895 26.8354 26.3804C26.7849 29.8658 24.3224 32.7072 21.1401 32.7072ZM42.1788 32.7072C39.047 32.7072 36.4834 29.8785 36.4834 26.3804C36.4834 22.8824 38.9712 20.041 42.1788 20.041C45.3864 20.041 47.9246 22.895 47.8741 26.3804C47.8236 29.8658 45.3611 32.7072 42.1788 32.7072Z';

const icons={
  discord:`<g transform="translate(0 6) scale(.75)"><path d="${DISCORD}" fill="#fff"/></g>`,
  chart:`<circle cx="21" cy="21" r="12" fill="none" stroke="#fff" stroke-width="3.2"/><path d="m30 30 11 11" stroke="#fff" stroke-width="3.6" stroke-linecap="round"/><path d="M14 25l5-6 4 3 5-7" fill="none" stroke="${GOLD}" stroke-width="2.8" stroke-linecap="round" stroke-linejoin="round"/>`,
  mega:`<path d="M8 19v10h6l14 8V11l-14 8H8z" fill="#fff"/><path d="M33 17c3 3 3 11 0 14M38 12c6 6 6 18 0 24" fill="none" stroke="${GOLD}" stroke-width="3" stroke-linecap="round"/>`
};
const ADS=[
  {id:'discord',c1:'#5865F2',c2:'#232a6b',icon:'discord',cta:"Discord'a katıl",
    sky:{title:['Öneri ve','görüşlerinizi','bize iletmek','için Discord','adresimize','katılın'],sub:['Fikirleriniz Metin2Search’ü','birlikte şekillendirir.']},
    wide:{title:['Öneri ve görüşlerinizi bize iletmek için','Discord adresimize katılın']},
    compact:{title:['Öneri ve görüşleriniz için','Discord adresimize katılın']}},
  {id:'pazar',c1:'#c9a45e',c2:'#3a2d12',icon:'chart',cta:'Pazarı keşfet',
    sky:{title:['Doğru fiyatı','bil, doğru','alışveriş','yap'],sub:['Fiyat geçmişi, karşılaştırma','ve efsun filtreleri','tek yerde.']},
    wide:{title:['Doğru fiyatı bil, doğru alışveriş yap','Fiyat geçmişi, karşılaştırma ve efsun filtreleri']},
    compact:{title:['Doğru fiyatı bil, doğru al','Geçmiş ve karşılaştırma']}},
  {id:'reklam',c1:'#2fa58f',c2:'#0f3a35',icon:'mega',cta:'Bize ulaşın',
    sky:{title:['Reklamınız','Metin2','topluluğuna','ulaşsın'],sub:['Bu alanda yer almak için','Discord’dan bize yazın.']},
    wide:{title:['Reklamınız Metin2 topluluğuna ulaşsın','Bu alanda yer almak için Discord’dan bize yazın']},
    compact:{title:['Reklamınız toplulukta','görünsün · Discord’dan yazın']}}
];
const esc=s=>s.replace(/&/g,'&amp;').replace(/</g,'&lt;');
const lines=(arr,x,y,size,lh,fill,weight,anchor='middle')=>arr.map((t,i)=>`<text x="${x}" y="${y+i*lh}" font-size="${size}" font-weight="${weight}" fill="${fill}" text-anchor="${anchor}">${esc(t)}</text>`).join('');
const head=(w,h,ad)=>`<svg xmlns="http://www.w3.org/2000/svg" width="${w}" height="${h}" viewBox="0 0 ${w} ${h}" font-family="${FONT}" role="img" aria-label="${esc(ad.sky.title.join(' ')+' '+ad.sky.sub.join(' '))}"><defs><linearGradient id="g" x1="0" y1="0" x2="1" y2="1"><stop offset="0" stop-color="${ad.c1}"/><stop offset="1" stop-color="${ad.c2}"/></linearGradient><radialGradient id="r" cx=".85" cy=".1" r=".9"><stop offset="0" stop-color="#fff" stop-opacity=".28"/><stop offset="1" stop-color="#fff" stop-opacity="0"/></radialGradient><pattern id="p" width="14" height="14" patternUnits="userSpaceOnUse"><circle cx="1.5" cy="1.5" r="1" fill="#fff" fill-opacity=".09"/></pattern></defs><rect width="${w}" height="${h}" fill="url(#g)"/><rect width="${w}" height="${h}" fill="url(#p)"/><rect width="${w}" height="${h}" fill="url(#r)"/>`;
const tag=(x,y,anchor)=>`<text x="${x}" y="${y}" font-size="8.5" font-weight="700" letter-spacing="1.6" fill="#fff" fill-opacity=".55" text-anchor="${anchor}">TANITIM</text>`;
const pill=(x,y,w,h,label,size)=>`<rect x="${x}" y="${y}" width="${w}" height="${h}" rx="${h/2}" fill="#fff"/><text x="${x+w/2}" y="${y+h/2+size*.35}" font-size="${size}" font-weight="700" fill="#1b1d23" text-anchor="middle">${esc(label)}</text>`;
const icon=(name,x,y,s)=>`<g transform="translate(${x} ${y}) scale(${s/48})">${icons[name]}</g>`;
const out={
  sky:ad=>`${head(160,600,ad)}<circle cx="80" cy="104" r="46" fill="#fff" fill-opacity=".14"/>${icon(ad.icon,50,74,60)}${lines(ad.sky.title,80,206,19,26,'#fff',700)}<rect x="62" y="${206+ad.sky.title.length*26-8}" width="36" height="3" rx="1.5" fill="${GOLD}"/>${lines(ad.sky.sub,80,206+ad.sky.title.length*26+26,12.5,18,'#fff',400)}${pill(20,516,120,38,ad.cta,13.5)}${tag(80,583,'middle')}</svg>`,
  wide:ad=>`${head(728,90,ad)}<circle cx="46" cy="45" r="30" fill="#fff" fill-opacity=".14"/>${icon(ad.icon,24,23,44)}${lines(ad.wide.title,92,38,19,26,'#fff',700,'start')}${tag(92,82,'start')}${pill(566,24,140,42,ad.cta,14.5)}</svg>`,
  compact:ad=>`${head(320,100,ad)}<circle cx="34" cy="50" r="24" fill="#fff" fill-opacity=".14"/>${icon(ad.icon,16,32,36)}${lines(ad.compact.title,70,36,13,19,'#fff',700,'start')}${pill(70,60,104,26,ad.cta,12)}${tag(306,92,'end')}</svg>`
};
const fixWide=svg=>svg.replace(/(<text x="92" y="64"[^>]*?)font-size="19" font-weight="700" fill="#fff"/,'$1font-size="13.5" font-weight="500" fill="#fff" fill-opacity=".85"');
for(const ad of ADS)for(const fmt of ['sky','wide','compact']){let svg=out[fmt](ad);if(fmt==='wide')svg=fixWide(svg);fs.writeFileSync(path.join(dir,`${ad.id}-${fmt}.svg`),svg)}
console.log(ADS.length*3+' reklam görseli yazıldı: '+dir);
