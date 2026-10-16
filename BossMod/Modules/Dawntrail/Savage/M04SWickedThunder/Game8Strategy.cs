namespace BossMod.Dawntrail.Savage.M04SWickedThunder;

// Game8 (game8.jp/ff14/631136 macro Ver2.1, 631340): Wicked Blaze MT group W / ST group E;
// Midnight Sabbath spread octagon (MT D2 / D1 ST / H1 D4 / D3 H2 from the north); Sword Quiver pairs MT/D1 NW, ST/D2 NE, H1/D3 SW, H2/D4 SE.
// ponytail: Witch Hunt in/out table, Electrope Edge counts, Ion Cluster, Transplant, Mustard Bomb passing and Sunrise are left to plain pathfinding;
// add when a replay shows the AI going wrong there.
sealed class Game8Strategy(BossModule module) : Game8Positions(module, static c => c.M4S)
{
    protected override WPos? Destination(int slot, Actor actor, PartyRolesConfig.Assignment a)
    {
        var center = Arena.Center;

        if (Module.FindComponent<WickedBlaze>() is var blaze && blaze != null && blaze.Stacks.Count != 0)
        {
            var mtGroup = a is PartyRolesConfig.Assignment.MT or PartyRolesConfig.Assignment.H1 or PartyRolesConfig.Assignment.M1 or PartyRolesConfig.Assignment.R1;
            return At(center, mtGroup ? W : E, 10f);
        }

        if (Module.FindComponent<ConcentratedScatteredBurst>() is var burst && burst != null && burst.Spreads.Count != 0)
        {
            var dir = a switch
            {
                PartyRolesConfig.Assignment.MT => -157.5f,
                PartyRolesConfig.Assignment.M2 => 157.5f,
                PartyRolesConfig.Assignment.OT => 112.5f,
                PartyRolesConfig.Assignment.R2 => 67.5f,
                PartyRolesConfig.Assignment.H2 => 22.5f,
                PartyRolesConfig.Assignment.R1 => -22.5f,
                PartyRolesConfig.Assignment.H1 => -67.5f,
                _ => -112.5f // D1
            };
            return At(center, dir, 10f);
        }

        if (Active(Module.FindComponent<SwordQuiverBurst>(), slot, actor))
        {
            var dir = a switch
            {
                PartyRolesConfig.Assignment.MT or PartyRolesConfig.Assignment.M1 => NW,
                PartyRolesConfig.Assignment.OT or PartyRolesConfig.Assignment.M2 => NE,
                PartyRolesConfig.Assignment.H1 or PartyRolesConfig.Assignment.R1 => SW,
                _ => SE
            };
            return At(center, dir, 8f);
        }
        return null;
    }
}
