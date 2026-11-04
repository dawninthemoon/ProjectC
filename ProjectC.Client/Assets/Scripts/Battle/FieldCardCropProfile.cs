using System;
using UnityEngine;

namespace ProjectC.Client
{
    // Presentation only: keyed by the server's definition ID, never sent as card data.
    [CreateAssetMenu(menuName = "Project C/Field card crop profile")]
    public sealed class FieldCardCropProfile : ScriptableObject
    {
        [Serializable]
        public struct Override
        {
            public int DefinitionId;
            [Min(1f)] public float Zoom;
            public Vector2 Offset;
        }

        [SerializeField, Min(1f)] private float _defaultZoom = 1.38f;
        [SerializeField] private Vector2 _defaultOffset = new Vector2(0, .12f);
        [SerializeField] private Override[] _overrides = Array.Empty<Override>();

        public void Resolve(int definitionId, out float zoom, out Vector2 offset)
        {
            zoom = _defaultZoom;
            offset = _defaultOffset;
            foreach (var item in _overrides)
            {
                if (item.DefinitionId == definitionId)
                {
                    zoom = Mathf.Max(1f, item.Zoom);
                    offset = item.Offset;
                    return;
                }
            }
        }
    }
}
