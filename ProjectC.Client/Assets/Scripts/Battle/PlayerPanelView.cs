using System;
using System.Collections.Generic;
using System.Linq;
using Game.Utils;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Newtonsoft.Json.Linq;

namespace ProjectC.Client
{
    public sealed class PlayerPanelView : MonoBehaviour
    {
        public TMP_Text Life;
        private bool _bound, _own;
        private RectTransform _resourceContainer;
        private readonly List<ResourceRow> _rows = new List<ResourceRow>();

        private sealed class ResourceRow
        {
            public RectTransform Root;
            public TMP_Text Label;
            public readonly List<Image> Segments = new List<Image>();
        }

        public void Render(PlayerView player, bool own, bool active, int maxYokai = 0)
        {
            if (!_bound)
            {
                if (!this.Bind("LifeText", out Life))
                    throw new InvalidOperationException("Player panel requires its live health text.");
                BattleText.StyleValue(Life);
                _bound = true;
            }
            _own = own;
            Life.text = player.Life.ToString();
        }

        private static RectTransform Rect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            go.layer = parent.gameObject.layer;
            return rect;
        }

        private static void Place(RectTransform rect, Vector2 min, Vector2 max, Vector2 offsetMin, Vector2 offsetMax)
        {
            rect.anchorMin = min;
            rect.anchorMax = max;
            rect.offsetMin = offsetMin;
            rect.offsetMax = offsetMax;
        }

        private static Image ImageAt(string name, Transform parent, Color color)
        {
            var rect = Rect(name, parent);
            var image = rect.gameObject.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        private static Color ResourceColor(string id)
        {
            switch (id)
            {
                case "Ki": return new Color(.48f, .76f, .68f);
                case "Spirit": return new Color(.42f, .67f, .88f);
                case "Blood": return new Color(.77f, .27f, .34f);
                default: return new Color(.80f, .68f, .40f);
            }
        }

        private ResourceRow CreateRow()
        {
            var root = Rect("ResourceRow", _resourceContainer);
            var rim = root.gameObject.AddComponent<Image>();
            rim.color = new Color(.65f, .48f, .30f, .92f);
            rim.raycastTarget = false;
            var bg = ImageAt("Backing", root, new Color(.10f, .085f, .12f, .97f));
            Place(bg.rectTransform, Vector2.zero, Vector2.one, new Vector2(1,1), new Vector2(-1,-1));
            var labelRect = Rect("NameAndAmount", root);
            Place(labelRect, new Vector2(0, .34f), Vector2.one, new Vector2(7,0), new Vector2(-7,-1));
            var label = labelRect.gameObject.AddComponent<TextMeshProUGUI>();
            label.font = Life.font;
            label.fontSize = 18;
            label.enableAutoSizing = true;
            label.fontSizeMin = 10;
            label.fontSizeMax = 18;
            label.alignment = TextAlignmentOptions.Center;
            label.color = new Color(.98f,.88f,.70f);
            label.raycastTarget = false;
            BattleText.StyleHud(label);
            return new ResourceRow { Root = root, Label = label };
        }

        public void RenderResources(JObject resources)
        {
            if (resources == null || !resources.HasValues)
            {
                if (_resourceContainer != null) _resourceContainer.gameObject.SetActive(false);
                return;
            }
            if (!_bound) return;
            if (_resourceContainer == null)
                _resourceContainer = Rect("ResourceContainer", transform);
            // Attach to the portrait panel, not a separate screen-space position.
            // Own panel has a taller authored portrait than the opponent panel.
            _resourceContainer.anchorMin = _resourceContainer.anchorMax = new Vector2(.5f,0);
            _resourceContainer.pivot = new Vector2(.5f,0);
            _resourceContainer.anchoredPosition = new Vector2(_own ? 7 : -4, _own ? 15 : 6);
            var entries = resources.Properties().OrderBy(p => p.Name, StringComparer.Ordinal).Take(2).ToArray();
            _resourceContainer.sizeDelta = new Vector2(_own ? 184 : 154, entries.Length * 34 + (entries.Length-1)*3);
            _resourceContainer.gameObject.SetActive(true);
            while (_rows.Count < entries.Length) _rows.Add(CreateRow());
            for (int i=0; i<_rows.Count; ++i)
            {
                var row = _rows[i];
                row.Root.gameObject.SetActive(i<entries.Length);
                if(i>=entries.Length) continue;
                var entry=entries[i];
                int max=Mathf.Max(1,(int?)entry.Value["Max"] ?? 1);
                int current=Mathf.Clamp((int?)entry.Value["Current"] ?? 0,0,max);
                row.Root.name="Resource_"+entry.Name;
                row.Root.anchorMin=row.Root.anchorMax=new Vector2(0,1);
                row.Root.pivot=new Vector2(0,1);
                row.Root.anchoredPosition=new Vector2(0,-i*37);
                row.Root.sizeDelta=new Vector2(_resourceContainer.sizeDelta.x,34);
                row.Label.text=YakouUiText.ResourceName(entry.Name)+"  "+current+"/"+max;
                // Discrete slots for small pools; normalized gauge for larger pools.
                int slots=max<=10?max:1;
                while(row.Segments.Count<slots)
                    row.Segments.Add(ImageAt("Segment",row.Root,Color.white));
                for(int j=0;j<row.Segments.Count;++j)
                {
                    var image=row.Segments[j];
                    image.gameObject.SetActive(j<slots);
                    if(j>=slots) continue;
                    float fraction=slots==1?(float)current/max:(current>j?1:0);
                    image.color=fraction>0?ResourceColor(entry.Name):new Color(.24f,.22f,.28f);
                    float left=(float)j/slots;
                    float right=(float)(j+1)/slots;
                    if(slots==1)right=Mathf.Max(.01f,fraction);
                    Place(image.rectTransform,new Vector2(left,0),new Vector2(right,.32f),
                        new Vector2(j==0?6:2,4),new Vector2(j==slots-1?-6:-2,-1));
                }
            }
        }
    }
}
