using System;
using System.Collections.Generic;
using System.Linq;

namespace ProjectC.Client
{
    [Serializable] public sealed class PersonalDeck
    {
        public string DeckId = Guid.NewGuid().ToString("N");
        public string Name = "새 덱";
        public List<int> Cards = new List<int>();
        public PersonalDeck Copy() => new PersonalDeck {DeckId=DeckId,Name=Name,Cards=new List<int>(Cards)};
        public bool SameContents(PersonalDeck other) => other!=null && Name==other.Name && Cards.SequenceEqual(other.Cards);
    }
    public sealed class DeckValidation
    {
        public readonly List<string> Errors = new List<string>();
        public bool CanPlay => Errors.Count==0;
        public string Summary => CanPlay?"구성 완료 · 30장 / 동일 카드 최대 3장":string.Join("\n",Errors);
    }
    public static class DeckValidator
    {
        public static DeckValidation Validate(PersonalDeck deck, CardLibrary library)
        {
            var result=new DeckValidation();
            if(deck==null) {result.Errors.Add("덱을 선택하세요.");return result;}
            if(deck.Cards.Count!=30) result.Errors.Add($"30장이 필요합니다. 현재 {deck.Cards.Count}장");
            foreach(var group in deck.Cards.GroupBy(id=>id))
            {
                var card=library.Find(group.Key);
                if(card==null) result.Errors.Add($"누락된 카드 정의 #{group.Key}");
                else if(!card.MainDeckEligible) result.Errors.Add(card.Name+": 현현 형태는 메인 덱에 넣을 수 없습니다.");
                if(group.Count()>3) result.Errors.Add((card?.Name??"#"+group.Key)+": 동일 카드가 3장을 초과했습니다.");
            }
            return result;
        }
        public static string CannotAdd(PersonalDeck deck, CardLibrary library, int id)
        {
            var card=library.Find(id);
            if(card==null) return "카드 정의를 찾을 수 없습니다.";
            if(!card.MainDeckEligible) return "현현 전용 카드입니다. 메인 덱에 넣을 수 없습니다.";
            if(deck.Cards.Count>=30) return "덱에는 최대 30장까지 추가할 수 있습니다.";
            if(deck.Cards.Count(c=>c==id)>=3) return "동일 카드는 최대 3장입니다.";
            return null;
        }
    }
    public sealed class DeckEditSession
    {
        PersonalDeck original;
        public PersonalDeck Draft {get;private set;}
        public bool Dirty => Draft!=null && !Draft.SameContents(original);
        public void Begin(PersonalDeck deck) {original=deck?.Copy();Draft=deck?.Copy();}
        public void Cancel() {Draft=original?.Copy();}
        public void Accept() {original=Draft?.Copy();}
    }
}
