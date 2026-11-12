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
        private EffectSelectionView _selection;
        private ActionChoiceView _serverChoice;
        private Button _confirm;
        private CardGalleryView _gallery;
        private Button _cancel;
        private bool _submitting;
        private Action _confirmAction, _cancelAction;

        public void ConfirmSelection() => _confirmAction?.Invoke();
        public void CancelSelection() => _cancelAction?.Invoke();

        private void Begin(bool canCancel, Action confirm, Action cancel)
        {
            this.Bind("TitleText", out TMP_Text title);
            this.Bind("CandidatesImage", out RectTransform content);
            this.Bind("ConfirmButton", out _confirm);
            this.Bind("CancelButton", out Button back);
            _cancel = back;
            _submitting = false;
            _confirmAction = confirm;
            _cancelAction = cancel;
            title.gameObject.SetActive(false);
            // Cards and heroes are selected directly; private-zone cards use the gallery.
            // Preserve the authored hierarchy without displaying a duplicate text-button list.
            content.parent.gameObject.SetActive(false);
            _confirm.gameObject.SetActive(false);
            _confirm.onClick.RemoveAllListeners();
            _confirm.onClick.AddListener(() => confirm());
            _confirm.GetComponentInChildren<TMP_Text>().text = BattleText.Get("ui.choice.confirm");
            back.gameObject.SetActive(false);
            back.interactable = canCancel;
            back.onClick.RemoveAllListeners();
            back.onClick.AddListener(() => cancel());
            back.GetComponentInChildren<TMP_Text>().text = BattleText.Get("ui.choice.cancel");
            gameObject.SetActive(true);
        }

        public void ShowServer(ActionChoiceView value, Action<string> pick, Action confirm, Action cancel)
        {
            _gallery = GetComponentInParent<BattlePresenter>().GetComponentInChildren<CardGalleryView>(true);
            _serverChoice = value;
            _selection = null;
            Begin(value.CanCancel, confirm, cancel);
            _gallery.ShowChoices(value, pick);
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
            Begin(true, confirm, cancel);
            _gallery.ShowTargets(value, pick);
        }

        public void SetServerSelected(List<string> selected)
        {
            _gallery?.SetSelected(selected);
            _confirm.interactable = !_submitting && selected.Count >= _serverChoice.MinCount && selected.Count <= _serverChoice.MaxCount;
        }

        public void SetSelected(List<EffectTargetView> selected)
        {
            var keys = selected.ConvertAll(CardGalleryView.TargetKey);
            _gallery?.SetSelected(keys);
            _confirm.interactable = !_submitting && selected.Count >= _selection.MinCount && selected.Count <= _selection.MaxCount;
        }

        public void SetSubmitting(bool submitting)
        {
            _submitting = submitting;
            _gallery?.SelectionBar.SetSubmitting(submitting);
            if (_confirm != null && submitting)
            {
                _confirm.interactable = false;
            }
            if (_cancel != null)
            {
                _cancel.interactable = !submitting && (_serverChoice == null || _serverChoice.CanCancel);
            }
        }
    }
}
