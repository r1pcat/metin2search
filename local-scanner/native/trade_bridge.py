import gc
import app as _app
import uiPrivateShopSearch as _ui
_bridge_response = '{}'
_generation = 0
_original_refresh = None
_refresh_class = None
_window = None
_plan = []
_shop = None
_net = None
_detail_tip = None
_active_filters = None
_detail_capture = None
_catalog_cache = {}
_server_info = None
_market = None
_market_hooks = []
_sale_hooks = []
_sale_events = []
_sale_sequence = 0
_sale_context = None
_purchase = None
_purchase_cached = None
_purchase_hooks = []
_purchase_internal = False
_purchase_quiet = 0
_purchase_used = set()
_purchase_chat_seen = set()

def _purchase_chat_key(line):return (line['pointer'],line['timeBits'],line['kind'],line['text'])

def _purchase_chat_poll():
    for line in _native_chat_tail():
        key=_purchase_chat_key(line)
        if key in _purchase_chat_seen:continue
        _purchase_chat_seen.add(key)
        if line['kind']==1:_sale_record('NativeChat.Info',(line['text'],))

def _purchase_stop():
    global _purchase, _purchase_cached
    for owner,name,original,wrapper in reversed(_purchase_hooks):
        if getattr(owner,name,None) is wrapper:setattr(owner,name,original)
    del _purchase_hooks[:]
    _sale_stop()
    _purchase=None
    _purchase_cached=None

def _purchase_start():
    global _purchase_quiet
    _purchase_stop()
    _sale_start()
    def factory(original):
        def watched(*args,**kwargs):
            if _purchase is not None:
                if _purchase_internal:_purchase['sendCount']+=1
                else:_purchase['interference']=True
            return original(*args,**kwargs)
        return watched
    try:
        for name in ('SendPrivateShopSerchBuyItem','SendShopBuyPacket'):
            original=getattr(_net,name,None)
            if callable(original):
                wrapper=factory(original)
                setattr(_net,name,wrapper)
                _purchase_hooks.append((_net,name,original,wrapper))
        if not any(name=='SendPrivateShopSerchBuyItem' for owner,name,original,wrapper in _purchase_hooks):
            raise ValueError('Purchase API missing')
        _purchase_quiet=float(_app.GetTime())+3
        return {'ready':True,'sendsPurchase':False}
    except Exception:
        _purchase_stop()
        raise

def _purchase_inspect(slot,generation,nonce):
    global _purchase_cached
    if not _purchase_hooks or _purchase is not None or nonce in _purchase_used:raise ValueError('Purchase monitor inactive, request pending or nonce reused')
    snapshot=_snapshot()
    wallet=_purchase_wallet()
    if not 0<=slot<snapshot['count'] or snapshot['generation']!=generation:raise ValueError('Purchase result changed')
    raw=snapshot['raw'][slot*374:(slot+1)*374]
    binary=''.join(chr(int(raw[index:index+2],16)) for index in range(0,len(raw),2))
    def u32(offset):return sum(ord(binary[offset+index])<<(index*8) for index in range(4))
    _purchase_cached={'nonce':nonce,'slot':slot,'generation':generation,'raw':raw,'serverInfo':_server_info,
                      'yang':u32(9),'won':u32(139),
                      'shopVid':u32(1),
                      'seller':_text(binary[46:71].split('\0')[0]),'wallet':wallet}
    result=dict(_purchase_cached)
    result['sendingSupported']=float(_app.GetTime())>=_purchase_quiet
    result['sendsPurchase']=False
    return result

def _purchase_begin(nonce,budget):
    global _purchase, _purchase_internal, _sale_context
    target=_purchase_cached
    if target is None or target['nonce']!=nonce or _purchase is not None:raise ValueError('Purchase nonce invalid or already sent')
    snapshot=_snapshot()
    raw=snapshot['raw'][target['slot']*374:(target['slot']+1)*374]
    wallet=_purchase_wallet()
    if (raw!=target['raw'] or snapshot['generation']!=target['generation'] or _server_info!=target['serverInfo']
        or not wallet.get('walletVerified') or wallet['serverInfo']!=target['serverInfo'] or wallet['won']!=0
        or wallet['yang']>budget or wallet['yang']!=target['wallet'].get('yang')
        or float(_app.GetTime())<_purchase_quiet or target['shopVid']==0 or not target['seller']
        or target['won']==target['yang']==0):raise ValueError('Purchase final preflight failed')
    _purchase=dict(target)
    _purchase_chat_seen.clear()
    _purchase_chat_seen.update(_purchase_chat_key(line) for line in _native_chat_tail())
    _purchase.update({'clientTime':float(_app.GetTime()),'sendCount':0,'interference':False,'events':[],'walletBefore':wallet})
    _sale_context={'slot':target['slot'],'raw':raw,'generation':target['generation'],'serverInfo':_server_info,'clientTime':_purchase['clientTime']}
    _purchase_internal=True
    _purchase_used.add(nonce)
    try:_purchase['nativeReturn']=_sale_safe(_net.SendPrivateShopSerchBuyItem(target['slot']))
    except Exception:
        _purchase['interference']=True
        raise
    finally:_purchase_internal=False
    return {'sent':True,'nonce':nonce,'sendCount':_purchase['sendCount'],'nativeReturn':_purchase.get('nativeReturn'),
            'sendEvidence':'canonical net API invocation; packet transmission not independently observed'}

def _purchase_poll():
    if _purchase is None:return {'code':'UNKNOWN','text':'No pending purchase','requestAndShopVerified':False}
    _purchase_chat_poll()
    elapsed=float(_app.GetTime())-_purchase['clientTime']
    wallet=_purchase_wallet()
    events=_purchase['events']
    bound=(_purchase['sendCount']==1 and not _purchase['interference'] and wallet.get('walletVerified')
           and wallet['serverInfo']==_purchase['serverInfo'] and wallet['won']==0)
    result={'nonce':_purchase['nonce'],'events':list(events),'sendCount':_purchase['sendCount'],
            'wallet':wallet,'requestAndShopVerified':False,'code':'PENDING','text':'Awaiting purchase response',
            'nativeReturn':_purchase.get('nativeReturn'),
            'evidenceSource':'serialized-client-request; server does not echo request ID'}
    if not bound:result.update(code='UNKNOWN',text='Interference or wallet/server verification failed')
    elif wallet['yang']!=_purchase['walletBefore']['yang']:
        spent=_purchase['walletBefore']['yang']-wallet['yang']
        success=_purchase['won']==0 and spent==_purchase['yang'] and spent>0
        result.update(code='SUCCESS' if success else 'UNKNOWN',text='Wallet debit observed',
                      confirmedSuccess=success,suspectedInventoryChange=not success,actualYangSpent=spent,
                      requestAndShopVerified=success)
    else:
        codes=[event['code'] for event in events if event['code']]
        if 'PREMIUM_PREPARING' in codes and len(set(codes))==1 and elapsed>=0.5:
            snapshot=_snapshot()
            same=(snapshot['generation']==_purchase['generation'] and
                  snapshot['raw'][_purchase['slot']*374:(_purchase['slot']+1)*374]==_purchase['raw'])
            result.update(code='PREMIUM_PREPARING' if same else 'UNKNOWN',
                          text=next(event['text'] for event in events if event['code']=='PREMIUM_PREPARING'),
                          requestAndShopVerified=same)
        elif codes and all(code in ('NOT_ENOUGH_MONEY','NOT_ENOUGH_MONEY_EX') for code in codes):
            result.update(code=codes[-1],text=events[-1]['text'],requestAndShopVerified=True)
        elif elapsed>=6:result.update(code='UNKNOWN',text='No unambiguous response before timeout')
    return result

_purchase_quiet_seconds=0.5

def _purchase_timing(interval):
    global _purchase_quiet_seconds
    if _purchase is not None or interval not in (500,1000):raise ValueError('Purchase timing cannot change while pending')
    wallet=_purchase_wallet()
    if interval==500 and (not wallet.get('walletVerified') or wallet['yang']!=0 or wallet['won']!=0):
        raise ValueError('Fast purchase timing requires verified zero wallet')
    _purchase_quiet_seconds=0.25 if interval==500 else 0.5
    return {'intervalMs':interval,'quietMs':int(_purchase_quiet_seconds*1000)}

def _purchase_next(nonce):
    global _purchase, _purchase_cached, _sale_context, _purchase_quiet
    if _purchase is None or _purchase['nonce']!=nonce:raise ValueError('Purchase completion nonce mismatch')
    result=_purchase_poll()
    if result['code'] not in ('PREMIUM_PREPARING','NOT_ENOUGH_MONEY','NOT_ENOUGH_MONEY_EX') or not result['requestAndShopVerified']:
        raise ValueError('Unresolved purchase cannot advance')
    _purchase=None
    _purchase_cached=None
    _sale_context=None
    _purchase_quiet=float(_app.GetTime())+_purchase_quiet_seconds
    return {'completed':True}

def _sale_safe(value,depth=0):
    if depth>3:return 'depth-limit'
    if value is None or isinstance(value,(bool,int,long,float)):return value
    if isinstance(value,(str,unicode)):return _text(value[:1024])
    if isinstance(value,(list,tuple)):return [_sale_safe(v,depth+1) for v in value[:8]]
    return type(value).__name__

def _purchase_wallet():
    import sys
    primary=list(_ui.__dict__.values())
    if _net is not None:primary.append(_net)
    def unique(values,predicate):
        found={}
        for value in values:
            if predicate(value):found[id(value)]=value
        return found.values()
    def player_api(value):
        return all(callable(getattr(value,name,None)) for name in ('GetStatus','GetElk','GetCheque'))
    def net_api(value):
        return all(callable(getattr(value,name,None)) for name in ('GetServerInfo','SendPrivateShopSearchInfoSub'))
    players=unique(primary,player_api)
    networks=unique(primary,net_api)
    if not players:players=unique(list(sys.modules.values()),player_api)
    if not networks:networks=unique(list(sys.modules.values()),net_api)
    if len(players)!=1 or len(networks)!=1:
        return {'walletVerified':False,'reason':'Wallet module API missing or ambiguous','playerCandidates':len(players),'networkCandidates':len(networks),'readOnly':True,'sendsPurchase':False}
    player=players[0]
    network=networks[0]
    server=_text(network.GetServerInfo()).strip()
    if not server:raise ValueError('Wallet server identity missing')
    yang=player.GetElk()
    won=player.GetCheque()
    if any(isinstance(v,bool) or not isinstance(v,(int,long)) or v<0 for v in (yang,won)):
        return {'walletVerified':False,'reason':'Invalid wallet value','serverInfo':server}
    if _text(network.GetServerInfo()).strip()!=server:raise ValueError('Wallet server changed')
    return {'walletVerified':True,'yang':yang,'won':won,'serverInfo':server,'readOnly':True,'sendsPurchase':False}

def _sale_preparing_candidate(text):
    folded=text.lower()
    for source,target in ((u'\u0131',u'i'),(u'\u015f',u's'),(u'\u00fc',u'u'),(u'\u00f6',u'o'),(u'\u00e7',u'c'),(u'\u011f',u'g')):
        folded=folded.replace(source,target)
    return (u'premium' in folded and (u'pazar' in folded or u'dukkan' in folded)
            and (u'hazirlaniyor' in folded or u'duzenleniyor' in folded))

def _sale_record(source,args):
    global _sale_sequence
    now=float(_app.GetTime())
    if _sale_context is None or now-_sale_context['clientTime']<0 or now-_sale_context['clientTime']>5:return
    if _server_info is None or _text(_net.GetServerInfo()).strip()!=_server_info:return
    _sale_sequence+=1
    values=_sale_safe(args)
    text=' '.join(v for v in values if isinstance(v,unicode))
    candidate=_sale_preparing_candidate(text)
    if _purchase is not None:
        folded=text.lower()
        for source_char,target_char in ((u'\u0131',u'i'),(u'\u015f',u's'),(u'\u00fc',u'u'),(u'\u00f6',u'o'),(u'\u00e7',u'c'),(u'\u011f',u'g')):
            folded=folded.replace(source_char,target_char)
        code=None
        if folded.strip()==u'[kisisel premium dukkan] dukkan su anda hazirlaniyor.':code='PREMIUM_PREPARING'
        elif text.strip() in ('NOT_ENOUGH_MONEY','NOT_ENOUGH_MONEY_EX'):code=text.strip()
        elif source=='GameWindow.OnShopError':code='OTHER_SHOP_ERROR'
        _purchase['events'].append({'source':source,'text':text,'code':code,'clientTime':now})
        del _purchase['events'][:-32]
    _sale_events.append({'sequence':_sale_sequence,'source':source,'clientTime':now,'arguments':values,
                         'preparingCandidate':candidate,'correlation':'time-only','context':dict(_sale_context)})
    del _sale_events[:-32]

def _sale_stop():
    global _sale_context
    for owner,name,original,wrapper,owned in reversed(_sale_hooks):
        current=getattr(owner,name)
        if getattr(current,'im_func',current) is wrapper:
            if owned:setattr(owner,name,original)
            else:delattr(owner,name)
    del _sale_hooks[:]
    _sale_context=None

def _sale_start():
    global _sale_context
    _sale_stop()
    del _sale_events[:]
    game=_native_game_window()
    def wrap(owner,name,factory,required=False):
        original=getattr(owner,name,None)
        if not callable(original):
            if required:raise ValueError('Satis gozlem API eksik: '+name)
            return
        replacement=factory(original)
        owned=name in getattr(owner,'__dict__',{})
        setattr(owner,name,replacement)
        _sale_hooks.append((owner,name,original,replacement,owned))
    def accept_factory(original):
        def accepted(self,*args,**kwargs):
            global _sale_context
            if _purchase is not None:_purchase['interference']=True
            _sale_context=None
            try:
                slot=int(getattr(self,'selectitemRealindex',-1))
                snapshot=_native_snapshot()
                if 0<=slot<snapshot['count']:
                    _sale_context={'slot':slot,'raw':snapshot['raw'][slot*374:(slot+1)*374],
                                   'generation':_generation,'serverInfo':_server_info,'clientTime':float(_app.GetTime())}
            except Exception:pass
            return original(self,*args,**kwargs)
        return accepted
    def event_factory(name):
        def factory(original):
            def observed(self,*args,**kwargs):
                try:_sale_record(name,args)
                except Exception:pass
                return original(self,*args,**kwargs)
            return observed
        return factory
    try:
        wrap(_window.__class__,'OnBuyAcceptEvent',accept_factory,True)
        wrap(game.__class__,'OnShopError',event_factory('GameWindow.OnShopError'))
        wrap(game.__class__,'AppendChat',event_factory('GameWindow.AppendChat'))
        try:
            import uiCommon
        except ImportError:uiCommon=None
        if uiCommon is not None:
            for type_name in ('PopupDialog','ExPopupDialog'):
                popup=getattr(uiCommon,type_name,None)
                if popup is not None:wrap(popup,'SetText',event_factory('uiCommon.'+type_name+'.SetText'))
        chat=getattr(_ui,'chat',None)
        if chat is not None:
            def chat_factory(original):
                def appended(*args,**kwargs):
                    try:_sale_record('chat.AppendChat',args)
                    except Exception:pass
                    return original(*args,**kwargs)
                return appended
            wrap(chat,'AppendChat',chat_factory)
        return {'observing':True,'hooks':[name for owner,name,original,wrapper,owned in _sale_hooks],
                'sendsPurchase':False,'remoteSaleStateVerified':False}
    except Exception:
        _sale_stop()
        raise
def _channel():
    _native_snapshot()
    if _server_info is not None and _text(_net.GetServerInfo()).strip()!=_server_info:
        raise ValueError('Sunucu degisti; yeni sunucu icin yeniden Baslat')
def _json(value):
    if value is None: return 'null'
    if value is True: return 'true'
    if value is False: return 'false'
    if isinstance(value,(int,long)): return str(value)
    if isinstance(value,float):
        if value!=value or value in (float('inf'),float('-inf')): raise ValueError('Gecersiz sayi')
        return repr(value)
    if isinstance(value,(list,tuple)): return '['+','.join(_json(x) for x in value)+']'
    if isinstance(value,dict): return '{'+','.join(_json(unicode(k))+':'+_json(v) for k,v in value.items())+'}'
    if isinstance(value,str): value=_native_text(value)
    if isinstance(value,unicode):
        parts=[]
        for c in value:
            n=ord(c)
            if c=='"': parts.append('\\"')
            elif c=='\\': parts.append('\\\\')
            elif 32<=n<127: parts.append(chr(n))
            elif n<=65535: parts.append('\\u%04x'%n)
            else:
                n-=65536
                parts.append('\\u%04x\\u%04x'%(0xd800+(n>>10),0xdc00+(n&1023)))
        return '"'+''.join(parts)+'"'
    raise ValueError('JSON icin desteklenmeyen veri tipi')
def _refresh(self, *args, **kwargs):
    global _generation
    result = _original_refresh(self, *args, **kwargs)
    _generation += 1
    return result

def _optional_constant(name):
    for value in (_window,_shop,_ui)+tuple(_ui.__dict__.values()):
        if hasattr(value,name): return getattr(value,name)
    return None

def _all_label(value):
    if isinstance(value,str): value=_native_text(value)
    if not isinstance(value,unicode): return False
    names=[u'hepsi',u't\u00fcm\u00fc',u'all']
    localized=_optional_constant('CATEGORY_ETC_ALL')
    if isinstance(localized,str): localized=_native_text(localized)
    if isinstance(localized,unicode): names.append(localized.strip().lower())
    return value.strip().lower() in names

def _build_plan(jobs,types,subs):
    plan=[]
    all_job=_optional_constant('JOB_MAX_COUNT')
    none_type=_optional_constant('MASK_ITEM_TYPE_NONE')
    common_added=set()
    for job in sorted(jobs):
        if job==all_job or _all_label(jobs[job]): continue
        def priority(category):
            label=types[category]
            if isinstance(label,str): label=_native_text(label)
            label=label.lower()
            return (0 if 'silah' in label or 'weapon' in label else 1 if any(word in label for word in (u'z\u0131rh','zirh','ekipman','armor','equipment')) else 2,category)
        for category in sorted(types,key=priority):
            if category==none_type or _all_label(types[category]): continue
            submap=subs.get(category)
            if not isinstance(submap,dict) or not submap:
                raise ValueError('Alt kategori sozlugu bulunamadi: %s' % category)
            nested=all(isinstance(value,dict) for value in submap.values())
            selected_job=job
            common=False
            if priority(category)[0]==2 and not nested and all_job in jobs and _all_label(jobs[all_job]):
                if category in common_added:continue
                common_added.add(category)
                selected_job=all_job
                common=True
            if nested:
                if job not in submap: continue
                submap=submap[job]
            if not isinstance(submap,dict) or not submap: continue
            if any(isinstance(value,dict) for value in submap.values()):
                raise ValueError('Taninmayan ic kategori yapisi: job=%s category=%s' % (job,category))
            concrete=[sub for sub in sorted(submap) if not _all_label(submap[sub])]
            selected=concrete if concrete else sorted(submap)[:1]
            for sub in selected:
                if not all(isinstance(x,(int,long)) and 0<=x<256 for x in (selected_job,category,sub)):
                    raise ValueError('Kategori kimlikleri gecersiz')
                sublabel=submap[sub] if concrete else 'Alt kategori yok'
                label='%s / %s / %s' % ('Ortak esyalar' if common else jobs[job],types[category],sublabel)
                if isinstance(label,str): label=_native_text(label)
                plan.append({'job':selected_job,'category':category,'sub':sub,'label':label})
    def stage(node):
        return priority(node['category'])[0]
    return sorted(plan,key=lambda node:(stage(node),node['job'],node['category'],node['sub']))
def _discover():
    global _window, _shop, _net, _plan, _original_refresh, _refresh_class, _server_info
    _channel()
    for value in _ui.__dict__.values():
        if hasattr(value,'GetPrivateShopSearchResultCount'): _shop=value
        if hasattr(value,'SendPrivateShopSearchInfoSub'): _net=value
    if _shop is None or _net is None: raise ValueError('Ticaret Cami Python modulleri bulunamadi')
    _server_info=_text(_net.GetServerInfo()).strip()
    if not _server_info:raise ValueError('Sunucu adi okunamadi; kayitlar karistirilmamasi icin tarama baslatilmadi')
    direct_error=None
    try:
        game=_native_game_window()
        interface=game.interface
        candidate=interface.wndPrivateShopSearch
        if candidate is None: raise AttributeError('wndPrivateShopSearch henuz olusturulmamis')
        _window=candidate
        discovery='GameWindow.interface.wndPrivateShopSearch'
    except (ReferenceError,AttributeError,TypeError,ValueError,RuntimeError) as error:
        direct_error=str(error)
        candidates={}
        for value in gc.get_objects():
            try:
                if isinstance(value,_ui.PrivateShopSeachWindow) and value.IsShow(): candidates[id(value)]=value
            except (ReferenceError,AttributeError,TypeError): pass
        if len(candidates)!=1:
            raise ValueError('Ticaret Cami referansi bulunamadi: %s; gc acik pencere=%d' % (direct_error,len(candidates)))
        _window=candidates.values()[0]
        discovery='gc fallback'
    for name in ('IsShow','Search','RefreshList','OnChangeJobSlot','OnChangeItemTypeSlot','OnChangeItemSubTypeSlot'):
        if not callable(getattr(_window,name,None)): raise ValueError('Pencere metodu eksik: '+name)
    if not _window.IsShow(): raise ValueError('Ticaret Cami penceresi kapali; oyunda acip tekrar Baslat')
    types = _window.ITEM_MASK_TYPE_DIC
    subs = _window.ITEM_MASK_MASK_SUBTYPE_DICS
    jobs = _window.JOB_NAME_DIC
    if not isinstance(types,dict) or not isinstance(subs,dict) or not isinstance(jobs,dict):
        raise ValueError('Kategori sozluklerinin yapisi eslesmedi')
    _plan=_build_plan(jobs,types,subs)
    if not _plan: raise ValueError('Kategori plani bos')
    if _original_refresh is None:
        _refresh_class=_window.__class__
        _original_refresh=_refresh_class.RefreshList
        _refresh_class.RefreshList=_refresh
    try: sort=_sort_info()
    except Exception as error: sort={'error':_text(error)}
    global _login_target
    try:
        _login_target=_compute_login_target(_server_info)
        if not _login_target.get('ok'):
            login_info=dict(_login_target)
            _login_target=None
        else:login_info=dict(_login_target)
    except Exception as error:
        _login_target=None
        login_info={'ok':False,'reason':str(error)}
    all_job=_optional_constant('JOB_MAX_COUNT')
    all_job=all_job if all_job in jobs and _all_label(jobs[all_job]) else None
    return {'plan':_plan,'generation':_generation,'discovery':discovery,'bridgeVersion':'0.7.0','captureDetails':True,'allCharacterJob':all_job,'purchaseTiming500':True,'fastSearch':callable(getattr(_net,'SendPrivateShopSearchInfo',None)),'sort':sort,'serverInfo':_server_info,'loginTarget':login_info,
            'filters':dict((key,int(getattr(_window,field))) for key,field in
                           (('job','savejobselectnumber'),('category','saveitemMasktypeselectnumber'),('sub','saveitemMasksubtypeselectnumber'))),
            'categoryTree':{'jobs':jobs,'types':types,'subtypes':subs},'fields':sorted(_window.__dict__.keys())}
_SORT_ATTR='_PrivateShopSeachWindow__save_sort_select_type'
_SORT_DIC='_PrivateShopSeachWindow__SORT_NAME_DIC'
_sort_target=None
_fast_search_verified=False
_fast_search_disabled=False
def _sort_info():
    info={'method':callable(getattr(_window,'OnChangeSortSlot',None)),'selected':None,'names':{},'recentIndex':None,'unitPriceIndex':None}
    value=getattr(_window,_SORT_ATTR,None)
    if isinstance(value,(int,long)) and not isinstance(value,bool): info['selected']=int(value)
    names=getattr(_window,_SORT_DIC,None)
    if isinstance(names,dict):
        for key,name in names.items():
            info['names'][unicode(key)]=_text(name)
        wanted=set([u'En yeniler'])
        recent=_optional_constant('PRIVATE_SHOP_SEARCH_SORT_RECENT')
        if recent is not None: wanted.add(_text(recent))
        matches=[key for key,name in names.items() if isinstance(key,(int,long)) and _text(name) in wanted]
        if len(matches)==1: info['recentIndex']=int(matches[0])
        unit_names=set([u'En d\u00fc\u015f\u00fck bireysel fiyat',u'En dusuk bireysel fiyat'])
        matches=[key for key,name in names.items() if isinstance(key,(int,long)) and not isinstance(key,bool) and _text(name) in unit_names]
        if len(matches)==1: info['unitPriceIndex']=int(matches[0])
    return info
def _apply_sort():
    if _sort_target is None: return
    _window.OnChangeSortSlot(_sort_target)
    actual=getattr(_window,_SORT_ATTR,None)
    if actual!=_sort_target: raise ValueError('Siralama istemciyle eslesmedi: %s != %s' % (actual,_sort_target))
def _set_sort(index):
    global _sort_target
    info=_sort_info()
    if not info['method']: raise ValueError('Siralama yontemi bulunamadi')
    if unicode(index) not in info['names']: raise ValueError('Bilinmeyen siralama: %s' % index)
    _sort_target=int(index)
    _apply_sort()
    return {'sort':_sort_info()}
def _snapshot():
    _channel()
    if _server_info!=_text(_net.GetServerInfo()).strip(): raise ValueError('Sunucu degisti; tarama durdu')
    if _sort_target is not None and getattr(_window,_SORT_ATTR,None)!=_sort_target: raise ValueError('Tarama sirasinda siralama degisti; eski cevap kaydedilmedi')
    result=_native_snapshot()
    if _active_filters is not None:
        actual=tuple(int(getattr(_window,name)) for name in ('savejobselectnumber','saveitemMasktypeselectnumber','saveitemMasksubtypeselectnumber'))
        if actual!=_active_filters: raise ValueError('Tarama sirasinda arama filtresi degisti; eski cevap kaydedilmedi')
    result['generation']=_generation
    return result

def _text(value):
    return _native_text(value) if isinstance(value,str) else unicode(value)

def _details(result):
    global _detail_tip, _detail_capture
    import uiToolTip
    if _detail_tip is None:
        _detail_tip=uiToolTip.ItemToolTip()
        _detail_tip.ShowToolTip=lambda *args:None
        _detail_tip.Show=lambda *args:None
        def safe(value,depth=0):
            if depth>8:return 'depth-limit'
            if value is None or isinstance(value,(int,long,float,bool,unicode)):return value
            if isinstance(value,str):return _text(value)
            if isinstance(value,(list,tuple)):return [safe(v,depth+1) for v in value]
            return type(value).__name__
        def capture(name,original):
            def wrapped(*args,**kwargs):
                if _detail_capture is not None:
                    _detail_capture['rendererCalls'].setdefault(name,[]).append(safe(args))
                    if name=='AppendTextLine' and args and isinstance(args[0],(str,unicode)):
                        _detail_capture['lines'].append(_text(args[0]))
                if name=='_ItemToolTip__AppendAcceItemAffectInformation' and _detail_capture is not None:
                    previous=item_module.SelectItem
                    def selected(vnum,*extra):
                        result=previous(vnum,*extra)
                        _detail_capture.setdefault('absorption',{}).setdefault('selectedVnums',[]).append(vnum)
                        absorption=_detail_capture['absorption']
                        if vnum!=_detail_capture['vnum'] and 'sourceVnum' not in absorption and item_module.GetItemType() in (getattr(item_module,'ITEM_TYPE_WEAPON',-1),getattr(item_module,'ITEM_TYPE_ARMOR',-1)):
                            absorption['sourceVnum']=vnum
                            absorption['sourceName']=_text(item_module.GetItemName())
                            absorption['source']='client-absorption-renderer-SelectItem'
                        return result
                    item_module.SelectItem=selected
                    try:return original(*args,**kwargs)
                    finally:item_module.SelectItem=previous
                return original(*args,**kwargs)
            return wrapped
        for name in ('AppendTextLine','AppendTextLineAcce','_ItemToolTip__AppendAcceItemAffectInformation',
                     '_ItemToolTip__AppendRefineElementInformation','_ItemToolTip__AppendRefineElementInformationAcce',
                     '_ItemToolTip__AppendAccessoryMetinSlotInfo'):
            original=getattr(_detail_tip,name,None)
            if callable(original):setattr(_detail_tip,name,capture(name,original))
    item_module=None
    for value in uiToolTip.__dict__.values():
        if hasattr(value,'SelectItem') and hasattr(value,'GetItemType'): item_module=value
    if item_module is None: raise ValueError('Item tanim modulu bulunamadi')
    raw=[int(result['raw'][i:i+2],16) for i in range(0,len(result['raw']),2)]
    details=[]
    def number(row,offset): return sum(row[offset+i]<<(8*i) for i in range(4))
    for slot in range(result['count']):
        row=raw[slot*187:(slot+1)*187]
        data={'slot':slot,'vnum':number(row,0x11),'source':'client-SetPrivateSearchItem','status':'partial','lines':[], 'stones':[], 'bonuses':[], 'warnings':[], 'rendererCalls':{}}
        _detail_capture=data
        try:
            _detail_tip.SetPrivateSearchItem(slot)
            _detail_tip.HideToolTip()
            lines=[];seen=set();pending=list(getattr(_detail_tip,'childrenList',[]))
            while pending and len(seen)<512:
                widget=pending.pop(0)
                if id(widget) in seen: continue
                seen.add(id(widget))
                getter=getattr(widget,'GetText',None)
                if callable(getter):
                    text=getter()
                    if text: lines.append(_text(text))
                children=getattr(widget,'childrenList',None)
                if isinstance(children,(list,tuple)): pending.extend(children)
                for value in getattr(widget,'__dict__',{}).values():
                    if callable(getattr(value,'GetText',None)): pending.append(value)
                    elif isinstance(value,(list,tuple)):
                        pending.extend(v for v in value if callable(getattr(v,'GetText',None)))
            for line in lines:
                if line not in data['lines']:data['lines'].append(line)
            vnum=number(row,0x11);item_module.SelectItem(vnum)
            definition=_catalog_cache.get(vnum)
            if definition is None:
                definition={'itemType':item_module.GetItemType(),'itemSubType':item_module.GetItemSubType()}
                for name,method in (('baseValues','GetValue'),('baseAffects','GetAffect'),('limits','GetLimit')):
                    getter=getattr(item_module,method,None)
                    if callable(getter):
                        values=[]
                        for index in range(6 if name=='baseValues' else 3 if name=='baseAffects' else 2):
                            try: values.append(getter(index))
                            except (ValueError,RuntimeError,TypeError): break
                        definition[name]=values
                if len(_catalog_cache)>=4096:_catalog_cache.clear()
                _catalog_cache[vnum]=definition
            data.update(definition)
            formatter=getattr(_detail_tip,'_ItemToolTip__GetAffectString',None)
            refine=getattr(_shop,'GetPrivateShopItemRefineElement',None)
            converter=getattr(_detail_tip,'GetRefineElement',None)
            if callable(refine) and callable(converter):
                try:
                    converted=converter(refine(slot))
                    element={'source':'client-GetRefineElement'}
                    for key,constant in (('grade','REFINE_ELEMENT_INDEX_GRADE'),('applyType','REFINE_ELEMENT_INDEX_APPLY_TYPE'),('value','REFINE_ELEMENT_INDEX_VALUE'),('bonus','REFINE_ELEMENT_INDEX_BONUS')):
                        index=getattr(_detail_tip,constant,None)
                        if isinstance(index,(int,long)) and 0<=index<len(converted):element[key]=converted[index]
                    if callable(formatter) and isinstance(element.get('applyType'),(int,long)) and isinstance(element.get('value'),(int,long)):
                        text=formatter(element['applyType'],element['value'])
                        if text:element['text']=_text(text)
                    data['element']=element
                except Exception as error:data['warnings'].append('Element: '+_text(str(error)))
            if callable(formatter):
                for index in range(7):
                    offset=0x63+index*5;kind=row[offset]+(row[offset+1]<<8);value=row[offset+2]+(row[offset+3]<<8)
                    if value>=32768:value-=65536
                    if kind:
                        try:
                            label=formatter(kind,value)
                            if label:data['bonuses'].append({'slot':index,'type':kind,'value':value,'text':_text(label)})
                        except Exception as error:data['warnings'].append('Bonus: '+_text(str(error)))
            is_equipment=data['itemType']==getattr(item_module,'ITEM_TYPE_WEAPON',-1)
            if data['itemType']==getattr(item_module,'ITEM_TYPE_ARMOR',-1):
                is_equipment=data['itemSubType'] in tuple(getattr(item_module,n,-999) for n in ('ARMOR_BODY','ARMOR_FOOTS','ARMOR_GLOVE'))
            if is_equipment:
                for index in range(7):
                    socket=number(row,0x47+index*4)
                    if not socket:continue
                    stone={'slot':index,'raw':socket}
                    stone['socketType']=_detail_tip.GetMetinSocketType(socket)
                    stone['vnum']=_detail_tip.GetMetinItemIndex(socket)
                    if stone['vnum']:
                        item_module.SelectItem(stone['vnum']);stone['name']=_text(item_module.GetItemName())
                    data['stones'].append(stone)
            data['status']='client-rendered' if data['lines'] else 'partial'
        except Exception as error:data['warnings'].append(_text(str(error)))
        try:item_module.SelectItem(data['vnum'])
        except Exception as error:data['warnings'].append('SelectItem restore: '+_text(str(error)))
        _detail_capture=None
        details.append(data)
    return details
def _constant(name):
    value=_optional_constant(name)
    if value is not None: return value
    raise ValueError('Istemci sabiti bulunamadi: '+name)

def _market_end():
    global _market
    try:
        if _market is not None and _market.get('status')=='Open':
            state=_native_snapshot()
            if state['shopOpen'] and state['shopPrivate']:
                _net.SendShopEndPacket()
                _native_game_window().EndShop()
    finally:
        for cls,name,original,wrapper in _market_hooks:
            current=getattr(cls,name)
            if getattr(current,'im_func',current) is wrapper:setattr(cls,name,original)
        del _market_hooks[:]
        _market=None

def _market_begin(vid,sellerhex):
    global _market
    _market_end()
    state=_native_snapshot()
    if state['shopOpen']:return {'status':'Unknown','reason':'An existing shop is open; no interaction sent'}
    import chr as character
    seller=_text(''.join(chr(int(sellerhex[i:i+2],16)) for i in range(0,len(sellerhex),2)))
    if not character.HasInstance(vid):return {'status':'Unknown','reason':'Seller is not loaded in the current map'}
    if _text(character.GetNameByVID(vid))!=seller:return {'status':'Unknown','reason':'Seller VID/name mismatch'}
    game=_native_game_window()
    for name in ('StartShop','EndShop'):
        if not callable(getattr(game,name,None)):raise ValueError('Pazar dogrulama API eksik: '+name)
    _market={'status':'Pending','vid':vid,'seller':seller,'reason':'Awaiting matching server shop-open callback'}
    cls=game.__class__
    original=cls.StartShop
    def opened(self,target,*args,**kwargs):
        result=original(self,target,*args,**kwargs)
        if _market is not None:
            try:
                current=_native_snapshot()
                if int(target)==_market['vid'] and current['shopOpen'] and current['shopPrivate'] and _text(character.GetNameByVID(target))==_market['seller']:
                    _market['status']='Open'
                    _market['reason']='Matching server StartShop callback and private-shop flags'
                else:
                    _market['status']='Unknown'
                    _market['reason']='A different shop opened; no close command will be sent'
            except Exception as error:
                _market['status']='Unknown'
                _market['reason']='Callback verification failed: '+str(error)
        return result
    cls.StartShop=opened
    _market_hooks.append((cls,'StartShop',original,opened))
    try:_net.SendOnClickPacket(vid)
    except Exception:
        _market_end()
        raise
    return dict(_market)

_GLASS_VNUM = 60005
_stream = None
_login_target = None
_PHASE_NAMES = {'LoginWindow': 'login', 'SelectCharacterWindow': 'select', 'LoadingWindow': 'loading', 'GameWindow': 'game'}

def _module_with(*names):
    import sys
    found = {}
    for value in list(_ui.__dict__.values()) + list(sys.modules.values()):
        try:
            if value is not None and all(callable(getattr(value, n, None)) for n in names):
                found[id(value)] = value
        except Exception:
            pass
    values = found.values()
    return values[0] if len(values) == 1 else None

def _main_stream():
    global _stream
    try:
        if _stream is not None and hasattr(_stream, 'curPhaseWindow'):
            return _stream
    except Exception:
        pass
    _stream = None
    try:
        import networkModule as _nm
        cls = getattr(_nm, 'MainStream', None)
    except Exception:
        cls = None
    if cls is None:
        return None
    for value in gc.get_objects():
        try:
            if isinstance(value, cls):
                _stream = value
                break
        except Exception:
            pass
    return _stream

def _phase():
    stream = _main_stream()
    if stream is None:
        return 'unknown', None
    window = getattr(stream, 'curPhaseWindow', None)
    if window is None:
        return 'none', None
    name = getattr(window, '__class__', type(window)).__name__
    return _PHASE_NAMES.get(name, 'other'), window

def _norm_name(value):
    text = _text(value).strip().lower()
    while u'|c' in text:
        at = text.find(u'|c')
        color = text[at + 2:at + 10]
        if len(color) != 8 or any(c not in u'0123456789abcdef' for c in color):
            break
        text = text[:at] + text[at + 10:]
    return u' '.join(text.replace(u'|r', u'').split())

def _login_server_name(value):
    text = _norm_name(value)
    if text.startswith(u'[') and u']' in text:
        return text[text.find(u']') + 1:].strip(), text[:text.find(u']') + 1]
    return text, u''

def _login_channel_name(value):
    text = _norm_name(value).replace(u' ', u'')
    for prefix in (u'channel', u'kanal', u'ch'):
        if text.startswith(prefix) and text[len(prefix):].isdigit():
            return int(text[len(prefix):])
    return text

def _compute_login_target(server_info):
    try:
        import serverInfo as _si
        regions = _si.REGION_DICT
    except Exception as error:
        return {'ok': False, 'reason': 'serverInfo tablosu okunamadi: ' + str(error)}
    text = _text(server_info)
    cut = text.rfind(',')
    if cut < 0:
        return {'ok': False, 'reason': 'Sunucu bilgisi ayristirilamadi'}
    server_name = _norm_name(text[:cut]); channel_name = _login_channel_name(text[cut + 1:])
    base, tag = _login_server_name(server_name)
    matches = []
    for region_id, region in regions.items():
        for sid, data in region.items():
            name = _norm_name(data.get('name', ''))
            other_base, other_tag = _login_server_name(name)
            exact = name == server_name
            if not exact and (base != other_base or (tag and other_tag)):
                continue
            for cid, channel in data.get('channel', {}).items():
                if _login_channel_name(channel.get('name', '')) == channel_name:
                    matches.append((2 if exact else 1, {'ok': True, 'region': int(region_id), 'server': int(sid), 'channel': int(cid), 'serverName': _text(data.get('name', '')), 'channelName': _text(channel.get('name', ''))}))
    if matches:
        best = max(score for score, target in matches)
        targets = [target for score, target in matches if score == best]
        if len(targets) == 1:
            return targets[0]
        return {'ok': False, 'reason': 'Ayni ada uyan birden fazla sunucu/kanal var; giris gonderilmedi'}
    return {'ok': False, 'reason': 'Sunucu/kanal serverInfo tablosunda bulunamadi'}

def _gamestate():
    phase, window = _phase()
    state = {'phase': phase, 'playerReady': False, 'inventoryReady': False, 'glassOpen': False, 'glassSlot': -1,
             'serverInfo': '', 'channel': -1, 'loginTarget': _login_target is not None}
    try:
        world = _native_world()
        state['channel'] = world['channel']
        if phase == 'game' and world['player']:
            state['playerReady'] = True
            state['inventoryReady'] = 0 < world['capacity'] <= 200
    except Exception:
        pass
    try:
        network = _module_with('GetServerInfo')
        if network is not None:
            state['serverInfo'] = _text(network.GetServerInfo()).strip()
    except Exception:
        pass
    if phase == 'game':
        try:
            identity = _rotation_roster()
            state['characterSlot'] = identity[2]
            state['character'] = identity[0]
        except Exception:
            state['characterSlot'] = -1
        try:
            state['glassOpen'] = bool(_native_game_window().interface.wndPrivateShopSearch.IsShow())
        except Exception:
            pass
        if state['inventoryReady']:
            try:
                state['glassSlot'] = int(_native_find_item(_GLASS_VNUM))
            except Exception:
                pass
    return state

def _login_prepare(hexed):
    global _login_target
    data = [int(hexed[i:i + 2], 16) for i in range(0, len(hexed), 2)]
    chars = []
    i = 0
    while i < len(data):
        b = data[i]
        if b < 0x80:
            chars.append(unichr(b)); i += 1
        elif 0xC0 <= b < 0xE0 and i + 1 < len(data):
            chars.append(unichr(((b & 0x1F) << 6) | (data[i + 1] & 0x3F))); i += 2
        elif 0xE0 <= b < 0xF0 and i + 2 < len(data):
            chars.append(unichr(((b & 0x0F) << 12) | ((data[i + 1] & 0x3F) << 6) | (data[i + 2] & 0x3F))); i += 3
        else:
            raise ValueError('Gecersiz sunucu metni')
    text = u''.join(chars)
    target = _compute_login_target(text)
    _login_target = target if target.get('ok') else None
    return target

def _login_list(window, which):
    name = 'serverList' if which == 'server' else 'channelList'
    box = getattr(window, name, None)
    if box is not None:
        return box
    wanted = 'ServerListBox' if which == 'server' else 'ChannelListBox'
    for value in gc.get_objects():
        try:
            if getattr(value, '__class__', type(value)).__name__ == wanted:
                return value
        except Exception:
            pass
    raise ValueError('Giris listesi bulunamadi: ' + which)

def _login_pick(which):
    if _login_target is None or not _login_target.get('ok'):
        raise ValueError('Giris hedefi bilinmiyor; sunucu/kanal eslesmedi')
    phase, window = _phase()
    if phase != 'login':
        raise ValueError('Giris ekrani degil: ' + phase)
    region_getter = getattr(window, '_LoginWindow__GetRegionID', None)
    region = int(region_getter()) if callable(region_getter) else 0
    if region != _login_target.get('region', 0):
        raise ValueError('Giris bolgesi hedef sunucuyla eslesmedi')
    box = _login_list(window, which)
    wanted = _login_target[which]
    getter = getattr(window, '_LoginWindow__GetServerID' if which == 'server' else '_LoginWindow__GetChannelID', None)
    if not callable(getter):
        raise ValueError('LoginWindow okuma metodu yok')
    items = getattr(box, 'itemList', None)
    count = len(items) if items is not None else 16
    rows = range(count)
    guess = wanted - 1 if which == 'channel' else None
    if guess is not None and 0 <= guess < count:
        rows = [guess] + [r for r in rows if r != guess]
    for row in rows:
        box.SelectItem(row)
        if int(getter()) == wanted:
            return {'selected': which, 'row': row, 'id': wanted}
    raise ValueError('Listede hedef satir dogrulanamadi: ' + which)

def _login_connect():
    if _login_target is None or not _login_target.get('ok'):
        raise ValueError('Giris hedefi bilinmiyor')
    phase, window = _phase()
    if phase != 'login':
        raise ValueError('Giris ekrani degil: ' + phase)
    region_getter = getattr(window, '_LoginWindow__GetRegionID', None)
    region = int(region_getter()) if callable(region_getter) else 0
    if region != _login_target.get('region', 0):
        raise ValueError('Baglanti oncesi giris bolgesi degisti')
    sid = int(window._LoginWindow__GetServerID()); cid = int(window._LoginWindow__GetChannelID())
    if sid != _login_target['server'] or cid != _login_target['channel']:
        raise ValueError('Sunucu/kanal secimi dogrulanamadi: %s/%s' % (sid, cid))
    window._LoginWindow__OnClickSelectServerButton()
    return {'clicked': True, 'server': sid, 'channel': cid}

def _login_enter(slot):
    slot = int(slot)
    if not 0 <= slot < 5:
        raise ValueError('Karakter slotu gecersiz')
    phase, window = _phase()
    if phase != 'select':
        raise ValueError('Karakter secim ekrani degil: ' + phase)
    network = _module_with('DirectEnter')
    if network is None:
        raise ValueError('DirectEnter bulunamadi')
    network.DirectEnter(slot % 5, slot // 5)
    return {'entered': True, 'slot': slot}

def _use_glass():
    state = _gamestate()
    if state['phase'] != 'game' or not state['playerReady'] or not state['inventoryReady']:
        raise ValueError('Oyun hazir degil: ' + state['phase'])
    if state['glassOpen']:
        return {'alreadyOpen': True}
    slot = state['glassSlot']
    if slot < 0:
        raise ValueError('Envanterde Ticaret Cami yok')
    if not _native_use_item(slot):
        raise ValueError('Esya kullanma istegi gonderilemedi')
    return {'used': True, 'slot': slot}

def _dispatch(command, *args):
    global _original_refresh, _active_filters, _detail_tip, _server_info, _sort_target
    global _fast_search_verified, _fast_search_disabled
    if command in ('characterlist','rotationstatus','rotationprepare','navinfo','navgrid','navwalk','navstop','storageopen','storagepassword','storagedeposit','storagewithdraw','storageclose','rotationlogout'):
        return _rotation_dispatch(command, args)
    if command=='wallet': return _purchase_wallet()
    if command=='gamestate': return _gamestate()
    if command=='loginprepare': return _login_prepare(args[0])
    if command=='loginserver': return _login_pick('server')
    if command=='loginchannel': return _login_pick('channel')
    if command=='loginconnect': return _login_connect()
    if command=='loginenter': return _login_enter(*args)
    if command=='useglass': return _use_glass()
    if command=='chattail':return {'readOnly':True,'sendsPurchase':False,'lines':_native_chat_tail()}
    if command=='inspect':
        return _discover()
    if command=='snapshot': return _snapshot()
    if command=='searchfallback':
        _fast_search_disabled=True
        return {'fastSearch':False}
    if command in ('purchasestart','purchaseinspect','purchasebegin','purchasepoll','purchasestop','purchasenext','purchasetiming'):
        _channel()
        if command=='purchasetiming':return _purchase_timing(*args)
        if command=='purchasestart':return _purchase_start()
        if command=='purchaseinspect':return _purchase_inspect(*args)
        if command=='purchasebegin':return _purchase_begin(*args)
        if command=='purchasepoll':return _purchase_poll()
        if command=='purchasenext':return _purchase_next(*args)
        _purchase_stop()
        return {'stopped':True}
    if command in ('salestart','salepoll','salestop'):
        if command=='salestop':
            _sale_stop()
            return {'stopped':True}
        _channel()
        if command=='salestart':return _sale_start()
        return {'events':list(_sale_events),'lastSequence':_sale_sequence}
    if command in ('marketbegin','marketpoll','marketend'):
        _channel()
        if command=='marketbegin':return _market_begin(*args)
        if command=='marketpoll':return dict(_market) if _market is not None else {'status':'Unknown','reason':'No pending verification'}
        _market_end()
        return {'ended':True}
    if command in ('details','capture'):
        before=_snapshot()
        if command=='capture' and before['generation']<=int(args[0]):
            return {'generation':before['generation']}
        try:before['details']=_details(before)
        except Exception as error:
            if command=='details':raise
            before['detailError']=_text(str(error))
        after=_snapshot()
        if (before['generation'],before['raw'],before['page'])!=(after['generation'],after['raw'],after['page']):
            raise ValueError('Ayrintilar okunurken sonuc degisti; tekrar okunmali')
        return before
    if command=='stop':
        if globals().get('_rotation_moving', False):
            try:
                _rotation_walk_stop()
            except Exception:
                pass
        _purchase_stop()
        cleanup_error=None
        try:_market_end()
        except Exception as error:cleanup_error=str(error)
        if _original_refresh is not None:
            _refresh_class.RefreshList=_original_refresh
            _original_refresh=None
        if _detail_tip is not None:
            _detail_tip.HideToolTip()
            _detail_tip.ClearToolTip()
            _detail_tip=None
        _active_filters=None
        _sort_target=None
        _fast_search_verified=False
        _fast_search_disabled=False
        _catalog_cache.clear()
        _server_info=None
        return {'stopped':True,'cleanupWarning':cleanup_error}
    _channel()
    if _window is None or not _window.IsShow(): raise ValueError('Ticaret Cami kapandi')
    if command=='setsort': return _set_sort(int(args[0]))
    if command in ('search','searchprice','searchfast'):
        if command=='searchprice':
            job,category,sub,mingold,maxgold,mincheque,maxcheque=[int(v) for v in args]
            args=(job,category,sub)
            if mingold>maxgold or mincheque>maxcheque or maxgold>int(_constant('GOLD_MAX')) or maxcheque>int(_constant('CHEQUE_MAX')): raise ValueError('Fiyat araligi gecersiz')
            prices=(str(mingold),str(maxgold),str(mincheque),str(maxcheque))
        else:
            job,category,sub=args
            prices=('0',str(_constant('GOLD_MAX')),'0',str(_constant('CHEQUE_MAX')))
        if not any((x['job'],x['category'],x['sub'])==args for x in _plan): raise ValueError('Kategori planda yok')
        fast=command=='searchfast' and _fast_search_verified and not _fast_search_disabled
        limit=min(float(_window.CLICK_LIMIT_TIME),0.5) if fast else float(_window.CLICK_LIMIT_TIME)
        remaining=limit-(float(_app.GetTime())-float(_window.searchclicktime))
        if remaining>=0:
            return {'busy':True,'generation':_generation,'waitMs':int(remaining*1000)+1}
        _window.OnChangeJobSlot(job)
        _window.OnChangeItemTypeSlot(category)
        _window.OnChangeItemSubTypeSlot(sub)
        actual=tuple(int(getattr(_window,name)) for name in ('savejobselectnumber','saveitemMasktypeselectnumber','saveitemMasksubtypeselectnumber'))
        if actual!=args: raise ValueError('Secilen kategori istemciyle eslesmedi: %s != %s' % (actual,args))
        for name,text in (('minrefinevalue','0'),('maxrefinevalue','255'),('minlevelvalue','0'),
                          ('maxlevelvalue','255'),('mingoldvalue',prices[0]),
                          ('maxgoldvalue',prices[1]),('minchequevalue',prices[2]),
                          ('maxchequevalue',prices[3]),('ItemNameValue','')):
            field=getattr(_window,name)
            if name in ('minrefinevalue','maxrefinevalue','minlevelvalue','maxlevelvalue'):
                setter=getattr(field,'SetMax',None)
                if callable(setter):setter(3)
            field.SetText(text)
            if _text(field.GetText())!=unicode(text): raise ValueError('Filtre degeri kirpildi: '+name)
        _active_filters=args
        _apply_sort()
        before=_generation
        sender=getattr(_net,'SendPrivateShopSearchInfo',None)
        observed=[]
        old_click=_window.searchclicktime
        def forward(*values):
            if observed:raise ValueError('Multiple search sends in one request')
            observed.append(values)
            return sender(*values)
        if callable(sender):setattr(_net,'SendPrivateShopSearchInfo',forward)
        try:
            if fast:_window.searchclicktime=float(_app.GetTime())-float(_window.CLICK_LIMIT_TIME)-0.01
            _window.Search()
        except Exception:
            _fast_search_disabled=True
            raise
        finally:
            if callable(sender):setattr(_net,'SendPrivateShopSearchInfo',sender)
            if fast:_window.searchclicktime=float(_app.GetTime()) if observed else old_click
        if len(observed)==1:_fast_search_verified=True
        elif fast:_fast_search_disabled=True
        return {'requested':True,'generation':before,'fastSearch':fast and len(observed)==1,'filters':{'job':job,'category':category,'sub':sub,'minLevel':0,'maxLevel':255,'minRefine':0,'maxRefine':255}}
    if command=='page':
        before=_generation
        _net.SendPrivateShopSearchInfoSub(args[0])
        return {'requested':True,'generation':before}
    raise ValueError('Bilinmeyen komut')
def _bridge_dispatch(command,*args):
    global _bridge_response
    try:
        _bridge_response=_json(_dispatch(command,*args))
    except Exception as error:
        _bridge_response=_json({'error':str(error),'errorType':type(error).__name__,'command':command,
                               'fatal':command not in ('search','searchfast','page','details','capture','marketbegin','marketpoll','marketend','gamestate','loginprepare','loginserver','loginchannel','loginconnect','loginenter','useglass')})
