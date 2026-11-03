const ICON_PATHS={search:'<circle cx="10" cy="10" r="6"/><path d="m15 15 6 6"/>',globe:'<circle cx="12" cy="12" r="9"/><ellipse cx="12" cy="12" rx="4" ry="9"/><path d="M3 12h18"/>',compass:'<circle cx="12" cy="12" r="9"/><path d="m16 8-2 6-6 2 2-6z"/>',bookmark:'<path d="M6 3h12v18l-6-4-6 4z"/>',compare:'<rect x="3" y="4" width="7" height="16" rx="1"/><rect x="14" y="4" width="7" height="16" rx="1"/>',shield:'<path d="m12 3 8 3v6c0 5-8 9-8 9s-8-4-8-9V6z"/><path d="m8 12 3 3 5-6"/>',info:'<circle cx="12" cy="12" r="9"/><path d="M12 10v7m0-11v.1"/>',database:'<ellipse cx="12" cy="5" rx="8" ry="3"/><path d="M4 5v14c0 4 16 4 16 0V5M4 12c0 4 16 4 16 0"/>',sliders:'<path d="M4 6h16M4 12h16M4 18h16M8 3v6m8 0v6M9 15v6"/>',sort:'<path d="M3 6h12M3 12h9M3 18h6M19 5v14m-3-3 3 3 3-3"/>',cursor:'<path d="m5 3 13 10-6 1-3 6z"/>',sword:'<path d="m4 20 5-5m-3-3 6 6M8 14 18 3h3v3L11 17"/>',armor:'<path d="m8 3 4 3 4-3 6 5-4 4-2-2v11H8V10l-2 2-4-4z"/>',gem:'<path d="m7 3-5 7 10 11 10-11-5-7zM2 10h20M7 3l5 18 5-18"/>',book:'<path d="M3 4h7l2 2 2-2h7v16h-7l-2 2-2-2H3zM12 6v16"/>',box:'<path d="m12 2 9 5v10l-9 5-9-5V7zM3 7l9 5 9-5M12 12v10"/>',ring:'<circle cx="12" cy="14" r="7"/><path d="m8 5 4-3 4 3-4 5z"/>',paw:'<ellipse cx="12" cy="16" rx="6" ry="5"/><circle cx="5" cy="9" r="2"/><circle cx="10" cy="5" r="2"/><circle cx="16" cy="6" r="2"/><circle cx="20" cy="11" r="2"/>',shop:'<path d="M3 9 5 3h14l2 6M3 9c0 4 5 4 5 0 0 4 8 4 8 0 0 4 5 4 5 0M5 12v9h14v-9M9 21v-7h6v7M3 9h18"/>',flask:'<path d="M8 3h8M10 3v6l-6 10c-1 2 15 2 16 0L14 9V3M7 15h10"/>',pick:'<path d="m4 21 12-12M4 4c7-3 13 0 17 7l-5-2-4-5z"/>',layers:'<path d="m12 3 10 5-10 5L2 8zM2 13l10 5 10-5M2 18l10 5 10-5"/>'};
Object.assign(ICON_PATHS,{
clock:'<circle cx="12" cy="12" r="9"/><path d="M12 7v5l3 2"/>',
history:'<path d="M3 12a9 9 0 1 0 3-6.7"/><path d="M3 4v5h5"/><path d="M12 7v5l3 2"/>',
close:'<path d="M6 6l12 12M18 6 6 18"/>',
chevron:'<path d="m6 9 6 6 6-6"/>',
arrow:'<path d="M5 12h14m-5-5 5 5-5 5"/>',
external:'<path d="M14 4h6v6M20 4l-9 9M18 14v5a1 1 0 0 1-1 1H5a1 1 0 0 1-1-1V7a1 1 0 0 1 1-1h5"/>',
reset:'<path d="M20 7v5h-5"/><path d="M19.2 12a7.2 7.2 0 1 1-2-5L20 10"/>',
plus:'<path d="M12 5v14M5 12h14"/>',
grid:'<rect x="4" y="4" width="7" height="7" rx="1"/><rect x="13" y="4" width="7" height="7" rx="1"/><rect x="4" y="13" width="7" height="7" rx="1"/><rect x="13" y="13" width="7" height="7" rx="1"/>',
list:'<path d="M8 6h12M8 12h12M8 18h12M4 6h.01M4 12h.01M4 18h.01"/>',
check:'<path d="m5 12 5 5 9-10"/>',
stone:'<path d="m12 3 7 5-2.5 10h-9L5 8z"/><path d="M5 8h14M12 3v15"/>',
chart:'<path d="M4 19h16"/><path d="m5 15 4-5 4 3 6-7"/>',
discord:'<path d="M8 8c3-1.3 5-1.3 8 0M7 16c3 1.4 7 1.4 10 0M8.5 7 7 4c-1.6.4-3 1-4.2 2C1.4 9.6 1 13 1.2 16.4 2.6 17.6 4 18.3 5.6 18.8L7 16.5M15.5 7 17 4c1.6.4 3 1 4.2 2 1.4 3.6 1.8 7 1.6 10.4-1.4 1.2-2.8 1.9-4.4 2.4L17 16.5"/><circle cx="9" cy="12" r="1.2"/><circle cx="15" cy="12" r="1.2"/>'
});
const ic=(name,cls='')=>`<svg class="ic ${cls}" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.6" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">${ICON_PATHS[name]||''}</svg>`;
document.querySelectorAll('[data-ic]').forEach(el=>el.insertAdjacentHTML('afterbegin',ic(el.dataset.ic)));
