import sys, gc, _weakref
_test_clock=[100.0]
_test_app=type(sys)('app')
_test_app.GetTime=lambda:_test_clock[0]
sys.modules['app']=_test_app
_test_ui=type(sys)('uiPrivateShopSearch')
sys.modules['uiPrivateShopSearch']=_test_ui
_test_requests=[]
_test_search_sends=[]
_test_shop_flags=[0,0]
class _TestShop:
    GOLD_MAX=1999999999
    CHEQUE_MAX=3000
    def GetPrivateShopSearchResultCount(self): return 1
    def GetPrivateShopItemRefineElement(self,slot):return [2,15,12,6]
class _TestNet:
    def SendPrivateShopSearchInfo(self,*args):_test_search_sends.append(args)
    def GetServerInfo(self):return 'Test Sunucusu, CH1'
    def SendPrivateShopSearchInfoSub(self,page):
        _test_requests.append(('page',page))
    def DirectEnter(self,local,page):_test_requests.append(('enter',local,page))
    def SendOnClickPacket(self,vid):
        _test_requests.append(('click',vid))
        _test_root.StartShop(vid)
    def SendShopEndPacket(self):_test_requests.append(('shop-end',))
class _TestWindow:
    selectitemRealindex=0
    def OnBuyAcceptEvent(self):
        _test_requests.append(('manual-buy',))
        return 73
    CLICK_LIMIT_TIME=1.0
    searchclicktime=0.0
    searchbutton=type('DisabledButton',(),{'IsDisable':lambda self:True})()
    ITEM_MASK_TYPE_DIC={0:'Hepsi',1:'Binekler',2:'Silahlar',3:'Zirhlar',4:'Diger'}
    ITEM_MASK_MASK_SUBTYPE_DICS={0:{255:'Hepsi'},1:{0:'Binek'},2:{0:{0:'Tek Elli',3:'Cift Elli'},1:{0:'Tek Elli',1:'Hancer',2:'Yay'},2:{0:'Tek Elli'},3:{4:'Can',5:'Yelpaze'},4:{6:'Pence'}},3:{0:'Govde'},4:{255:'Hepsi'}}
    JOB_NAME_DIC={0:'Savasci',1:'Ninja',2:'Sura',3:'Saman',4:'Lycan',5:'Hepsi'}
    JOB_MAX_COUNT=5
    MASK_ITEM_TYPE_NONE=0
    MAX_CHAR_LEVEL=120
    savejobselectnumber=0
    saveitemMasktypeselectnumber=0
    saveitemMasksubtypeselectnumber=0
    _PrivateShopSeachWindow__save_sort_select_type=0
    _PrivateShopSeachWindow__SORT_NAME_DIC={0:'En dusuk genel fiyat',1:'En dusuk bireysel fiyat',2:'En yeniler',3:'En yuksek genel fiyat'}
    def OnChangeSortSlot(self,n):
        _test_requests.append(('sort',n))
        self._PrivateShopSeachWindow__save_sort_select_type=n
    def IsShow(self): return True
    def RefreshList(self): return 42
    def OnChangeJobSlot(self,n): self.savejobselectnumber=n
    def OnChangeItemTypeSlot(self,n): self.saveitemMasktypeselectnumber=n
    def OnChangeItemSubTypeSlot(self,n): self.saveitemMasksubtypeselectnumber=n
    def Search(self):
        if _test_clock[0]-self.searchclicktime<=self.CLICK_LIMIT_TIME:return
        self.searchclicktime=_test_clock[0]
        _test_ui.net.SendPrivateShopSearchInfo(self.savejobselectnumber,self.saveitemMasktypeselectnumber,self.saveitemMasksubtypeselectnumber)
        _test_requests.append(('search',self.savejobselectnumber,self.saveitemMasktypeselectnumber,self.saveitemMasksubtypeselectnumber))
        self.RefreshList()
class _TestText:
    def SetText(self,text): self.text=text
    def GetText(self):return self.text
_test_window=_TestWindow()
for _field in ('minrefinevalue','maxrefinevalue','minlevelvalue','maxlevelvalue','mingoldvalue','maxgoldvalue','minchequevalue','maxchequevalue','ItemNameValue'):
    setattr(_test_window,_field,_TestText())
_test_ui.PrivateShopSeachWindow=_TestWindow
_test_ui.shop=_TestShop()
_test_ui.net=_TestNet()
_test_gc=gc.get_objects
gc.get_objects=lambda:[]
class _TestRoot:
    def StartShop(self,vid):_test_shop_flags[:]=[1,1]
    def EndShop(self):_test_shop_flags[:]=[0,0]
    def OnShopError(self,*args):return 81
    def AppendChat(self,*args):return 82
_test_root=_TestRoot()
_test_interface=_TestRoot()
_test_root.interface=_weakref.proxy(_test_interface)
_test_interface.wndPrivateShopSearch=_weakref.proxy(_test_window)
def _native_game_window(): return _weakref.proxy(_test_root)
def _native_snapshot(): return {'count':1,'total':12,'page':1,'raw':'00'*187,'shopOpen':_test_shop_flags[0],'shopPrivate':_test_shop_flags[1]}
def _native_text(text): return u''.join(unichr(ord(c)) for c in text)
_test_tooltip_module=type(sys)('uiToolTip')
sys.modules['uiToolTip']=_test_tooltip_module
class _TestItem:
    ITEM_TYPE_WEAPON=1
    def SelectItem(self,vnum):self.vnum=vnum
    def GetItemType(self):return 1
    def GetItemSubType(self):return 0
    def GetValue(self,i):return i
    def GetAffect(self,i):return (i,i*10)
    def GetLimit(self,i):return (i,120)
    def GetItemName(self):return 'Stone+4'
class _TestTip:
    REFINE_ELEMENT_INDEX_GRADE=0
    REFINE_ELEMENT_INDEX_APPLY_TYPE=1
    REFINE_ELEMENT_INDEX_VALUE=2
    REFINE_ELEMENT_INDEX_BONUS=3
    def __init__(self):self.childrenList=[]
    def ShowToolTip(self):raise AssertionError('Must not show tooltip')
    def Show(self):raise AssertionError('Must not show window')
    def HideToolTip(self):pass
    def ClearToolTip(self):self.childrenList=[]
    def AppendTextLine(self,text):
        widget=_TestText();widget.text=text;self.childrenList.append(widget);return widget
    def SetPrivateSearchItem(self,slot):
        self.ClearToolTip()
        self.AppendTextLine(u'Emi\u015f oran\u0131: %20')
        self.AppendTextLine(u'Ate\u015fin G\u00fcc\u00fc +%12')
        self.AppendTextLine('Stone+4')
        self._ItemToolTip__AppendAcceItemAffectInformation(0,[],[],[])
        self.ShowToolTip()
    def _ItemToolTip__AppendAcceItemAffectInformation(self,*args):
        _test_tooltip_module.item.SelectItem(189)
        _test_tooltip_module.item.SelectItem(0)
    def GetMetinSocketType(self,value):return 1
    def GetMetinItemIndex(self,value):return value
    def GetRefineElement(self,raw):return raw
    def _ItemToolTip__GetAffectString(self,kind,value):return u'Ate\u015fin G\u00fcc\u00fc +%12'
_test_tooltip_module.item=_TestItem()
_test_tooltip_module.ItemToolTip=_TestTip
_test_character=type(sys)('chr')
_test_character.HasInstance=lambda vid:vid==123
_test_character.GetNameByVID=lambda vid:'Seller'
sys.modules['chr']=_test_character
_test_common=type(sys)('uiCommon')
class _TestPopup:
    def SetText(self,text):return 83
_test_common.PopupDialog=_TestPopup
class _TestExPopup(_TestPopup):pass
_test_common.ExPopupDialog=_TestExPopup
sys.modules['uiCommon']=_test_common

_test_native={'player':1,'channel':1,'capacity':90,'items':{5:60005,7:27001},'used':[],'glass':[True]}
def _native_world():return {'player':_test_native['player'],'channel':_test_native['channel'],'capacity':_test_native['capacity']}
def _native_find_item(vnum):
    for slot in sorted(_test_native['items']):
        if _test_native['items'][slot]==vnum:return slot
    return -1
def _native_use_item(slot):
    _test_native['used'].append(slot)
    return True
class _TestListBox:
    def __init__(self,ids):
        self.ids=list(ids);self.itemList=list(ids);self.sel=0
    def SelectItem(self,row):self.sel=row
class LoginWindow:
    def __init__(self):
        self.serverList=_TestListBox([1,2]);self.channelList=_TestListBox([1,2,3]);self.clicks=0
    def _LoginWindow__GetServerID(self):return self.serverList.ids[self.serverList.sel]
    def _LoginWindow__GetChannelID(self):return self.channelList.ids[self.channelList.sel]
    def _LoginWindow__OnClickSelectServerButton(self):self.clicks+=1
class SelectCharacterWindow:pass
class LoadingWindow:pass
class GameWindow:pass
_test_network_module=type(sys)('networkModule')
class MainStream(object):
    curPhaseWindow=None
_test_network_module.MainStream=MainStream
sys.modules['networkModule']=_test_network_module
_test_server_info=type(sys)('serverInfo')
_test_server_info.REGION_DICT={0:{1:{'name':'Baska Sunucu','channel':{1:{'name':'CH1'}}},2:{'name':'Test Sunucusu','channel':{1:{'name':'CH1'},2:{'name':'CH2'},3:{'name':'CH3'}}}}}
sys.modules['serverInfo']=_test_server_info
