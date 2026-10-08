using UnityEngine;

namespace BadAppleHotel.Game
{
    public partial class GameManager
    {
        public float PersonalShotCooldown(Resident resident) =>
            resident == null ? 0f : Mathf.Max(0f, resident.NextPersonalShotAt - Now);

        public ActionResult ResidentShotAvailability(Resident resident)
        {
            if (Phase != Phase.Night || HumanRole != Role.Resident || resident == null ||
                resident != Human || !resident.IsHuman || !resident.Alive || resident.IsMonster)
                return ActionResult.Invalid;
            if (resident.Asleep || PersonalShotCooldown(resident) > 0f) return ActionResult.Blocked;
            var monster = Monster;
            if (monster == null || monster.Dead || Now < monster.CloakUntil ||
                !CanSee(resident.Pos, monster.Pos, Cfg.residents.personalShotRangeTiles) ||
                !ClearLine(resident.Pos, monster.Pos))
                return ActionResult.TooFar;
            return ActionResult.Ok;
        }

        /// <summary>One aimed shot per explicit player press. Towers never call this action.</summary>
        public ActionResult TryResidentShoot(Resident resident)
        {
            var result = ResidentShotAvailability(resident);
            if (result != ActionResult.Ok) return result;
            var monster = Monster;
            resident.NextPersonalShotAt = Now + Mathf.Max(.05f, Cfg.residents.personalShotCooldownSeconds);
            resident.AttackUntil = Now + .45f;
            Vector2 direction = monster.Pos - resident.Pos;
            if (direction.sqrMagnitude > .0001f) resident.Facing = direction.normalized;
            SpawnProjectile(resident.Pos, monster.Pos + Vector2.up * .5f, "bullet");
            float damage = Cfg.residents.personalShotDamage * DamageTaken(monster, DamageTypes.Bullet);
            if (resident.IsHuman) LearnDamageType(DamageTypes.Bullet);
            if (Now < monster.JamUntil && direction.magnitude <= monster.JamRadius) damage *= monster.JamValue;
            DamageMonster(damage, resident);
            return ActionResult.Ok;
        }
    }
}
