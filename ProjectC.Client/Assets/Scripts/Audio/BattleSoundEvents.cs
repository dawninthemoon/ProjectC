using System;

namespace ProjectC.Client
{
    internal static class BattleSoundEvents
    {
        // Existing protocol-v2 values. Sound observes accepted presentation events only.
        private enum Kind
        {
            Attack = 0, Damage = 1, Destroy = 2, Life = 8, PlayerDamage = 10,
            Summon = 11, Effect = 13, Transform = 15, Spell = 19,
        }

        public static void Present(EventView value, StateView before)
        {
            switch ((Kind)value.Type)
            {
                case Kind.Attack:
                    ClientAudio.Play(SoundCue.Attack);
                    break;
                case Kind.PlayerDamage:
                    // Direct attacks emit PlayerDamaged without BattleStarted.
                    ClientAudio.Play(SoundCue.Attack);
                    if (value.Damage > 0)
                    {
                        ClientAudio.Play(SoundCue.Hit);
                    }
                    break;
                case Kind.Damage:
                    if (value.Damage > 0)
                    {
                        ClientAudio.Play(SoundCue.Hit);
                    }
                    break;
                case Kind.Life:
                    var player = before?.Players == null ? null : Array.Find(before.Players, p => p.PlayerId == value.TargetPlayerId);
                    // PlayerDamaged already updated the projection for a direct attack;
                    // its following LifeChanged must not play the same impact twice.
                    if (player != null && value.LifeAfter < player.Life)
                    {
                        ClientAudio.Play(SoundCue.Hit);
                    }
                    break;
                case Kind.Destroy:
                    ClientAudio.Play(SoundCue.Destroy);
                    break;
                case Kind.Summon:
                    ClientAudio.Play(SoundCue.Summon);
                    break;
                case Kind.Spell:
                    ClientAudio.Play(SoundCue.CardUse);
                    break;
                case Kind.Effect:
                    ClientAudio.Play(SoundCue.Effect);
                    break;
                case Kind.Transform:
                    ClientAudio.Play(SoundCue.Transform);
                    break;
            }
        }
    }
}
