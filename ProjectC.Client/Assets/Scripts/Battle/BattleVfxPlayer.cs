using UnityEngine;

namespace ProjectC.Client
{
    // Bounded UI VFX pool shared by presentation cues; no gameplay state lives here.
    public sealed class BattleVfxPlayer : MonoBehaviour
    {
        private const int PoolCapacity = 4;
        [SerializeField] private BattleVfxCue _summonPrefab;
        private readonly BattleVfxCue[] _pool = new BattleVfxCue[PoolCapacity];
        private bool _initialized;

        public int ActiveCount
        {
            get
            {
                int count = 0;
                foreach (var cue in _pool)
                {
                    if (cue != null && cue.gameObject.activeSelf)
                    {
                        ++count;
                    }
                }
                return count;
            }
        }

        public BattleVfxCue AcquireSummon()
        {
            if (!_initialized)
            {
                _initialized = true;
                if (_summonPrefab == null)
                {
                    Debug.LogWarning("Summon VFX prefab is missing; card reveal will continue without particles.", this);
                    return null;
                }
                for (int i = 0; i < PoolCapacity; ++i)
                {
                    _pool[i] = Instantiate(_summonPrefab, transform);
                    _pool[i].Initialize();
                    _pool[i].gameObject.SetActive(false);
                }
            }
            foreach (var cue in _pool)
            {
                if (cue != null && !cue.gameObject.activeSelf)
                {
                    cue.gameObject.SetActive(true);
                    return cue;
                }
            }
            return null;
        }

        public void Release(BattleVfxCue cue)
        {
            if (cue != null)
            {
                cue.gameObject.SetActive(false);
            }
        }

        public void ResetEffects()
        {
            foreach (var cue in _pool)
            {
                Release(cue);
            }
        }

        private void OnDisable()
        {
            ResetEffects();
        }
    }
}
