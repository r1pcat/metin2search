_rot_saved = dict((k, globals().get(k)) for k in ('_native_game_window','_gamestate','_native_world','_native_navigation','_native_snapshot','_purchase_stop','_market_end','_stream'))
_rot_saved_gc = gc.get_objects
_rot_saved_ui_net = _ui.net
_rot_items = {4: [60005,1], 5: [0,0]}
_rot_box = {7: [0,0], 8: [0,0]}
_rot_sends = []
_rot_world_ready = [True]
_rot_map = ['metin2_map_privateshop']
_rot_net = type(sys)('_rotation_net')
_rot_net.SAFEBOX_PASSWORD_TYPE = 3
_rot_net.ACCOUNT_CHARACTER_SLOT_NAME = 0
_rot_net.GetAccountCharacterSlotDataString = lambda slot,kind:'R'+str(slot)
_rot_net.SendSafeboxCheckinPacket = lambda *args:_rot_sends.append(('deposit',args))
_rot_net.SendSafeboxCheckoutPacket = lambda *args:_rot_sends.append(('withdraw',args))
_rot_net.SendOnClickPacket = lambda *args:_rot_sends.append(('npc',args))
_rot_net.SendShopEndPacket = lambda:_test_requests.append(('shop-end',))
_ui.net=_rot_net
_rot_player = type(sys)('_rotation_player')
_rot_player.GetName = lambda:'R0'
_rot_player.GetElk = lambda:0
_rot_player.GetCheque = lambda:0
_rot_player.GetItemIndex = lambda slot:_rot_items.get(slot,[0,0])[0]
_rot_player.GetItemCount = lambda slot:_rot_items.get(slot,[0,0])[1]
_rot_player.INVENTORY_PAGE_SIZE = 45
_rot_player.INVENTORY_SLOT_COUNT = 15
_rot_storage = type(sys)('_rotation_storage')
_rot_storage.GetItemID = lambda slot:_rot_box[slot][0]
_rot_storage.GetItemCount = lambda slot:_rot_box[slot][1]
_rot_storage.GetMallItemID = lambda slot:0
_rot_size = type(sys)('_rotation_size')
_rot_size.GetCurrentSafeboxSize = lambda:0
_rot_size.GetItemID = lambda slot:777
_rot_size.GetItemCount = lambda slot:1
_rot_item = type(sys)('_rotation_item')
_rot_selected_item = [0]
_rot_item.SelectItem = lambda vnum:_rot_selected_item.__setitem__(0,vnum)
_rot_item.GetItemSize = lambda:(1,3) if _rot_selected_item[0]==123 else (1,1)
_rot_storage.GetItemID = lambda slot:_rot_box.get(slot,[0,0])[0]
_rot_storage.GetItemCount = lambda slot:_rot_box.get(slot,[0,0])[1]
_rot_background = type(sys)('_rotation_background')
_rot_background.GetCurrentMapName = lambda:_rot_map[0]
for _rmodule in (_rot_net,_rot_player,_rot_storage,_rot_size,_rot_background,_rot_item):sys.modules[_rmodule.__name__]=_rmodule
class _RotationStorageWindow:
    opened = True
    visible = 45
    pageButtonList = [object()]
    def IsShow(self):return self.opened
    def GetSlotCount(self):return self.visible
    def Close(self):self.opened=False
class _RotationRoot:pass
_rot_root=_RotationRoot();_rot_root.interface=_RotationRoot();_rot_root.interface.wndSafebox=_RotationStorageWindow()
_rot_root.interface.wndSafebox.wndItem=_rot_root.interface.wndSafebox
_rot_events=[]
_rot_shop_open=[False]
class _RotationGlassWindow:
    opened=False
    fail=False
    def IsShow(self):return self.opened
    def CloseProcess(self,send):
        assert send==1
        _rot_events.append('glass-close-server')
        if not self.fail:self.opened=False
_rot_root.interface.wndPrivateShopSearch=_RotationGlassWindow()
def _rot_end_shop():
    _rot_events.append('shop-close')
    _rot_shop_open[0]=False
_rot_root.EndShop=_rot_end_shop
_native_snapshot=lambda:{'shopOpen':_rot_shop_open[0],'shopPrivate':True}
_purchase_stop=lambda:_rot_events.append('purchase-stop')
_market_end=lambda:_rot_events.append('market-stop')
_native_game_window=lambda:_rot_root
_gamestate=lambda:{'phase':'game','playerReady':_rot_world_ready[0],'inventoryReady':True,'channel':1,'serverInfo':'Safir, CH1'}
_native_world=lambda:{'capacity':15}
_native_navigation=lambda grid:{'x':1000,'y':1000,'npcX':1100,'npcY':1000,'npcVid':123}
_rotation_context={'npcClicked':False,'answerSelected':False,'passwordSent':False}
_rotation_transfer_pending=None
_rot_root.interface.wndPrivateShopSearch.opened=True;_rot_shop_open[0]=True
assert _rotation_prepare()=={'prepared':True,'glassClosed':True,'shopClosed':True}
assert _rot_events==['purchase-stop','market-stop','shop-close','glass-close-server']
assert _test_requests[-1]==('shop-end',)
_rot_root.interface.wndPrivateShopSearch.opened=True;_rot_root.interface.wndPrivateShopSearch.fail=True
try:_rotation_prepare();assert False,'glass still open but preparation succeeded'
except ValueError:pass
_rot_root.interface.wndPrivateShopSearch.fail=False
_rotation_prepare()
assert _rotation_status()['characterSlot']==0
assert not hasattr(_rot_storage,'GetCurrentSafeboxSize')
assert not hasattr(_rot_storage,'SAFEBOX_SLOT_X_COUNT') and not hasattr(_rot_storage,'SAFEBOX_PAGE_SIZE')
_rot_box[0]=[123,1]
_rot_grid=dict((row[0],row[1]) for row in _rotation_storage()[1])
assert len(_rot_grid)==45 and _rot_grid[5]==4294967295 and _rot_grid[10]==4294967295 and _rot_grid[1]==0
del _rot_box[0]
_rot_storage.SAFEBOX_SLOT_X_COUNT=5;_rot_storage.SAFEBOX_PAGE_SIZE=45
assert len(_rotation_storage()[1])==45 and _rotation_storage()[1][0]==(0,0,0)
_rot_box[100]=[60005,1]
_rot_root.interface.wndSafebox.pageButtonList=[object(),object(),object()]
assert len(_rotation_storage()[1])==135 and _rotation_storage()[1][100]==(100,60005,1)
del _rot_box[100]
_rot_root.interface.wndSafebox.pageButtonList=[]
_rot_root.interface.wndSafebox.visible=15
assert len(_rotation_storage()[1])==15
_rot_root.interface.wndSafebox.visible=0
try:_rotation_storage();assert False,'uninitialized storage reported full'
except ValueError:pass
_rot_root.interface.wndSafebox.visible=45
_rot_root.interface.wndSafebox.pageButtonList=[object()]
for _rot_field,_rot_bad in (('SAFEBOX_SLOT_X_COUNT',6),('SAFEBOX_PAGE_SIZE',90)):
    _rot_good=getattr(_rot_storage,_rot_field)
    setattr(_rot_storage,_rot_field,_rot_bad)
    try:_rotation_storage();assert False,'unsupported storage grid accepted'
    except ValueError:pass
    setattr(_rot_storage,_rot_field,_rot_good)
del _rot_storage.SAFEBOX_SLOT_X_COUNT;del _rot_storage.SAFEBOX_PAGE_SIZE
_rotation_transfer('deposit',4,0)
assert _rot_sends==[('deposit',(1,4,0))]
try:_rotation_transfer('deposit',4,0);assert False,'duplicate deposit sent'
except ValueError:pass
assert len(_rot_sends)==1
_rot_items[4]=[0,0];_rot_box[0]=[60005,1]
assert _rotation_status()['storage'][0]==(0,60005,1) and _rotation_transfer_pending is None
_rotation_transfer('withdraw',4,0)
assert _rot_sends[-1]==('withdraw',(0,1,4))
_rot_items[4]=[60005,1];_rot_box[0]=[0,0]
_rotation_status()
_rot_items[4]=[123,1]
try:_rotation_transfer('deposit',4,7);assert False,'non glass moved'
except ValueError:pass
assert len(_rot_sends)==2
_rot_footprint = dict((row[0],row[1]) for row in _rotation_cells([(i,123 if i==0 else 0,1 if i==0 else 0) for i in range(15)],5,45))
assert _rot_footprint[5]==4294967295 and _rot_footprint[10]==4294967295 and _rot_footprint[1]==0
_rot_map[0]='other_map'
try:_rotation_status();assert False,'wrong map accepted'
except ValueError:pass
_rot_map[0]='metin2_map_privateshop'
_rot_world_ready[0]=False
try:_rotation_status();assert False,'loading world accepted'
except ValueError:pass
_rot_world_ready[0]=True
_rot_ui=type(sys)('uiSafebox')
class _RotationPassword:
    send_type=3
    hWnd=900
    shown=True
    passwordValue=_TestText()
    submits=0
    def IsShow(self):return self.shown
    def OnAccept(self):self.submits+=1
_rot_ui.PasswordDialog=_RotationPassword;sys.modules['uiSafebox']=_rot_ui
_rot_dialog=_RotationPassword();gc.get_objects=lambda:[_weakref.proxy(_rot_dialog),_rot_dialog]
assert _rotation_password_dialog() is _rot_dialog
_rot_dialog.send_type=7
assert _rotation_password_dialog() is None
_rot_dialog.send_type=3
_rotation_password('313233343536')
assert _rot_dialog.submits==1 and _rot_dialog.passwordValue.GetText()=='123456'
try:_rotation_password('313233343536');assert False,'password repeated'
except ValueError:pass
assert _rot_dialog.submits==1
_rot_root.interface.wndSafebox.opened=False
_rot_dialog.shown=False
_rot_quest=type('QuestDialog',(object,),{})()
_rot_quest.descIndex=17;_rot_quest.hWnd=700;_rot_quest.shown=False
_rot_quest.IsShow=lambda:_rot_quest.shown
def _rot_button(text):
    value=_TestText();value.SetText(text);return value
_rot_quest.btnAnswer=[_rot_button(text) for text in (u"Depocu'ya Yardim",u'Depoyu a\u00e7',u'Nesne market deposunu a\u00e7',u'Gumus ve Altin kulce al',u'Doviz Burosu',u'Kapat')]
_rot_other=type('QuestDialog',(object,),{})()
_rot_other.descIndex=18;_rot_other.hWnd=701;_rot_other.IsShow=lambda:True
_rot_other.btnAnswer=[_rot_button(u'Gorevi kabul et'),_rot_button(u'Kapat')]
def _rot_wrong_answer(index):raise AssertionError('unrelated quest answered')
_rot_other.ClickAnswerEvent=_rot_wrong_answer
def _rot_answer(index):
    assert index==1
    _rot_sends.append(('answer',(17,index)))
    _rot_quest.shown=False;_rot_dialog.shown=True
_rot_quest.ClickAnswerEvent=_rot_answer
gc.get_objects=lambda:[_weakref.proxy(_rot_quest),_rot_quest,_rot_other,_weakref.proxy(_rot_dialog),_rot_dialog]
_rotation_prepare()
assert not _rotation_open()['passwordReady']
assert _rot_sends[-1]==('npc',(123,))
_rot_quest.shown=True
assert not _rotation_open()['passwordReady']
assert _rot_sends[-1]==('answer',(17,1))
_rot_other.btnAnswer=[_rot_button(u'Depoyu ac')]
_rot_quest.shown=True
_rot_other.board=_RotationRoot();_rot_other.board.IsShow=lambda:False
assert _rotation_storage_menu()[1]==1,'cached menu with hidden board treated as visible'
_rot_other.board.IsShow=lambda:True
try:_rotation_storage_menu();assert False,'two distinct storage menus accepted'
except ValueError:pass
del _rot_other.board
_rot_other.btnAnswer=[_rot_button(u'Gorevi kabul et'),_rot_button(u'Kapat')]
_rot_quest.shown=False
assert _rotation_open()['passwordReady']
assert sum(1 for action,args in _rot_sends if action=='answer')==1
_rotation_password('313233343536')
assert _rot_dialog.submits==2
_rot_root.interface.wndSafebox.opened=True
assert _rotation_open()['open']
assert _rotation_close_storage()['closed'] and not _rot_root.interface.wndSafebox.IsShow()
_rot_only_shop=_rot_other.btnAnswer
_rot_other.btnAnswer=[_rot_button(u'Nesne market deposunu ac')]
assert _rotation_storage_menu() is None,'item mall answer mistaken for depot'
_rot_other.btnAnswer=_rot_only_shop
_rot_root.interface.wndPrivateShopSearch.opened=True
try:_rotation_open();assert False,'depot NPC allowed with reopened trade glass'
except ValueError:pass
_rot_root.interface.wndPrivateShopSearch.opened=False
_rot_select=SelectCharacterWindow();_rot_select.mycharacters=_RotationRoot()
_rot_select.mycharacters.RealSlot=[4,3,2,1,0]
_stream=MainStream();_stream.curPhaseWindow=_rot_select
_rot_list=_rotation_character_list()
assert _rot_list['roster']==['R0','R1','R2','R3','R4'] and _rot_list['displaySlots']==[4,3,2,1,0]
_rot_enters=[]
_rot_net.DirectEnter=lambda *args:_rot_enters.append(args)
_rot_saved_direct=_test_ui.net
assert _login_enter(_rot_list['roster'].index('R4'))['slot']==4
assert _rot_enters==[(4,0)]
_rot_select.mycharacters.RealSlot=[4,4,2,1,0]
try:_rotation_character_list();assert False,'ambiguous visual slot map accepted'
except ValueError:pass
_stream=None
_rot_net.GetAccountCharacterSlotDataInteger=lambda slot,kind:[10,50,30,40,20][slot]
_rot_net.ACCOUNT_CHARACTER_SLOT_LAST_PLAYTIME=9
assert _rotation_character_list()['displaySlots']==[1,3,2,4,0]
for _rmodule in (_rot_net,_rot_player,_rot_storage,_rot_size,_rot_background,_rot_item,_rot_ui):del sys.modules[_rmodule.__name__]
for _rkey,_rvalue in _rot_saved.items():
    if _rvalue is None:globals().pop(_rkey,None)
    else:globals()[_rkey]=_rvalue
gc.get_objects=_rot_saved_gc
_ui.net=_rot_saved_ui_net
_rotation_context=None;_rotation_transfer_pending=None
