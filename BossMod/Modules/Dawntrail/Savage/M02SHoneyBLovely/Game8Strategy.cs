namespace BossMod.Dawntrail.Savage.M02SHoneyBLovely;

// Game8 (game8.jp/ff14/630353): venom spread on the base grid, venom pairs MT/D3 NW, ST/D4 NE, H1/D1 SW, H2/D2 SE; poison drop TH NW, DPS SE.
// ponytail: Live 1-3 hearts/towers and Rotten Heart pairing are left to plain pathfinding; add when a replay shows the AI taking the wrong tower.
sealed class Game8Strategy(BossModule module) : Game8Positions(module, static c => c.M2S)
{
    protected override WPos? Destination(int slot, Actor actor, PartyRolesConfig.Assignment a)
    {
        var center = Arena.Center;
        if (Module.FindComponent<PoisonStingBait>() is var poison && poison != null && poison.CurrentBaits.Exists(b => b.Target == actor))
            return At(center, IsSupport(a) ? NW : SE, 16f);

        if (Module.FindComponent<DropSplashOfVenom>() is var venom && venom != null && (venom.Spreads.Count != 0 || venom.Stacks.Count != 0))
        {
            // donut: hug the boss; line: go out
            var inside = Active(Module.FindComponent<TemptingTwist>(), slot, actor) || Active(Module.FindComponent<TemptingTwistBeat>(), slot, actor);
            var outside = Active(Module.FindComponent<HoneyBeeline>(), slot, actor) || Active(Module.FindComponent<HoneyBeelineBeat>(), slot, actor);
            var radius = inside ? 4f : outside ? 12f : 8f;
            if (venom.Spreads.Count != 0)
                return At(center, BaseSpread(a), radius);
            var pair = a switch
            {
                PartyRolesConfig.Assignment.MT or PartyRolesConfig.Assignment.R1 => NW,
                PartyRolesConfig.Assignment.OT or PartyRolesConfig.Assignment.R2 => NE,
                PartyRolesConfig.Assignment.H1 or PartyRolesConfig.Assignment.M1 => SW,
                _ => SE
            };
            return At(center, pair, radius);
        }
        return null;
    }
}
