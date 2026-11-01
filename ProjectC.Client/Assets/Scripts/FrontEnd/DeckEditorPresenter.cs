using System;
using System.Collections.Generic;
using System.Linq;
using Game.Utils;
using UnityEngine;
using UnityEngine.UI;

namespace ProjectC.Client
{
    public sealed class DeckEditorPresenter : MonoBehaviour
    {
        public DeckTileView TilePrefab;
        public DeckEditSession Session {get;}=new DeckEditSession();
        public ConfirmationDialog Dialog {get;private set;}
        public InputField NameInput {get;private set;}
        public InputField Search {get;private set;}
        public Dropdown DeckList {get;private set;}
        public Transform DeckContent {get;private set;}
        public Transform LibraryContent {get;private set;}
        ClientApp app;Text count,status,detailName,detailStats,detailRules,detailCrest;
        Button typeFilter,archetypeFilter,rarityFilter;
        string type="",archetype="",rarity="";
        readonly List<DeckTileView> deckTiles=new List<DeckTileView>(),libraryTiles=new List<DeckTileView>();
        List<PersonalDeck> listed=new List<PersonalDeck>();
        bool allowQuit;
        void Start()
        {
            app=ClientApp.Instance;
            this.Bind("ConfirmOverlay",out ConfirmationDialog dialog);Dialog=dialog;
            this.Bind("DeckName",out InputField name);NameInput=name;this.Bind("Search",out InputField search);Search=search;
            this.Bind("DeckList",out Dropdown list);DeckList=list;
            this.Bind("DeckCards",out Transform deck);DeckContent=deck;this.Bind("LibraryCards",out Transform library);LibraryContent=library;
            this.Bind("DeckCount",out count);this.Bind("Feedback",out status);this.Bind("DetailName",out detailName);this.Bind("DetailStats",out detailStats);this.Bind("DetailRules",out detailRules);this.Bind("DetailCrest",out detailCrest);
            Bind("Save",()=>Save());Bind("CancelEdits",()=>Guard(()=>{Session.Cancel();Refresh();}));
            Bind("Back",()=>Guard(()=>app.Navigate("LobbyScene")));Bind("NewDeck",()=>Guard(Create));
            Bind("CopyDeck",()=>Guard(Duplicate));Bind("DeleteDeck",Delete);
            Bind("ImportStarter",()=>Guard(ImportStarter));
            this.BindButton("TypeFilter",out typeFilter,()=>{type=type==""?"Monster":type=="Monster"?"Spell":"";RenderLibrary();});
            this.BindButton("ArchetypeFilter",out archetypeFilter,()=>{archetype=Cycle(archetype,app.Library.Cards.SelectMany(c=>c.Archetypes));RenderLibrary();});
            this.BindButton("RarityFilter",out rarityFilter,()=>{rarity=Cycle(rarity,app.Library.Cards.Select(c=>c.Rarity));RenderLibrary();});
            NameInput.onValueChanged.AddListener(value=>{if(Session.Draft!=null) {Session.Draft.Name=value;RenderStatus();}});
            Search.onValueChanged.AddListener(_=>RenderLibrary());DeckList.onValueChanged.AddListener(index=>{if(index>=listed.Count) return;string id=listed[index].DeckId;Guard(()=>Select(id));});
            Session.Begin(app.Decks.Selected);Refresh();status.text=app.Error+app.Decks.Warning;
            Application.wantsToQuit+=WantsQuit;
        }
        void Bind(string name,UnityEngine.Events.UnityAction action) {this.BindButton(name,out Button _,action);}
        static string Cycle(string current,IEnumerable<string> values) {var all=new[]{""}.Concat(values.Distinct().OrderBy(v=>v)).ToArray();return all[(Array.IndexOf(all,current)+1)%all.Length];}
        bool WantsQuit() {if(allowQuit || !Session.Dirty) return true;Guard(()=>{allowQuit=true;Application.Quit();});return false;}
        void OnDestroy() {Application.wantsToQuit-=WantsQuit;}
        public void Guard(Action next)
        {
            if(!Session.Dirty) {next();return;}
            // Restore the persisted dropdown selection while a navigation decision is pending.
            DeckList.SetValueWithoutNotify(Math.Max(0,listed.FindIndex(d=>d.DeckId==Session.Draft.DeckId)));
            Dialog.Show("저장하지 않은 변경","변경한 덱을 저장한 뒤 이동할까요? 미완성 덱도 저장할 수 있습니다.","저장 후 이동",()=>{if(Save()) next();},"버리고 이동",()=>{Session.Cancel();next();});
        }
        public bool Save()
        {
            if(Session.Draft==null) {status.text="덱을 먼저 만드세요.";return false;}
            if(!app.Decks.Save(Session.Draft,out var error)) {status.text=error;return false;}
            Session.Begin(app.Decks.Selected);Refresh();status.text="개인 덱을 저장했습니다. 온라인 서버에는 전송하지 않습니다.";return true;
        }
        void Select(string id)
        {if(!app.Decks.Select(id,out var error)) {status.text=error;return;}Session.Begin(app.Decks.Selected);Refresh();}
        void Create()
        {var deck=new PersonalDeck();if(app.Decks.Save(deck,out var error)) {Session.Begin(app.Decks.Selected);Refresh();}else status.text=error;}
        void Duplicate()
        {
            if(Session.Draft==null) return;
            var copy=Session.Draft.Copy();copy.DeckId=Guid.NewGuid().ToString("N");copy.Name=(copy.Name.Length>55?copy.Name.Substring(0,55):copy.Name)+" 사본";
            if(app.Decks.Save(copy,out var error)) {Session.Begin(app.Decks.Selected);Refresh();}else status.text=error;
        }
        void ImportStarter()
        {
            if(app.Starter==null) {status.text=app.Error;return;}
            var deck=app.Starter.Copy();deck.DeckId=Guid.NewGuid().ToString("N");
            if(app.Decks.Save(deck,out var error)) {Session.Begin(app.Decks.Selected);Refresh();}else status.text=error;
        }
        void Delete()
        {
            if(Session.Draft==null) return;string id=Session.Draft.DeckId;
            Dialog.Show("덱 삭제",Session.Draft.Name+" 덱을 삭제할까요? 저장하지 않은 변경도 삭제됩니다.","삭제",()=>{
                if(app.Decks.Delete(id,out var error)) {Session.Begin(app.Decks.Selected);Refresh();}else status.text=error;});
        }
        public void Add(int id)
        {
            if(Session.Draft==null) {status.text="새 덱을 만들거나 기본 덱을 복사하세요.";return;}
            string reason=DeckValidator.CannotAdd(Session.Draft,app.Library,id);if(reason!=null) {status.text=reason;return;}
            Session.Draft.Cards.Add(id);RefreshCards();status.text="카드를 추가했습니다.";
        }
        public void Remove(int id) {if(Session.Draft?.Cards.Remove(id)==true) {RefreshCards();status.text="카드를 제거했습니다.";}}
        void Refresh()
        {
            listed=app.Decks.Decks.ToList();DeckList.ClearOptions();DeckList.AddOptions(listed.Count==0?new List<string>{"저장된 덱 없음"}:listed.Select(d=>d.Name+" / "+d.Cards.Count+"장").ToList());
            DeckList.SetValueWithoutNotify(Math.Max(0,listed.FindIndex(d=>d.DeckId==Session.Draft?.DeckId)));DeckList.interactable=listed.Count>0;
            NameInput.SetTextWithoutNotify(Session.Draft?.Name??"");NameInput.interactable=Session.Draft!=null;RefreshCards();
        }
        void RefreshCards()
        {
            int i=0;
            if(Session.Draft!=null) foreach(var group in Session.Draft.Cards.GroupBy(id=>id).OrderBy(g=>g.Key))
            {var tile=Tile(deckTiles,DeckContent,i++);int id=group.Key;tile.Render(id,app.Library.Find(id),group.Count(),false,()=>Remove(id),()=>Details(id));}
            for(;i<deckTiles.Count;++i) deckTiles[i].gameObject.SetActive(false);
            RenderLibrary();RenderStatus();
        }
        DeckTileView Tile(List<DeckTileView> tiles,Transform parent,int index)
        {if(index==tiles.Count) tiles.Add(Instantiate(TilePrefab,parent));tiles[index].gameObject.SetActive(true);return tiles[index];}
        void RenderLibrary()
        {
            typeFilter.GetComponentInChildren<Text>().text=type==""?"종류: 전체":type=="Monster"?"종류: 몬스터":"종류: 주문";
            archetypeFilter.GetComponentInChildren<Text>().text=archetype==""?"카드군: 전체":archetype;
            rarityFilter.GetComponentInChildren<Text>().text=rarity==""?"희귀도: 전체":rarity;
            int i=0;foreach(var card in app.Library.Filter(Search.text.Trim(),type,archetype,rarity))
            {var tile=Tile(libraryTiles,LibraryContent,i++);int id=card.Id;tile.Render(id,card,Session.Draft?.Cards.Count(c=>c==id)??0,true,()=>Add(id),()=>Details(id));}
            for(;i<libraryTiles.Count;++i) libraryTiles[i].gameObject.SetActive(false);
        }
        void RenderStatus()
        {count.text=(Session.Draft==null?"덱 없음":Session.Draft.Cards.Count+" / 30장"+(Session.Dirty?" · 미저장":" · 저장됨"))+"\n"+DeckValidator.Validate(Session.Draft,app.Library).Summary;}
        void Details(int id)
        {
            var card=app.Library.Find(id);detailName.text=card?.Name??"누락 카드 #"+id;
            detailCrest.text=card?.Type=="Monster"?"妖":"呪";
            detailStats.text=card==null?"정의가 없어 대전에 사용할 수 없습니다.":card.Type+" / "+card.Rarity+"\n"+(card.Type=="Monster"?$"ATK {card.Attack}   HP {card.MaxHp}":"일반 주문")+"\n"+(card.MainDeckEligible?"메인 덱 투입 가능":"현현 전용 · 메인 덱 투입 불가");
            detailRules.text=card?.Description??"중앙 목록에서 클릭하면 제거할 수 있습니다.";
        }
    }
}
