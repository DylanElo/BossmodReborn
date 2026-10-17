using System.Numerics;
using BossMod;
using BossMod.Dawntrail.Ultimate.DMU;

internal static partial class EncounterChecks
{
    static partial void CheckDMUOrchestra()
    {
        RunGroup("DMU Yarn Orchestra scatter and actual bait membership", CheckOrchestraBaits);
        RunGroup("DMU Yarn Holy/Flare observed invulnerability", CheckHolyFlare);
    }

    private static Actor OrchestraBoss(Fixture f)
    {
        DMUParty(f);
        var boss = f.AddActor(0x204, (uint)OID.KefkaP5, ActorType.Enemy, Class.None, new(100, 100));
        f.EnterState(4, f.Module.StateMachine.Phases[4].InitialState.ID);
        f.Tick(); // populate the actual P5 actor cache before component construction
        return boss;
    }

    private static void OrchestraMove(Fixture f, Actor actor, WPos pos)
        => f.World.Execute(new ActorState.OpMove(actor.InstanceID, new Vector4(pos.X, 0, pos.Z, 0)));

    private static void OrchestraUpdate(Fixture f)
    {
        foreach (var component in f.Module.Components.ToArray()) component.Update();
    }

    private static void CheckOrchestraBaits()
    {
        Service.Config.Get<DMUConfig>().YarnPirenStrategy = true;
        WDir[] scatter = [new(-4, -10), new(4, -10), new(-10, 4), new(10, 4), new(-4, 10), new(4, 10), new(-10, -4), new(10, -4)];
        // Every possible random set of three initial non-tank targets; the guide has no fixed 1-6 bait order.
        for (var a = 2; a < 6; ++a)
        for (var b = a + 1; b < 7; ++b)
        for (var c = b + 1; c < 8; ++c)
        {
            using var f = new Fixture("DMU");
            var boss = OrchestraBoss(f);
            f.Activate("BossMod.Dawntrail.Ultimate.DMU.MaddeningOrchestra");
            for (var slot = 0; slot < 8; ++slot) OrchestraMove(f, f.World.Party[slot]!, f.Module.Center + scatter[slot]);
            f.StartCast(boss, (uint)AID.MaddeningOrchestra, 5);
            OrchestraUpdate(f);
            for (var slot = 0; slot < 8; ++slot)
            {
                var point = f.Module.Center + scatter[slot];
                var hints = f.ModuleHints(slot, (PartyRolesConfig.Assignment)slot);
                Check(hints.ForbiddenZones.Count > 0 && hints.ForbiddenZones.All(z => z.shapeDistance.Distance(point) >= 0),
                    $"DMU Orchestra first scatter role{slot} survives aggregate bait hazards");
            }
            for (var i = 0; i < 5; ++i) f.EventCast(boss, (uint)AID.Holy);
            OrchestraUpdate(f);
            var known = f.ModuleHints(2, PartyRolesConfig.Assignment.H1).ForbiddenZones.Count;
            var unknown = f.ModuleHints(2, PartyRolesConfig.Assignment.Unassigned).ForbiddenZones.Count;
            Check(known == unknown, "DMU Orchestra incomplete random-target statuses do not invent bait membership");
            foreach (var slot in new[] { a, b, c })
                f.Status(f.World.Party[slot]!, 0, new((uint)SID.MagicVulnerabilityUp, 0, f.Now.AddSeconds(20), 0));
            WPos[] second = new WPos[8];
            for (var slot = 0; slot < 8; ++slot)
            {
                var hit = slot == a || slot == b || slot == c;
                var offset = slot < 2 ? new WDir(0, -10) : scatter[slot] / scatter[slot].Length() * (hit ? 16f : 8f);
                second[slot] = f.Module.Center + offset;
                OrchestraMove(f, f.World.Party[slot]!, second[slot]);
            }
            var tankbuster = f.Activate("BossMod.Dawntrail.Ultimate.DMU.ChaoticFlareTB");
            tankbuster.GetType().GetField("active")!.SetValue(tankbuster, true);
            OrchestraUpdate(f);
            for (var slot = 0; slot < 8; ++slot)
            {
                var hints = f.ModuleHints(slot, (PartyRolesConfig.Assignment)slot);
                Check(hints.ForbiddenZones.All(z => z.shapeDistance.Distance(second[slot]) >= 0),
                    $"DMU Orchestra set{a}{b}{c} role{slot} inner/outer/tank destination survives aggregate actual stack and bait hazards");
            }
            f.EventCast(boss, (uint)AID.ChaoticFlareTB);
            for (var i = 0; i < 3; ++i) f.EventCast(boss, (uint)AID.Holy);
            OrchestraUpdate(f);
            Check(f.ModuleHints(2, PartyRolesConfig.Assignment.H1).ForbiddenZones.Count == 0,
                "DMU Orchestra completed effects clear forced routes and inherited bait/stack hazards");
        }
        Service.Config.Get<DMUConfig>().YarnPirenStrategy = false;
    }

    private static void CheckHolyFlare()
    {
        Service.Config.Get<DMUConfig>().YarnPirenStrategy = true;
        foreach (var holySlot in new[] { 0, 1 })
        {
            using var f = new Fixture("DMU");
            var boss = OrchestraBoss(f);
            f.Module.ClearComponents(_ => true);
            f.Activate("BossMod.Dawntrail.Ultimate.DMU.ChaoticHolyFlareDiffusion");
            var holy = f.World.Party[holySlot]!;
            var flare = f.World.Party[1 - holySlot]!;
            OrchestraMove(f, holy, new(100, 90));
            OrchestraMove(f, flare, new(100, 81));
            for (var slot = 2; slot < 8; ++slot) OrchestraMove(f, f.World.Party[slot]!, new(100, 115));
            f.Status(holy, 0, new((uint)SID.SurpriseHoly, 0, f.Now.AddSeconds(3), 0));
            f.Status(flare, 0, new((uint)SID.SurpriseFlare, 0, f.Now.AddSeconds(3), 0));
            Check(f.ModuleHints(holySlot, (PartyRolesConfig.Assignment)holySlot).ForbiddenZones.Any(z => z.shapeDistance.Distance(new(100, 90)) < 0),
                "DMU Holy keeps actual lethal Flare avoidance until invulnerability is observed");
            f.Status(holy, 1, new(82, 0, f.Now.AddSeconds(2), 0));
            Check(f.ModuleHints(holySlot, (PartyRolesConfig.Assignment)holySlot).ForbiddenZones.Any(z => z.shapeDistance.Distance(new(100, 90)) < 0),
                "DMU Holy invulnerability expiring before the hit cannot authorize overlap");
            // The observed Flare may resolve after Holy; immunity must cover both effects.
            f.Status(flare, 0, new((uint)SID.SurpriseFlare, 0, f.Now.AddSeconds(5), 0));
            f.Status(holy, 1, new(82, 0, f.Now.AddSeconds(4), 0));
            Check(f.ModuleHints(holySlot, (PartyRolesConfig.Assignment)holySlot).ForbiddenZones.Any(z => z.shapeDistance.Distance(new(100, 90)) < 0),
                "DMU Holy immunity covering Holy but expiring before observed Flare cannot authorize overlap");
            f.Status(holy, 1, new(82, 0, f.Now.AddSeconds(10), 0));
            for (var slot = 0; slot < 8; ++slot)
            {
                var point = slot == holySlot ? new WPos(100, 90) : slot == 1 - holySlot ? new WPos(100, 81) : new WPos(100, 115);
                var hints = f.ModuleHints(slot, (PartyRolesConfig.Assignment)slot);
                Check(hints.ForbiddenZones.Count > 0 && hints.ForbiddenZones.All(z => z.shapeDistance.Distance(point) >= 0),
                    $"DMU Holy observed tank{holySlot} swap role{slot} route survives aggregate live Flare/Holy zones");
            }
            f.EventCast(boss, (uint)AID.FlareDiffusion);
            f.EventCast(boss, (uint)AID.ChaoticHoly);
            Check(f.ModuleHints(2, PartyRolesConfig.Assignment.H1).ForbiddenZones.Count == 0,
                "DMU Holy/Flare resolves clear status-driven routes and hazards");
        }
        Service.Config.Get<DMUConfig>().YarnPirenStrategy = false;
    }
}
