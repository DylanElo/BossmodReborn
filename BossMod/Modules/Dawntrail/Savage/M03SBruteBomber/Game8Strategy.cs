namespace BossMod.Dawntrail.Savage.M03SBruteBomber;

// Game8 (game8.jp/ff14/630870, macro Ver.2.0):
// Bombarian Special 8: base grid; 4: pair X MT/D1 NW, ST/D2 NE, H1/D3 SW, H2/D4 SE.
// Lariat 8-way: base grid around the boss; 4-way: MT/D3 NW, H2/D4 NE, H1/D1 SW, ST/D2 SE.
// Infernal Spin (attach): D3 MT D4 / D1 ☆ D2 / H1 ST H2. Knockback tower 1: melee NW, ranged SE.
// ponytail: Fuse or Foe, Fusefield order, Diveboom 8/4 shock and towers 2-3 are left to plain pathfinding; add when a replay shows them going wrong.
sealed class Game8Strategy(BossModule module) : Game8Positions(module, static c => c.M3S)
{
    private Actor? _lariat;
    private bool _lariatPairs, _lariatIn;

    public override void OnCastStarted(Actor caster, ActorCastInfo spell)
    {
        if (spell.Action.ID is (uint)AID.OctupleLariatIn or (uint)AID.OctupleLariatOut or (uint)AID.QuadrupleLariatIn or (uint)AID.QuadrupleLariatOut)
        {
            _lariat = caster;
            _lariatPairs = spell.Action.ID is (uint)AID.QuadrupleLariatIn or (uint)AID.QuadrupleLariatOut;
            _lariatIn = spell.Action.ID is (uint)AID.OctupleLariatIn or (uint)AID.QuadrupleLariatIn;
        }
    }

    public override void OnEventCast(Actor caster, ActorCastEvent spell)
    {
        if (spell.Action.ID is (uint)AID.BlazingLariatSpread or (uint)AID.BlazingLariatPair)
            _lariat = null;
    }

    protected override WPos? Destination(int slot, Actor actor, PartyRolesConfig.Assignment a)
    {
        var center = Arena.Center;

        if (_lariat != null)
        {
            var dir = !_lariatPairs ? BaseSpread(a) : a switch
            {
                PartyRolesConfig.Assignment.MT or PartyRolesConfig.Assignment.R1 => NW,
                PartyRolesConfig.Assignment.H2 or PartyRolesConfig.Assignment.R2 => NE,
                PartyRolesConfig.Assignment.H1 or PartyRolesConfig.Assignment.M1 => SW,
                _ => SE
            };
            return At(_lariat.Position, dir, _lariatIn ? 5f : 13f);
        }

        if (Module.FindComponent<BombarianSpecial>() is var special && special != null && (special.Spreads.Count != 0 || special.Stacks.Count != 0))
        {
            var inside = Active(Module.FindComponent<BombarianSpecialIn>(), slot, actor) || Active(Module.FindComponent<SpecialBombarianSpecialIn>(), slot, actor);
            var outside = Active(Module.FindComponent<BombarianSpecialOut>(), slot, actor) || Active(Module.FindComponent<SpecialBombarianSpecialOut>(), slot, actor);
            var radius = inside ? 4f : outside ? 12f : 8f;
            var dir = special.Spreads.Count != 0 ? BaseSpread(a) : a switch
            {
                PartyRolesConfig.Assignment.MT or PartyRolesConfig.Assignment.M1 => NW,
                PartyRolesConfig.Assignment.OT or PartyRolesConfig.Assignment.M2 => NE,
                PartyRolesConfig.Assignment.H1 or PartyRolesConfig.Assignment.R1 => SW,
                _ => SE
            };
            return At(center, dir, radius);
        }

        if (Module.FindComponent<ExplosiveRain>() is var rain && rain != null && rain.Sequences.Count != 0)
        {
            var dir = a switch
            {
                PartyRolesConfig.Assignment.MT => N,
                PartyRolesConfig.Assignment.R1 => NW,
                PartyRolesConfig.Assignment.R2 => NE,
                PartyRolesConfig.Assignment.M1 => W,
                PartyRolesConfig.Assignment.M2 => E,
                PartyRolesConfig.Assignment.H1 => SW,
                PartyRolesConfig.Assignment.H2 => SE,
                _ => S
            };
            return At(center, dir, 10f);
        }

        if (Module.FindComponent<BarbarousBarrageTowers>() is var towers && towers != null
            && towers.CurState is BarbarousBarrageTowers.State.NextNS or BarbarousBarrageTowers.State.NextEW)
        {
            var melee = a is PartyRolesConfig.Assignment.MT or PartyRolesConfig.Assignment.OT or PartyRolesConfig.Assignment.M1 or PartyRolesConfig.Assignment.M2;
            return At(center, melee ? NW : SE, 10f);
        }
        return null;
    }
}
