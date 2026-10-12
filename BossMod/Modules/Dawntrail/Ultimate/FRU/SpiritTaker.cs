namespace BossMod.Dawntrail.Ultimate.FRU;

abstract class SpiritTaker(BossModule module) : Components.GenericStackSpread(module)
{
    public const float Radius = 5f;

    public override void AddAIHints(int slot, Actor actor, PartyRolesConfig.Assignment assignment, AIHints hints)
    {
        foreach (var spread in ActiveSpreads.Where(s => s.Target != actor))
            hints.AddForbiddenZone(new SDCapsule(spread.Target.Position, spread.Target.LastFrameMovement.Normalized(), 2, spread.Radius + 1f), spread.Activation);
    }

    public override void OnCastStarted(Actor caster, ActorCastInfo spell)
    {
        if (spell.Action.ID == (uint)AID.SpiritTaker)
        {
            var activation = Module.CastFinishAt(spell, 0.3d);
            foreach (var (i, p) in Raid.WithSlot(true, true, true))
            {
                Spreads.Add(new(p, Radius, activation));
            }
            // Fragment is an encounter actor, not necessarily a party NPC. P3 has none.
            foreach (var fragment in Module.Enemies((uint)OID.FragmentOfFate))
                if (!fragment.IsDeadOrDestroyed)
                    Spreads.Add(new(fragment, Radius + fragment.HitboxRadius, activation));
        }
    }

    public override void OnEventCast(Actor caster, ActorCastEvent spell)
    {
        if (spell.Action.ID == (uint)AID.SpiritTakerAOE)
            Spreads.Clear();
    }
}
