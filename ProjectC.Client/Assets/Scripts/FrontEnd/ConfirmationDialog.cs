using System;
using Game.Utils;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ProjectC.Client
{
    public sealed class ConfirmationDialog : MonoBehaviour
    {
        public void Show(
            string title,
            string message,
            string primary,
            Action confirm,
            string secondary = null,
            Action alternative = null
        )
        {
            this.Bind("DialogTitle", out TMP_Text heading);
            this.Bind("DialogMessage", out TMP_Text body);
            this.Bind("Confirm", out Button yes);
            this.Bind("Alternative", out Button other);
            this.Bind("Dismiss", out Button no);
            heading.text = title;
            body.text = message;
            yes.GetComponentInChildren<TMP_Text>().text = primary;
            yes.onClick.RemoveAllListeners();
            yes.onClick.AddListener(() =>
            {
                gameObject.SetActive(false);
                confirm();
            });
            other.gameObject.SetActive(secondary != null);
            other.GetComponentInChildren<TMP_Text>().text = secondary;
            other.onClick.RemoveAllListeners();
            if (alternative != null)
            {
                other.onClick.AddListener(() =>
                {
                    gameObject.SetActive(false);
                    alternative();
                });
            }
            no.onClick.RemoveAllListeners();
            no.onClick.AddListener(() => gameObject.SetActive(false));
            gameObject.SetActive(true);
        }
    }
}
