using System;
using UnityEngine;

namespace ProjectC.Client
{
    public enum SoundCue
    {
        CardSelect,
        CardDeselect,
        Draw,
        CardUse,
        Summon,
        Attack,
        Hit,
        Destroy,
        Transform,
        Effect,
        TurnStart,
        ButtonClick,
        Victory,
        Defeat,
    }

    [Serializable]
    public sealed class SoundDefinition
    {
        public SoundCue Cue;
        public AudioClip Clip;
        [Range(0, 1)] public float Gain = .6f;
        [Range(.5f, 1.5f)] public float Pitch = 1;
        [Min(0)] public float Cooldown = .1f;
    }

    [CreateAssetMenu(menuName = "Project C/Sound library")]
    public sealed class SoundLibrary : ScriptableObject
    {
        public SoundDefinition[] Sounds = Array.Empty<SoundDefinition>();
    }
}
