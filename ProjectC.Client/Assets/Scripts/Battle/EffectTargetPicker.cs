using System;
using System.Collections.Generic;
using Game.Utils;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

namespace ProjectC.Client
{
    public sealed class EffectTargetPicker : MonoBehaviour, IPointerClickHandler
    {
        public void OnPointerClick(PointerEventData eventData)
        {
            // Keep choice-panel clicks separate from battlefield background clicks.
        }

        public Button CandidatePrefab;
        private readonly List<Button> _buttons = new List<Button>();
        private EffectSelectionView _selection;
        private ActionChoiceView _serverChoice;
        private Button _confirm;
        private CardGalleryView _gallery;

        private void Begin(int count, int minimum, int maximum, bool cost, bool canCancel, Action confirm, Action cancel)
        {
            this.Bind("Title", out TMP_Text title);
            this.Bind("Candidates", out RectTransform content);
            this.Bind("Confirm", out _confirm);
            this.Bind("Cancel", out Button back);
            title.text = BattleText.Get(cost ? "ui.choice.cost" : "ui.choice.targets", minimum, maximum);
            title.color = cost ? new Color(1, .7f, .4f) : Color.white;
            while (_buttons.Count < count)
            {
                _buttons.Add(Instantiate(CandidatePrefab, content));
            }
            for (int i = 0; i < _buttons.Count; ++i)
            {
                _buttons[i].gameObject.SetActive(i < count);
                _buttons[i].onClick.RemoveAllListeners();
            }
            _confirm.onClick.RemoveAllListeners();
            _confirm.onClick.AddListener(() => confirm());
            _confirm.GetComponentInChildren<TMP_Text>().text = BattleText.Get("ui.choice.confirm");
            back.interactable = canCancel;
            back.onClick.RemoveAllListeners();
            back.onClick.AddListener(() => cancel());
            back.GetComponentInChildren<TMP_Text>().text = BattleText.Get(canCancel ? "ui.choice.cancel" : "ui.choice.required");
            gameObject.SetActive(true);
        }

        public void ShowServer(ActionChoiceView value, Action<string> pick, Action confirm, Action cancel)
        {
            _gallery = GetComponentInParent<BattlePresenter>().GetComponentInChildren<CardGalleryView>(true);
            _serverChoice = value;
            _selection = null;
            Begin(value.Candidates.Length, value.MinCount, value.MaxCount, value.IsCost, value.CanCancel, confirm, cancel);
            for (int i = 0; i < value.Candidates.Length; ++i)
            {
                var option = value.Candidates[i];
                _buttons[i].gameObject.SetActive(option.CardId == 0);
                _buttons[i].GetComponentInChildren<TMP_Text>().text = CardPresentation.Name(option.Card,
                    BattleText.Get("ui.choice.player", option.PlayerId));
                _buttons[i].onClick.AddListener(() => pick(option.Token));
            }
            if (Array.Exists(value.Candidates, option => option.Card != null && option.Zone != (int)PresentationZone.Field))
            {
                _gallery.ShowChoices(value, pick);
            }
        }

        private void OnDisable()
        {
            if (_gallery != null && _gallery.Choosing)
            {
                _gallery.Close();
            }
        }

        public void Show(EffectSelectionView value, int index, Func<EffectTargetView, string> name,
            Action<EffectTargetView> pick, Action confirm, Action cancel)
        {
            var presenter = GetComponentInParent<BattlePresenter>();
            _gallery = presenter.GetComponentInChildren<CardGalleryView>(true);
            _selection = value;
            _serverChoice = null;
            Begin(value.Candidates.Length, value.MinCount, value.MaxCount, false, true, confirm, cancel);
            for (int i = 0; i < value.Candidates.Length; ++i)
            {
                var target = value.Candidates[i];
                _buttons[i].gameObject.SetActive(target.CardId == 0);
                _buttons[i].GetComponentInChildren<TMP_Text>().text = name(target);
                _buttons[i].onClick.AddListener(() => pick(target));
            }
            if (Array.Exists(value.Candidates, target => target.CardId != 0 && target.Zone != (int)PresentationZone.Field))
            {
                _gallery.ShowTargets(value, pick);
            }
        }

        private void Highlight(int index, bool selected)
        {
            _buttons[index].GetComponent<Image>().color = selected
                ? new Color(.23f, .46f, .36f) : new Color(.1f, .18f, .24f);
        }

        public void SetServerSelected(List<string> selected)
        {
            _gallery?.SetSelected(selected);
            _confirm.interactable = selected.Count >= _serverChoice.MinCount && selected.Count <= _serverChoice.MaxCount;
            for (int i = 0; i < _serverChoice.Candidates.Length; ++i)
            {
                Highlight(i, selected.Contains(_serverChoice.Candidates[i].Token));
            }
        }

        public void SetSelected(List<EffectTargetView> selected)
        {
            var keys = selected.ConvertAll(CardGalleryView.TargetKey);
            _gallery?.SetSelected(keys);
            _confirm.interactable = selected.Count >= _selection.MinCount && selected.Count <= _selection.MaxCount;
            for (int i = 0; i < _selection.Candidates.Length; ++i)
            {
                var target = _selection.Candidates[i];
                Highlight(i, selected.Exists(t => t.CardId == target.CardId
                    && t.PlayerId == target.PlayerId && t.Zone == target.Zone));
            }
        }
    }
}
