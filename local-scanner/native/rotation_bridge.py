_rotation_context = None
_rotation_moving = False
_rotation_transfer_pending = None

def _rotation_cells(rows, width, page_size):
    item = _module_with('SelectItem', 'GetItemSize')
    if item is None or width != 5 or page_size < width or page_size % width:
        raise ValueError('Esya hucre boyutu profili dogrulanamadi')
    occupied = set()
    sizes = {}
    for slot, vnum, count in rows:
        if not vnum:
            continue
        if vnum not in sizes:
            item.SelectItem(vnum)
            sizes[vnum] = tuple(int(n) for n in item.GetItemSize())
        sx, sy = sizes[vnum]
        if sx < 1 or sx > width or sy < 1 or sy > page_size // width or (vnum == 60005 and (sx, sy) != (1,1)):
            raise ValueError('Esya kapladigi hucreler dogrulanamadi')
        if (slot % page_size) % width + sx > width or (slot % page_size) // width + sy > page_size // width:
            raise ValueError('Esya sayfa yerlesimi gecersiz')
        for dy in range(sy):
            for dx in range(sx):
                occupied.add(slot + dy * width + dx)
    return [(slot, 4294967295 if vnum == 0 and slot in occupied else vnum, count) for slot, vnum, count in rows]

def _rotation_world():
    state = _gamestate()
    if not state['playerReady'] or not state['inventoryReady'] or state['channel'] != 1 or state['phase'] != 'game':
        raise ValueError('Karakter/depo islemi icin oyun ve kanal 1 hazir olmali')
    background = _module_with('GetCurrentMapName')
    if background is None or str(background.GetCurrentMapName()) != 'metin2_map_privateshop':
        raise ValueError('Karakter devri yalniz Ticaret haritasinda yapilir')
    return state

def _rotation_storage():
    safebox = _module_with('GetItemID', 'GetItemCount', 'GetMallItemID')
    if safebox is None:
        raise ValueError('Depo item API bulunamadi')
    wnd = _native_game_window().interface.wndSafebox
    opened = bool(wnd.IsShow())
    size = 0
    if opened:
        visible = int(wnd.wndItem.GetSlotCount())
        pages = max(1, len(wnd.pageButtonList))
        if visible < 5 or visible > 45 or visible % 5 or pages > 6 or (pages > 1 and visible != 45):
            raise ValueError('Depo hucre tablosu hazir degil veya boyutu gecersiz')
        size = visible * pages
    rows = [(i, int(safebox.GetItemID(i)), int(safebox.GetItemCount(i))) for i in range(size)]
    if opened:
        width = int(getattr(safebox, 'SAFEBOX_SLOT_X_COUNT', 5))
        page_size = int(getattr(safebox, 'SAFEBOX_PAGE_SIZE', 45))
        if width != 5 or page_size != 45:
            raise ValueError('Depo hucre duzeni istemci profiliyle uyusmuyor')
        rows = _rotation_cells(rows, width, page_size)
    return opened, rows

def _rotation_character_list():
    network = _module_with('GetAccountCharacterSlotDataString')
    if network is None or not hasattr(network, 'ACCOUNT_CHARACTER_SLOT_NAME'):
        raise ValueError('Karakter listesi API bulunamadi')
    roster = [_text(network.GetAccountCharacterSlotDataString(i, network.ACCOUNT_CHARACTER_SLOT_NAME)) for i in range(5)]
    if len(set(name for name in roster if name)) != len([name for name in roster if name]):
        raise ValueError('Karakter adlari tekrarlaniyor')
    order = list(range(5))
    phase, window = _phase()
    mapping = getattr(getattr(window, 'mycharacters', None), 'RealSlot', None) if phase == 'select' else None
    if mapping is not None:
        try:
            mapped = [int(mapping[i]) for i in range(len([n for n in roster if n]))]
            if len(set(mapped)) != len(mapped) or any(i < 0 or i >= 5 or not roster[i] for i in mapped):
                raise ValueError('Karakter ekraninin gercek yuva tablosu gecersiz')
            order = mapped + [i for i in range(5) if i not in mapped]
        except (KeyError, IndexError, TypeError):
            raise ValueError('Karakter ekraninin gercek yuva tablosu okunamadi')
    elif callable(getattr(network, 'GetAccountCharacterSlotDataInteger', None)) and hasattr(network, 'ACCOUNT_CHARACTER_SLOT_LAST_PLAYTIME'):
        times = [int(network.GetAccountCharacterSlotDataInteger(i, network.ACCOUNT_CHARACTER_SLOT_LAST_PLAYTIME)) for i in range(5)]
        order = sorted(order, key=lambda i:(not bool(roster[i]), -times[i], i))
    if phase == 'game':
        player = _module_with('GetName', 'GetElk', 'GetCheque')
        active = _text(player.GetName()) if player is not None else ''
        matches = [i for i in range(5) if active and roster[i] == active]
        if len(matches) == 1:
            order = matches + [i for i in order if i != matches[0]]
    return {'roster': roster, 'displaySlots': order}

def _rotation_roster():
    data = _rotation_character_list()
    player = _module_with('GetName', 'GetElk', 'GetCheque')
    if player is None:
        raise ValueError('Karakter kimligi API bulunamadi')
    name = _text(player.GetName())
    roster = data['roster']
    matches = [i for i in range(5) if name and roster[i] == name]
    if len(matches) != 1:
        raise ValueError('Aktif karakter yuvasi dogrulanamadi')
    return name, roster, matches[0]

def _rotation_status():
    global _rotation_transfer_pending
    state = _rotation_world()
    name, roster, slot = _rotation_roster()
    opened, stored = _rotation_storage()
    player = _module_with('GetItemIndex', 'GetItemCount', 'GetName')
    capacity = int(_native_world()['capacity'])
    if player is None or capacity < 1 or capacity > 200:
        raise ValueError('Envanter item API hazir degil')
    capacity = min(capacity, int(getattr(player, 'INVENTORY_SLOT_COUNT', capacity)))
    rows = _rotation_cells([(i, int(player.GetItemIndex(i)), int(player.GetItemCount(i))) for i in range(capacity)], 5, int(player.INVENTORY_PAGE_SIZE))
    if _rotation_transfer_pending is not None and _rotation_transfer_pending[1] == slot and opened:
        action, old_slot, inv_slot, box_slot = _rotation_transfer_pending
        inv = dict((r[0], tuple(r[1:])) for r in rows)
        box = dict((r[0], tuple(r[1:])) for r in stored)
        confirmed = (inv.get(inv_slot, (-1,0))[0] == 0 and box.get(box_slot) == (60005,1)) if action == 'deposit' else (box.get(box_slot, (-1,0))[0] == 0 and inv.get(inv_slot) == (60005,1))
        if confirmed:
            _rotation_transfer_pending = None
    return {'serverInfo': state['serverInfo'], 'character': name, 'roster': roster, 'characterSlot': slot,
            'displaySlots': _rotation_character_list()['displaySlots'], 'inventory': rows, 'storageOpen': opened, 'storage': stored}

def _rotation_navigation(grid):
    _rotation_world()
    return _native_navigation(int(grid))

def _rotation_walk(x, y):
    global _rotation_moving
    _rotation_world()
    result = _native_walk('%d %d' % (int(x), int(y)))
    _rotation_moving = True
    return {'requested': bool(result)}

def _rotation_walk_stop():
    global _rotation_moving
    try:
        _native_stop_walk()
    finally:
        _rotation_moving = False
    return {'stopped': True}

def _rotation_prepare():
    global _rotation_context, _rotation_transfer_pending
    _rotation_world()
    _purchase_stop()
    _market_end()
    if _native_snapshot()['shopOpen']:
        _module_with('SendShopEndPacket').SendShopEndPacket()
        _native_game_window().EndShop()
    wnd = _native_game_window().interface.wndPrivateShopSearch
    if wnd.IsShow():
        wnd.CloseProcess(1)
    _rotation_trade_closed()
    _rotation_context = {'npcClicked': False, 'answerSelected': False, 'passwordSent': False}
    return {'prepared': True, 'glassClosed': True, 'shopClosed': True}

def _rotation_trade_closed():
    if _native_game_window().interface.wndPrivateShopSearch.IsShow() or _native_snapshot()['shopOpen']:
        raise ValueError('Pazar/Ticaret Cami kapanmadi; depo devri baslamadi')

def _rotation_near_npc():
    nav = _rotation_navigation(0)
    if int(nav['npcVid']) <= 0 or (nav['x']-nav['npcX'])**2 + (nav['y']-nav['npcY'])**2 > 350**2:
        raise ValueError('Depocu canli kimlik/mesafe dogrulamasi basarisiz')
    return nav

def _rotation_password_dialog():
    import uiSafebox
    provider = _module_with('SendSafeboxCheckinPacket')
    if provider is None or not hasattr(provider, 'SAFEBOX_PASSWORD_TYPE'):
        raise ValueError('Depo parola tipi bulunamadi')
    dialogs = {}
    for value in gc.get_objects():
        try:
            if isinstance(value, uiSafebox.PasswordDialog) and _rotation_dialog_visible(value) and value.send_type == provider.SAFEBOX_PASSWORD_TYPE:
                dialogs[_rotation_dialog_key(value)] = value
        except (ReferenceError, AttributeError, TypeError):
            pass
    if len(dialogs) > 1:
        raise ValueError('Birden cok depo parola penceresi var')
    return dialogs.values()[0] if dialogs else None

def _rotation_dialog_key(value):
    handle = getattr(value, 'hWnd', None)
    if handle is not None and int(handle) > 0:
        return ('window', int(handle))
    data = getattr(value, '__dict__', None)
    if data is not None:
        return ('instance', id(data))
    return ('event', int(value.descIndex))

def _rotation_dialog_visible(value):
    if not value.IsShow():
        return False
    board = getattr(value, 'board', None)
    return board is None or not callable(getattr(board, 'IsShow', None)) or bool(board.IsShow())

def _rotation_storage_menu():
    dialogs = {}
    for value in gc.get_objects():
        try:
            if value.__class__.__name__ != 'QuestDialog' or not _rotation_dialog_visible(value):
                continue
            if hasattr(value, 'descIndex') and callable(getattr(value, 'ClickAnswerEvent', None)):
                dialogs[_rotation_dialog_key(value)] = value
        except (ReferenceError, AttributeError, TypeError, ValueError):
            pass
    matches = []
    labelled = False
    for value in dialogs.values():
        buttons = getattr(value, 'btnAnswer', [])
        rows = buttons.items() if isinstance(buttons, dict) else enumerate(buttons)
        for index, button in rows:
            try:
                if callable(getattr(button, 'IsShow', None)) and not button.IsShow():
                    continue
                text = u' '.join(_rotation_normalize(button.GetText()).split())
                labelled = labelled or bool(text)
                if text in (u'depoyu ac', u'depo ac', u'open storage', u'open warehouse'):
                    matches.append((value, int(index)))
            except (ReferenceError, AttributeError, TypeError, ValueError):
                pass
    if len(matches) == 1:
        return matches[0]
    if len(matches) > 1:
        raise ValueError('Birden fazla farkli Depoyu ac secenegi var; cevap gonderilmedi')
    if len(dialogs) == 1 and not labelled:
        return (dialogs.values()[0], 1)
    return None

def _rotation_open():
    if _rotation_context is None:
        raise ValueError('Depo devri hazirlanmadi')
    _rotation_trade_closed()
    nav = _rotation_near_npc()
    opened, rows = _rotation_storage()
    if opened:
        return {'open': True, 'passwordReady': False}
    if _rotation_password_dialog() is not None:
        return {'open': False, 'passwordReady': True}
    if not _rotation_context['npcClicked']:
        _module_with('SendOnClickPacket').SendOnClickPacket(int(nav['npcVid']))
        _rotation_context['npcClicked'] = True
        return {'open': False, 'passwordReady': False}
    if not _rotation_context['answerSelected']:
        selected = _rotation_storage_menu()
        if selected:
            _rotation_context['answerSelected'] = True
            selected[0].ClickAnswerEvent(selected[1])
    return {'open': False, 'passwordReady': False}

def _rotation_normalize(text):
    text = _text(text).lower()
    for source, target in ((u'\u0131',u'i'),(u'\u015f',u's'),(u'\u00fc',u'u'),(u'\u00f6',u'o'),(u'\u00e7',u'c'),(u'\u011f',u'g')):
        text = text.replace(source, target)
    return text

def _rotation_password(hexed):
    if _rotation_context is None or _rotation_context['passwordSent']:
        raise ValueError('Depo parola istegi daha once gonderildi veya baglam yok')
    _rotation_trade_closed()
    _rotation_near_npc()
    dialog = _rotation_password_dialog()
    if dialog is None:
        raise ValueError('Dogrulanmis depo parola penceresi yok')
    if len(hexed) < 2 or len(hexed) > 32 or len(hexed) % 2:
        raise ValueError('Depo parola bicimi gecersiz')
    password = ''.join(chr(int(hexed[i:i+2],16)) for i in range(0,len(hexed),2))
    if any(ord(c) < 33 or ord(c) > 126 for c in password):
        raise ValueError('Depo parola bicimi gecersiz')
    _rotation_context['passwordSent'] = True
    try:
        dialog.passwordValue.SetText(password)
        dialog.OnAccept()
    except Exception:
        raise ValueError('Depo parola adimi tamamlanamadi; otomatik tekrar yapilmaz')
    return {'submitted': True}

def _rotation_transfer(action, inventory_slot, storage_slot):
    global _rotation_transfer_pending
    _rotation_trade_closed()
    _rotation_near_npc()
    state = _rotation_status()
    if not state['storageOpen']:
        raise ValueError('Depo acik degil')
    inv = dict((r[0], r[1:]) for r in state['inventory'])
    stored = dict((r[0], r[1:]) for r in state['storage'])
    inventory_slot, storage_slot = int(inventory_slot), int(storage_slot)
    if inventory_slot not in inv or storage_slot not in stored:
        raise ValueError('Depo/envanter yuvasi gecersiz')
    signature = (action, state['characterSlot'], inventory_slot, storage_slot)
    if _rotation_transfer_pending is not None:
        old = _rotation_transfer_pending
        if old == signature:
            raise ValueError('Ayni depo istegi tekrar gonderilemez; durum okunmali')
        if old[0] == 'deposit' and not (stored.get(old[3], (0,0)) == (60005,1) and inv.get(old[2], (0,0))[0] == 0):
            raise ValueError('Onceki depo istegi belirsiz')
        if old[0] == 'withdraw' and not (inv.get(old[2], (0,0)) == (60005,1) and stored.get(old[3], (0,0))[0] == 0):
            raise ValueError('Onceki depo istegi belirsiz')
    if action == 'deposit':
        if tuple(inv[inventory_slot]) != (60005,1) or stored[storage_slot][0] != 0:
            raise ValueError('Yalniz tek Ticaret Cami bos depoya aktarilabilir')
        _rotation_transfer_pending = signature
        _module_with('SendSafeboxCheckinPacket').SendSafeboxCheckinPacket(1, inventory_slot, storage_slot)
    elif action == 'withdraw':
        if tuple(stored[storage_slot]) != (60005,1) or inv[inventory_slot][0] != 0:
            raise ValueError('Yalniz tek Ticaret Cami bos envantere alinabilir')
        _rotation_transfer_pending = signature
        _module_with('SendSafeboxCheckoutPacket').SendSafeboxCheckoutPacket(storage_slot, 1, inventory_slot)
    else:
        raise ValueError('Depo islemi gecersiz')
    return {'sent': True}

def _rotation_close_storage():
    opened, rows = _rotation_storage()
    if opened:
        _native_game_window().interface.wndSafebox.Close()
    return {'closed': not _rotation_storage()[0]}

def _rotation_logout():
    _rotation_world()
    _rotation_trade_closed()
    if _rotation_storage()[0]:
        raise ValueError('Depo kapanmadan karakter degistirilemez')
    network = _module_with('LogOutGame')
    if network is None:
        raise ValueError('Istemci cikis API bulunamadi')
    network.LogOutGame()
    return {'requested': True}

def _rotation_dispatch(command, args):
    if command == 'characterlist': return _rotation_character_list()
    if command == 'rotationstatus': return _rotation_status()
    if command == 'rotationprepare': return _rotation_prepare()
    if command == 'navinfo': return _rotation_navigation(0)
    if command == 'navgrid': return _rotation_navigation(1)
    if command == 'navwalk': return _rotation_walk(*args)
    if command == 'navstop': return _rotation_walk_stop()
    if command == 'storageopen': return _rotation_open()
    if command == 'storagepassword': return _rotation_password(*args)
    if command == 'storagedeposit': return _rotation_transfer('deposit', *args)
    if command == 'storagewithdraw': return _rotation_transfer('withdraw', *args)
    if command == 'storageclose': return _rotation_close_storage()
    if command == 'rotationlogout': return _rotation_logout()
    raise ValueError('Bilinmeyen karakter devri komutu')
