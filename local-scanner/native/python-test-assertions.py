_bridge_dispatch('inspect')
assert len(_plan)==16 and _original_refresh is not None
assert set(x['job'] for x in _plan if x['category'] in (2,3))==set(range(5))
assert all(x['job']==5 for x in _plan if x['category'] in (1,4))
assert set(x['category'] for x in _plan)==set((1,2,3,4))
assert not any('Hepsi' in x['label'] for x in _plan)
assert any(x['job']==0 and x['category']==2 and x['sub']==0 for x in _plan)
assert any(x['category']==4 and x['sub']==255 for x in _plan)
assert not any(x['category'] in (2,3) and x['sub']==255 for x in _plan)
assert not any(x['job']==0 and x['category']==2 and x['sub']==1 for x in _plan)
assert any(x['job']==1 and x['category']==2 and x['sub']==1 for x in _plan)
assert _plan[0]['category']==2 and not any('{' in x['label'] for x in _plan)
_stages=[0 if x['category']==2 else 1 if x['category']==3 else 2 for x in _plan]
_live_types=dict(_test_window.ITEM_MASK_TYPE_DIC)
_live_types[3]='Ekipmanlar'
_live_order=_build_plan(_test_window.JOB_NAME_DIC,_live_types,_test_window.ITEM_MASK_MASK_SUBTYPE_DICS)
_live_stages=[0 if x['category']==2 else 1 if x['category']==3 else 2 for x in _live_order]
assert _live_stages==sorted(_live_stages)
_no_all_jobs=dict((k,v) for k,v in _test_window.JOB_NAME_DIC.items() if k!=5)
assert set(x['job'] for x in _build_plan(_no_all_jobs,_live_types,_test_window.ITEM_MASK_MASK_SUBTYPE_DICS))==set(range(5))
assert _stages==sorted(_stages)
assert 'serverInfo' in _bridge_response,_bridge_response[:600]
assert 'GameWindow.interface.wndPrivateShopSearch' in _bridge_response,_bridge_response[:600]
assert '"recentIndex":2' in _bridge_response and '"selected":0' in _bridge_response,_bridge_response[:900]
assert _sort_info()['unitPriceIndex']==1
_sort_names=_test_window._PrivateShopSeachWindow__SORT_NAME_DIC
_saved_sort_names=dict(_sort_names)
_sort_names[7]=_sort_names.pop(1)
assert _sort_info()['unitPriceIndex']==7,'unit price index was guessed'
_sort_names[8]=_sort_names[7]
assert _sort_info()['unitPriceIndex'] is None,'ambiguous unit price accepted'
_sort_names.clear();_sort_names.update(_saved_sort_names)
_bridge_dispatch('search',5,1,0)
assert _test_requests==[('search',5,1,0)]
assert _generation==1
assert _test_window.ItemNameValue.text=='' and _test_window.maxgoldvalue.text=='1999999999'
assert _test_window.minlevelvalue.text=='0' and _test_window.maxrefinevalue.text=='255'
_bridge_dispatch('snapshot')
assert '"generation":1' in _bridge_response and '"count":1' in _bridge_response
_bridge_dispatch('details')
assert 'client-rendered' in _bridge_response and 'rendererCalls' in _bridge_response
_bridge_dispatch('capture',0)
assert 'client-rendered' in _bridge_response and '"generation":1' in _bridge_response
_bridge_dispatch('capture',1)
assert '"generation":1' in _bridge_response and '"raw"' not in _bridge_response and '"details"' not in _bridge_response
_saved_details_function=_details
def _failed_capture_details(result):raise ValueError('capture-detail-test')
_details=_failed_capture_details
_bridge_dispatch('capture',0)
assert '"detailError"' in _bridge_response and 'capture-detail-test' in _bridge_response and '"raw"' in _bridge_response
def _changed_capture_details(result):
    global _generation
    _generation+=1
    return []
_details=_changed_capture_details
_bridge_dispatch('capture',0)
assert '"error"' in _bridge_response and '"fatal":false' in _bridge_response,'mixed-generation capture accepted'
_details=_saved_details_function
_generation=1
_bridge_dispatch('details')
assert '\\u015f' in _bridge_response and 'Stone+4' in _bridge_response
assert 'sourceVnum' in _bridge_response and '189' in _bridge_response
assert '"grade":2' in _bridge_response and '"applyType":15' in _bridge_response and '"bonus":6' in _bridge_response
assert _test_tooltip_module.item.SelectItem.__name__=='SelectItem'
_test_tooltip_module.ItemToolTip._ItemToolTip__AppendAcceItemAffectInformation=lambda *args: (_ for _ in ()).throw(ValueError('render-test'))
_detail_tip=None
_bridge_dispatch('details')
assert 'render-test' in _bridge_response and _test_tooltip_module.item.SelectItem.__name__=='SelectItem'
_test_tooltip_module.ItemToolTip._ItemToolTip__AppendAcceItemAffectInformation=lambda *args:None
_bridge_dispatch('page',2)
assert _test_requests[-1]==('page',2)
_bridge_dispatch('setsort',2)
assert _test_requests[-1]==('sort',2) and '"selected":2' in _bridge_response,_bridge_response[:600]
_bridge_dispatch('setsort',9)
assert '"error"' in _bridge_response
_test_window._PrivateShopSeachWindow__save_sort_select_type=1
_bridge_dispatch('snapshot')
assert '"error"' in _bridge_response and 'siralama' in _bridge_response,_bridge_response[:600]
_test_window._PrivateShopSeachWindow__save_sort_select_type=2
_bridge_dispatch('search',255,255,255)
assert '"error"' in _bridge_response
_test_clock[0]+=2.0
_bridge_dispatch('searchprice',5,1,0,4500,4500,0,0)
assert _test_requests[-1]==('search',5,1,0),_bridge_response[:300]
assert _test_window.mingoldvalue.text=='4500' and _test_window.maxgoldvalue.text=='4500' and _test_window.minchequevalue.text=='0' and _test_window.maxchequevalue.text=='0'
_bridge_dispatch('searchprice',5,1,0,5,4,0,0)
assert '"error"' in _bridge_response
_test_clock[0]+=2.0
_bridge_dispatch('search',5,1,0)
assert _test_window.mingoldvalue.text=='0' and _test_window.maxgoldvalue.text=='1999999999' and _test_window.maxchequevalue.text!='0'
assert _json(u'\u00c7a\u011fr\u0131"\\\n')=='"\\u00c7a\\u011fr\\u0131\\"\\\\\\u000a"'
_bridge_dispatch('stop')
_test_window.RefreshList()
assert _generation==3 and _original_refresh is None
_test_window.IsShow=lambda:False
_bridge_dispatch('inspect')
assert 'kapali' in _bridge_response and _original_refresh is None
_test_window.IsShow=lambda:True
def _missing_root(): raise RuntimeError('GameWindow hazir degil')
_native_game_window=_missing_root
gc.get_objects=lambda:[_test_window,_test_window]
_bridge_dispatch('inspect')
assert 'gc fallback' in _bridge_response and _original_refresh is not None
_bridge_dispatch('stop')
gc.get_objects=lambda:[_test_window,_TestWindow()]
_bridge_dispatch('inspect')
assert 'gc acik pencere=2' in _bridge_response and _original_refresh is None
gc.get_objects=_test_gc
_native_game_window=lambda:_test_root
_bridge_dispatch('inspect')
_test_clock[0]+=2
for _node in _plan:
    _args=(_node['job'],_node['category'],_node['sub'])
    _start=len(_test_requests)
    assert _dispatch('search',*_args)['requested'] is True
    assert _test_requests[-1]==('search',)+_args
    _busy=_dispatch('search',*_args)
    assert _busy['busy'] is True and _busy['waitMs']>0
    assert len(_test_requests)==_start+1
    _test_clock[0]+=2
_bridge_dispatch('stop')
_bridge_dispatch('inspect')
_original_start=_TestRoot.StartShop
_result=_dispatch('marketbegin',123,'53656c6c6572')
assert _result['status']=='Open' and _result['seller']=='Seller'
assert _dispatch('marketpoll')['status']=='Open'
_dispatch('marketend')
assert _TestRoot.StartShop==_original_start and _test_shop_flags==[0,0]
assert _test_requests[-1]==('shop-end',)
_start=len(_test_requests)
assert _dispatch('marketbegin',456,'53656c6c6572')['status']=='Unknown'
assert _dispatch('marketbegin',123,'57726f6e67')['status']=='Unknown'
assert len(_test_requests)==_start
_test_ui.net.SendOnClickPacket=lambda vid:None
assert _dispatch('marketbegin',123,'53656c6c6572')['status']=='Pending'
assert _dispatch('marketpoll')['status']=='Pending'
_dispatch('marketend')
assert _TestRoot.StartShop==_original_start
_test_ui.net.GetServerInfo=lambda:'Other Server, CH1'
_bridge_dispatch('search',1,1,0)
assert 'Sunucu degisti' in _bridge_response
_test_ui.net.GetServerInfo=lambda:'Test Sunucusu, CH1'
_bridge_dispatch('stop')
_bridge_dispatch('inspect')
_before=len(_test_requests)
_accept=_TestWindow.OnBuyAcceptEvent
_chat=_TestRoot.AppendChat
_error=_TestRoot.OnShopError
_popup=_TestPopup.SetText
assert _dispatch('salestart')['sendsPurchase'] is False
assert len(_test_requests)==_before and _dispatch('salepoll')['events']==[]
assert _test_window.OnBuyAcceptEvent()==73
assert len(_test_requests)==_before+1 and _test_requests[-1]==('manual-buy',)
assert _test_root.OnShopError('NOT_ENOUGH_MONEY')==81
assert _test_root.AppendChat(1,u'Premium pazar haz\u0131rlan\u0131yor')==82
_events=_dispatch('salepoll')['events']
assert len(_events)==2 and not _events[0]['preparingCandidate'] and _events[1]['preparingCandidate']
assert _events[1]['correlation']=='time-only' and len(_events[1]['context']['raw'])==374
assert _TestPopup().SetText(u'Premium pazar haz\u0131rlan\u0131yor')==83
assert len(_dispatch('salepoll')['events'])==3
_test_clock[0]+=6
_test_root.AppendChat(1,u'Premium pazar haz\u0131rlan\u0131yor')
assert len(_dispatch('salepoll')['events'])==3
_dispatch('salestop')
assert _TestWindow.OnBuyAcceptEvent==_accept and _TestRoot.AppendChat==_chat and _TestRoot.OnShopError==_error
assert _TestPopup.SetText==_popup
assert 'SetText' not in _TestExPopup.__dict__
_before=len(_test_requests)
_dispatch('salestart')
_dispatch('salestop')
assert len(_test_requests)==_before
_bridge_dispatch('stop')

assert _sale_preparing_candidate(u'[Ki\u015fisel premium d\u00fckkan] D\u00fckkan \u015fu anda haz\u0131rlan\u0131yor.')
assert _sale_preparing_candidate(u'Premium pazar haz\u0131rlan\u0131yor')
assert _sale_preparing_candidate(u'Premium d\u00fckkan d\u00fczenleniyor')
assert not _sale_preparing_candidate(u'Premium d\u00fckkan haz\u0131r.')
assert not _sale_preparing_candidate(u'Premium d\u00fckkan haz\u0131rlanm\u0131yor')
assert not _sale_preparing_candidate(u'Yetersiz Yang')
assert not _sale_preparing_candidate(u'D\u00fckkan \u015fu anda haz\u0131rlan\u0131yor.')

import sys as _wallet_sys
import types as _wallet_types
_wallet_old_player=_wallet_sys.modules.get('player')
_wallet_old_net=_wallet_sys.modules.get('net')
_wallet_player=_wallet_types.ModuleType('player')
_wallet_player.GetStatus=lambda:0
_wallet_player.GetElk=lambda:0
_wallet_player.GetCheque=lambda:0
_wallet_net=_wallet_types.ModuleType('net')
_wallet_net.GetServerInfo=lambda:'Test Sunucusu, CH1'
_wallet_sys.modules['player']=_wallet_player
_wallet_sys.modules['net']=_wallet_net
_wallet_requests=len(_test_requests)
try:
    _wallet=_purchase_wallet()
    assert _wallet['walletVerified'] and _wallet['yang']==0 and _wallet['won']==0
    assert _wallet['readOnly'] and not _wallet['sendsPurchase']
    _wallet_player.GetCheque=lambda:1
    assert _purchase_wallet()['won']==1
    _wallet_player.GetElk=lambda:True
    assert not _purchase_wallet()['walletVerified']
    del _wallet_player.GetCheque
    assert not _purchase_wallet()['walletVerified']
    assert len(_test_requests)==_wallet_requests and not _sale_hooks and _original_refresh is None
finally:
    if _wallet_old_player is None:del _wallet_sys.modules['player']
    else:_wallet_sys.modules['player']=_wallet_old_player
    if _wallet_old_net is None:del _wallet_sys.modules['net']
    else:_wallet_sys.modules['net']=_wallet_old_net

_bridge_dispatch('inspect')
assert '"loginTarget"' in _bridge_response and '"server":2' in _bridge_response and '"channel":1' in _bridge_response,_bridge_response[-700:]
assert _login_target['server']==2 and _login_target['channel']==1
_test_stream=MainStream()
_stream=_test_stream
_login_window=LoginWindow()
_test_stream.curPhaseWindow=_login_window
_bridge_dispatch('gamestate')
assert '"phase":"login"' in _bridge_response and '"playerReady":false' in _bridge_response,_bridge_response
_bridge_dispatch('loginserver')
assert '"selected":"server"' in _bridge_response and _login_window._LoginWindow__GetServerID()==2,_bridge_response
_bridge_dispatch('loginchannel')
assert _login_window._LoginWindow__GetChannelID()==1,_bridge_response
_bridge_dispatch('loginconnect')
assert _login_window.clicks==1 and '"clicked":true' in _bridge_response,_bridge_response
_login_window.channelList.sel=2
_bridge_dispatch('loginconnect')
assert '"error"' in _bridge_response and _login_window.clicks==1
_bridge_dispatch('loginenter',3)
assert '"error"' in _bridge_response and not any(r[0]=='enter' for r in _test_requests)
_test_stream.curPhaseWindow=SelectCharacterWindow()
_bridge_dispatch('gamestate')
assert '"phase":"select"' in _bridge_response
_bridge_dispatch('loginenter',3)
assert _test_requests[-1]==('enter',3,0),_bridge_response
_bridge_dispatch('loginenter',5)
assert '"error"' in _bridge_response
_bridge_dispatch('loginserver')
assert '"error"' in _bridge_response
_test_stream.curPhaseWindow=LoadingWindow()
_bridge_dispatch('gamestate')
assert '"phase":"loading"' in _bridge_response and '"playerReady":false' in _bridge_response
_test_stream.curPhaseWindow=GameWindow()
_bridge_dispatch('gamestate')
assert '"phase":"game"' in _bridge_response and '"playerReady":true' in _bridge_response and '"glassSlot":5' in _bridge_response and '"glassOpen":true' in _bridge_response,_bridge_response
_bridge_dispatch('useglass')
assert '"alreadyOpen":true' in _bridge_response and _test_native['used']==[]
_closed_window=_TestWindow()
_closed_window.IsShow=lambda:False
_test_interface.wndPrivateShopSearch=_weakref.proxy(_closed_window)
_bridge_dispatch('gamestate')
assert '"glassOpen":false' in _bridge_response,_bridge_response
_bridge_dispatch('useglass')
assert '"used":true' in _bridge_response and _test_native['used']==[5],_bridge_response
_test_native['items']={}
_bridge_dispatch('useglass')
assert '"error"' in _bridge_response and 'Ticaret Cami yok' in _bridge_response and _test_native['used']==[5]
_test_native['items']={5:60005}
_test_native['player']=0
_bridge_dispatch('useglass')
assert '"error"' in _bridge_response and _test_native['used']==[5]
_test_native['player']=1
_test_stream.curPhaseWindow=LoginWindow()
_bridge_dispatch('useglass')
assert '"error"' in _bridge_response and _test_native['used']==[5]
_login_target=None
_bridge_dispatch('loginprepare','546573742053756e75637573752c20434831')
assert '"error"' not in _bridge_response,_bridge_response
assert _login_target is not None and _login_target['server']==2 and _login_target['channel']==1,_bridge_response
_login_saved_regions=_test_server_info.REGION_DICT
_test_server_info.REGION_DICT={0:{9:{'name':'Safir','channel':{1:{'name':'CH 1'}}}}}
assert _compute_login_target('[SAPPHIRE] Safir, CH1')['server']==9
assert _compute_login_target('|cffff0000[SAPPHIRE] Safir|r, Kanal 1')['channel']==1
assert not _compute_login_target('[SAPPHIRE] Safir, CH2')['ok']
_test_server_info.REGION_DICT[0][10]={'name':'Safir','channel':{1:{'name':'CH1'}}}
assert not _compute_login_target('[SAPPHIRE] Safir, CH1')['ok'],'ambiguous server alias accepted'
_test_server_info.REGION_DICT[0][9]['name']='[SAPPHIRE] Safir'
assert _compute_login_target('[SAPPHIRE] Safir, CH1')['server']==9
_test_server_info.REGION_DICT={0:{9:{'name':'[RUBY] Safir','channel':{1:{'name':'CH1'}}}}}
assert not _compute_login_target('[SAPPHIRE] Safir, CH1')['ok'],'different tagged server accepted'
_test_server_info.REGION_DICT={1:{9:{'name':'Safir','channel':{1:{'name':'CH1'}}}}}
assert _compute_login_target('[SAPPHIRE] Safir, CH1')['region']==1
_login_target=_compute_login_target('[SAPPHIRE] Safir, CH1')
_stream=MainStream();_stream.curPhaseWindow=LoginWindow()
try:_login_pick('server');assert False,'wrong login region selected'
except ValueError:pass
try:_login_connect();assert False,'wrong login region connected'
except ValueError:pass
assert _stream.curPhaseWindow.clicks==0
_test_server_info.REGION_DICT=_login_saved_regions
_login_target=_compute_login_target('Test Sunucusu, CH1')
_stream=None

_bridge_dispatch('stop')
_test_interface.wndPrivateShopSearch=_weakref.proxy(_test_window)
_bridge_dispatch('inspect')
_fast_search_verified=False
_fast_search_disabled=False
_saved_limit=_test_window.CLICK_LIMIT_TIME
_test_window.CLICK_LIMIT_TIME=3.0
_test_clock[0]+=4
_before_sends=len(_test_search_sends)
_bridge_dispatch('search',5,1,0)
assert len(_test_search_sends)==_before_sends+1 and _fast_search_verified,_bridge_response
_test_clock[0]+=0.2
_bridge_dispatch('searchfast',5,1,0)
assert '"busy":true' in _bridge_response and len(_test_search_sends)==_before_sends+1
_test_clock[0]+=0.4
_bridge_dispatch('searchfast',5,1,0)
assert '"fastSearch":true' in _bridge_response and len(_test_search_sends)==_before_sends+2
assert _test_search_sends[-1]==(5,1,0) and _test_window.searchclicktime==_test_clock[0]
_bridge_dispatch('searchfallback')
_test_clock[0]+=0.6
_bridge_dispatch('searchfast',5,1,0)
assert '"busy":true' in _bridge_response,'fallback did not restore original UI interval'
_test_window.CLICK_LIMIT_TIME=_saved_limit
_fast_search_disabled=False

_saved_wallet_function=_purchase_wallet
_purchase=None
_purchase_wallet=lambda:{'walletVerified':True,'yang':0,'won':0}
assert _purchase_timing(500)=={'intervalMs':500,'quietMs':250}
_purchase_wallet=lambda:{'walletVerified':True,'yang':1,'won':0}
try:_purchase_timing(500);assert False,'funded fast timing accepted'
except ValueError:pass
_purchase_wallet=lambda:{'walletVerified':True,'yang':0,'won':0}
_purchase={}
try:_purchase_timing(500);assert False,'pending purchase pacing changed'
except ValueError:pass
_purchase=None
assert _purchase_timing(1000)['quietMs']==500
_purchase_wallet=_saved_wallet_function
