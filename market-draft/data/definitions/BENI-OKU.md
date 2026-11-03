# Oyun veri tabloları (bu klasör bilerek boş)

Site, oyuna ait eşya tablolarına ihtiyaç duyar. Bu tablolar oyunun telifli içeriğinden türetildiği için depoya **dahil edilmemiştir**. Kendi oyun istemcinizden üretip bu klasöre koymanız gerekir. Bu depoda tabloları çıkaran bir araç yoktur.

Beklenen dosyalar (hepsi UTF-8 JSON):

| Dosya | Biçim |
|---|---|
| `item_proto.json` | `{ "<vnum>": { "Vnum", "Name", "Type", "SubType", "Size", "AntiFlags", "Flags", "WearFlags", "LimitType0", "LimitValue0", "LimitType1", "LimitValue1", "ApplyType0", "ApplyValue0", … "ApplyType3", "ApplyValue3", "Value0" … "Value11", … } }` (oyunun `item_proto` alanları) |
| `item_names.json` | `{ "<vnum>": "Eşya adı" }` |
| `item_icon.json` | `{ "<vnum>": "simge-dosya-adi.png" }` (dosyalar `assets/items/` altında aranır) |
| `stat_map.json` | `{ "<apply numarası>": "Max. HP +%d" }` biçiminde efsun/özellik metinleri |
| `client-stat-map.json` | `stat_map.json` ile aynı biçim, istemciden okunan metinler |
| `proto_stat_map.json` | `{ "APPLY_MAX_HP": 1, … }` (apply adı → numara) |

Bu dosyalar yoksa site başlamaz (`start-site.ps1` eksik dosyaları adıyla bildirir).