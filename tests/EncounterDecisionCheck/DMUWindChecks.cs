using BossMod;
using BossMod.Dawntrail.Ultimate.DMU;
using System.Numerics;

internal static partial class EncounterChecks
{
    static partial void CheckDMUWind()
    {
        foreach (var rotation in new[] { 0f, 90f, 180f, 270f })
            foreach (var mirror in new[] { false, true })
                foreach (var fireFirst in new[] { false, true })
                    CheckYarnCrystals(rotation, mirror, fireFirst);
        CheckYarnWindStack();
    }

    private static void CheckYarnCrystals(float rotation, bool mirror, bool fireFirst)
    {
        using var f = new Fixture("DMU");
        DMUParty(f);
        var config = Service.Config.Get<DMUConfig>();
        config.YarnPirenStrategy = true;
        f.Activate("BossMod.Dawntrail.Ultimate.DMU.Crystals");
        var center = f.Module.Center;
        var windDir = new WDir(1, -1).Normalized().Rotate(rotation.Degrees());
        var waterDir = new WDir(mirror ? 1 : -1, mirror ? 1 : -1).Normalized().Rotate(rotation.Degrees());
        var water = f.AddActor(0x200, (uint)OID.WaterP3, ActorType.EventObj, Class.None, center + waterDir * 14.142136f);
        var fire = f.AddActor(0x201, (uint)OID.FireP3, ActorType.EventObj, Class.None, center - waterDir * 14.142136f);
        f.AddActor(0x202, (uint)OID.WindP3, ActorType.EventObj, Class.None, center + windDir * 14.142136f);
        foreach (var slot in new[] { 0, 4 }) f.Status(f.World.Party[slot]!, 0, new((uint)SID.Entropy, 0, f.Now.AddSeconds(fireFirst ? 10 : 30), 0));
        foreach (var slot in new[] { 1, 5 }) f.Status(f.World.Party[slot]!, 0, new((uint)SID.DynamicFluid, 0, f.Now.AddSeconds(fireFirst ? 30 : 10), 0));
        var component = f.Activate("BossMod.Dawntrail.Ultimate.DMU." + (fireFirst ? "FireCrystal" : "WaterCrystal"));
        WPos[] expected = [center + windDir * 7, center + windDir * 7, center + windDir * 7, center + windDir * 7,
            center + windDir * 19.5f, center + windDir * 19.5f, center + waterDir * 16 - windDir * 7, center + waterDir * 16 + windDir * 7];
        for (var slot = 0; slot < 8; ++slot)
            f.World.Execute(new ActorState.OpMove(f.World.Party[slot]!.InstanceID, new Vector4(expected[slot].X, 0, expected[slot].Z, 0)));
        component.Update();
        // The live first Thunder III is drawn opposite the wind, as in Yarn diagram134.
        var exdeath = f.AddActor(0x203, (uint)OID.Exdeath, ActorType.Enemy, Class.None, center - windDir * 14.142136f);
        f.Activate("BossMod.Dawntrail.Ultimate.DMU.ThunderIII");
        f.StartCast(exdeath, (uint)AID.ThunderIII, 7);
        for (var slot = 0; slot < 8; ++slot)
        {
            var hints = f.ModuleHints(slot, (PartyRolesConfig.Assignment)slot);
            Check(hints.ForbiddenZones.All(z => z.shapeDistance.Distance(expected[slot]) >= 0),
                $"DMU Yarn crystals role{slot} rotation{rotation} mirror{mirror} fireFirst{fireFirst}: assigned point survives all native element/Thunder hints");
            Check(hints.ForbiddenZones.Any(z => z.shapeDistance.Distance(center) < 0),
                $"DMU Yarn crystals role{slot}: role destination is actual movement requirement");
        }
        f.FinishCast(exdeath);
        f.World.Execute(new ActorState.OpDestroy((fireFirst ? fire : water).InstanceID));
        Check(f.Hints(component, 0, PartyRolesConfig.Assignment.MT).ForbiddenZones.Count == 0,
            "DMU Yarn resolved crystal releases its route before next element");
        config.YarnPirenStrategy = false;
    }

    private static void CheckYarnWindStack()
    {
        using var f = new Fixture("DMU");
        DMUParty(f);
        var config = Service.Config.Get<DMUConfig>();
        config.YarnPirenStrategy = true;
        var center = f.Module.Center;
        var source = f.AddActor(0x200, (uint)OID.Exdeath, ActorType.Enemy, Class.None, center + new WDir(10, -10));
        var kb = f.Activate("BossMod.Dawntrail.Ultimate.DMU.HeadTailWind");
        var cyclone = (BossMod.Components.GenericStackSpread)f.Activate("BossMod.Dawntrail.Ultimate.DMU.Cyclone");
        var preposition = center + new WDir(1, -1).Normalized() * 10;
        for (var slot = 0; slot < 8; ++slot)
        {
            f.Status(f.World.Party[slot]!, 0, new((uint)((slot & 1) == 0 ? SID.Headwind : SID.Tailwind), 0, f.Now.AddSeconds(30), 0));
            var toSource = (source.Position - preposition).Normalized();
            var facing = ((slot & 1) == 0 ? -toSource : toSource).ToAngle();
            f.World.Execute(new ActorState.OpMove(f.World.Party[slot]!.InstanceID, new(preposition.X, 0, preposition.Z, facing.Rad)));
        }
        f.StartCast(source, (uint)AID.VacuumWave, 8);
        cyclone.Update();
        Check(cyclone.Stacks.Count == 0, "DMU Yarn wind: center stack does not override pre-knockback positioning");
        config.YarnPirenStrategy = false;
        cyclone.Update();
        Check(cyclone.Stacks.Count == 8 && cyclone.Stacks.All(s => s.MinSize == 2),
            "DMU legacy wind remains eight individual two-person mechanics instead of Yarn eight-person formation");
        config.YarnPirenStrategy = true;
        cyclone.Update();
        for (var slot = 0; slot < 8; ++slot)
        {
            var hints = f.ModuleHints(slot, (PartyRolesConfig.Assignment)slot);
            Check(hints.ForbiddenZones.All(z => z.shapeDistance.Distance(preposition) >= 0), $"DMU Yarn wind role{slot}: required facing and10y knockback permit center landing");
            Check(hints.ForbiddenDirections.Count == 1 && hints.ForbiddenDirections[0].activation > f.Now.AddSeconds(7), $"DMU Yarn wind role{slot}: facing deadline follows actual cast finish");
            Check(hints.ForbiddenZones.Any(z => z.shapeDistance.Distance(center) < 0), $"DMU Yarn wind role{slot}: prevents premature center arrival before knockback");
        }
        f.EventCast(source, (uint)AID.VacuumWave);
        for (var slot = 0; slot < 8; ++slot)
        {
            f.Status(f.World.Party[slot]!, 0, default);
            f.World.Execute(new ActorState.OpMove(f.World.Party[slot]!.InstanceID, new(center.X, 0, center.Z, 0)));
        }
        cyclone.Update();
        Check(cyclone.Stacks.Count == 1 && cyclone.Stacks[0].MinSize == 8 && cyclone.Stacks[0].MaxSize == 8,
            "DMU Yarn wind: real Vacuum event creates8person stack even after wind debuffs cleanse");
        for (var slot = 0; slot < 8; ++slot)
        {
            var hints = f.ModuleHints(slot, (PartyRolesConfig.Assignment)slot);
            Check(hints.ForbiddenZones.All(z => z.shapeDistance.Distance(center) >= 0) && hints.ForbiddenZones.Any(z => z.shapeDistance.Distance(preposition) < 0),
                $"DMU Yarn wind role{slot}: all native active components permit center stack and reject old outer positions");
        }
        for (var slot = 0; slot < 8; ++slot) f.EventCast(source, (uint)AID.Cyclone, f.World.Party[slot]!.InstanceID);
        cyclone.Update();
        Check(cyclone.Stacks.Count == 0 && f.Hints(cyclone, 0).ForbiddenZones.Count == 0, "DMU Yarn wind: all8 actual Cyclone effects release stack movement");
        config.YarnPirenStrategy = false;
    }
}
