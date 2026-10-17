using System.Numerics;
using BossMod;
using BossMod.Dawntrail.Ultimate.DMU;

internal static partial class EncounterChecks
{
    static partial void CheckDMURemaining()
    {
        RunGroup("DMU P5 actual second-wave completion", CheckDMUSecondWaveCompletion);
        RunGroup("DMU P5 observed tower assignments", CheckDMUCelestriad);
        RunGroup("DMU P5 tower timeline completion", CheckDMUTowerCompletion);
        RunGroup("DMU P5 interleaved tower actors", CheckDMUInterleavedTowers);
        RunGroup("DMU P5 all observed exaflare lines", CheckDMUExaflares);
    }

    private static void CheckDMUSecondWaveCompletion()
    {
        foreach (var second in new[] { false, true })
        foreach (var holyFirst in new[] { false, true })
        {
            using var f = new Fixture("DMU");
            var boss = OrchestraBoss(f);
            var before = second ? 0x04000250u : 0x04000090u;
            var after = second ? 0x04000270u : 0x04000110u;
            f.EnterState(4, before);
            f.Activate("BossMod.Dawntrail.Ultimate.DMU.MaddeningOrchestra");
            f.StartCast(boss, (uint)AID.MaddeningOrchestra, 5);
            f.EventCast(boss, (uint)AID.Holy);
            f.Advance(second ? 0.9 : 9.9);
            var awaiting = f.Module.StateMachine.ActiveState!.ID;
            for (var i = 1; i < 6; ++i) f.EventCast(boss, (uint)AID.Holy);
            f.EventCast(boss, (uint)AID.ChaoticFlareTB);
            f.Advance(3.2);
            Check(f.Module.StateMachine.ActiveState?.ID == awaiting,
                $"DMU Orchestra {(second ? 2 : 1)} retains routes after only six of eight effects");
            f.EventCast(boss, (uint)AID.Holy);
            f.EventCast(boss, (uint)AID.Holy);
            f.Advance(0);
            Check(f.Module.StateMachine.ActiveState?.ID == after, "DMU all eight bait effects and tankbuster advance native timeline");
            var first = holyFirst ? AID.ChaoticHoly : AID.FlareDiffusion;
            var last = holyFirst ? AID.FlareDiffusion : AID.ChaoticHoly;
            f.EventCast(boss, (uint)first);
            f.Advance(3.5);
            Check(f.Module.StateMachine.ActiveState?.ID == after, "DMU both Holy/Flare sets hold after first actual tank hit in either order");
            Check(f.Module.Components.Any(c => c.GetType().Name == "ChaoticHolyFlareDiffusion"), "DMU unmatched tank hit keeps native route component alive");
            f.EventCast(boss, (uint)last);
            f.Advance(0);
            Check(!f.Module.Components.Any(c => c.GetType().Name == "ChaoticHolyFlareDiffusion"), "DMU second actual tank hit clears route component");
            Check(f.Errors.Count == 0, "DMU repeated bait/tank lifecycle avoids missing-component fallback");
        }

        using var delayedTB = new Fixture("DMU");
        var kefka = OrchestraBoss(delayedTB);
        delayedTB.EnterState(4, 0x04000250);
        delayedTB.Activate("BossMod.Dawntrail.Ultimate.DMU.MaddeningOrchestra");
        delayedTB.EventCast(kefka, (uint)AID.Holy);
        delayedTB.Advance(0.9);
        for (var i = 1; i < 8; ++i) delayedTB.EventCast(kefka, (uint)AID.Holy);
        delayedTB.Advance(3.2);
        Check(delayedTB.Module.StateMachine.ActiveState?.ID == 0x04000260, "DMU eight bait effects still wait for actual tankbuster");
        delayedTB.EventCast(kefka, (uint)AID.ChaoticFlareTB);
        delayedTB.Advance(0);
        Check(delayedTB.Module.StateMachine.ActiveState?.ID == 0x04000270, "DMU delayed tankbuster releases native phase progression");
    }

    private static void CheckDMUCelestriad()
    {
        using var f = new Fixture("DMU");
        OrchestraBoss(f);
        f.Module.ClearComponents(_ => true);
        var component = (BossMod.Components.GenericTowers)f.Activate("BossMod.Dawntrail.Ultimate.DMU.Celestriad");
        Actor[] towers = new Actor[9];
        for (var i = 0; i < 9; ++i)
        {
            var direction = (180 - i * 40).Degrees().ToDirection();
            var oid = i < 3 ? OID.IceTower : i < 6 ? OID.FireTower : OID.ThunderTower;
            towers[i] = f.AddActor((ulong)(0x300 + i), (uint)oid, ActorType.EventObj, Class.None, f.Module.Center + 14 * direction);
        }
        int[][] waves = [[0, 1, 3, 6], [1, 3, 4, 6], [1, 4, 6, 7]];
        int[][] expected = [[3, 3, 6, 6, 0, 0, 1, 1], [6, 6, 1, 1, 3, 3, 4, 4], [1, 1, 4, 4, 6, 6, 7, 7]];
        uint[] statuses = [(uint)SID.IceResistanceDownII, (uint)SID.FireResistanceDownII, (uint)SID.LightningResistanceDownII];
        for (var wave = 0; wave < 3; ++wave)
        {
            for (var i = 0; i < 4; ++i)
            {
                var tower = towers[waves[wave][i]];
                TowerAnimation(tower, Animations.TowerGlow);
                component.Update();
                if (i < 3)
                    Check(f.ModuleHints(0).ForbiddenZones.Count == 0, "DMU partial tower glow wave does not invent assignments");
            }
            if (wave == 0)
            {
                Check(f.ModuleHints(0).ForbiddenZones.Count == 0, "DMU full tower wave with unknown debuffs does not guess destinations");
                for (var slot = 0; slot < 6; ++slot)
                {
                    f.Status(f.World.Party[slot]!, 0, new(statuses[slot / 2], 0, f.Now.AddSeconds(12), 0));
                    component.Update();
                    if (slot < 5)
                        Check(f.ModuleHints(0).ForbiddenZones.Count == 0, "DMU incomplete initial debuffs do not route any role");
                }
            }
            component.Update();
            TowerAnimation(towers[waves[wave][0]], Animations.TowerGlow);
            Check(component.Towers.Count == 4, "DMU duplicate tower glow cannot create a fifth tower");
            for (var slot = 0; slot < 8; ++slot)
            {
                var destination = towers[expected[wave][slot]].Position;
                var player = f.World.Party[slot]!;
                f.World.Execute(new ActorState.OpMove(player.InstanceID, new Vector4(destination.X, 0, destination.Z, 0)));
            }
            for (var slot = 0; slot < 8; ++slot)
            {
                var eligible = component.Towers.Where(t => !t.ForbiddenSoakers[slot]).ToArray();
                Check(eligible.Length == 1 && eligible[0].ActorID == towers[expected[wave][slot]].InstanceID,
                    $"DMU tower wave {wave + 1} role {slot} actual element rotation and duplicated neutral assignment");
                var hints = f.ModuleHints(slot, (PartyRolesConfig.Assignment)slot);
                Check(hints.ForbiddenZones.Count > 0 && hints.ForbiddenZones.All(z => z.shapeDistance.Distance(f.World.Party[slot]!.Position) >= 0),
                    $"DMU tower wave {wave + 1} role {slot} aggregate AI permits correct full-party destination");
                Check(eligible[0].Activation == f.Now.AddSeconds(6.1f), "DMU tower soak deadline follows observed six-second glow");
            }
            f.World.Frame.Timestamp = f.Now.AddSeconds(6.1);
            foreach (var index in waves[wave])
            {
                TowerAnimation(towers[index], Animations.TowerExplosion);
                component.Update();
                Check(f.ModuleHints(0).ForbiddenZones.Count == 0, "DMU incomplete resolved tower wave releases stale route constraints");
            }
            TowerAnimation(towers[waves[wave][0]], Animations.TowerExplosion);
            Check(component.NumCasts == (wave + 1) * 4, "DMU duplicate or unmatched tower explosion cannot spend future wave");
            for (var slot = 0; slot < 6; ++slot) f.Status(f.World.Party[slot]!, 0, default);
        }
        Check(component.Towers.Count == 0, "DMU three complete tower waves leave no stale AI zones");

        void TowerAnimation(Actor tower, Animations animation)
        {
            var state = (uint)animation;
            f.World.Execute(new ActorState.OpEventObjectAnimation(tower.InstanceID, (ushort)(state >> 16), (ushort)state));
        }
    }

    private static void CheckDMUExaflares()
    {
        using var f = new Fixture("DMU");
        OrchestraBoss(f);
        f.Module.ClearComponents(_ => true);
        var component = (BossMod.Components.Exaflare)f.Activate("BossMod.Dawntrail.Ultimate.DMU.StrayApocalypse");
        Actor[] helpers = new Actor[6];
        for (var i = 0; i < helpers.Length; ++i)
        {
            helpers[i] = f.AddActor((ulong)(0x500 + i), (uint)OID.Helper, ActorType.Enemy, Class.None, new(85 + 6 * i, 85));
            f.StartCast(helpers[i], (uint)AID.StrayApocalypseExaFlareCast, 1);
        }
        component.Update();
        Check(component.Lines.Count == 6 && component.ActiveAOEs(0, f.World.Party[0]!).Length == 42,
            "DMU six observed exaflare lines all export their seven native explosions");
        var hints = f.ModuleHints(0);
        Check(hints.ForbiddenZones.Any(z => z.shapeDistance.Distance(helpers[5].Position) < 0), "DMU aggregate AI avoids observed fifth and sixth lines");
        var unrelated = f.AddActor(0x600, (uint)OID.Helper, ActorType.Enemy, Class.None, new(200, 200));
        f.EventCast(unrelated, (uint)AID.StrayApocalypseExaFlare);
        Check(component.NumCasts == 0, "DMU unmatched exaflare event cannot advance timeline count");
        for (var explosion = 0; explosion < 7; ++explosion)
        {
            for (var line = 0; line < helpers.Length; ++line)
            {
                var position = new WPos(85 + 6 * line, 85 + 7.071f * explosion);
                f.World.Execute(new ActorState.OpMove(helpers[line].InstanceID, new Vector4(position.X, 0, position.Z, 0)));
                f.EventCast(helpers[line], (uint)(explosion == 0 ? AID.StrayApocalypseExaFlareCast : AID.StrayApocalypseExaFlare));
            }
        }
        Check(component.NumCasts == 42 && !component.Active, "DMU each matched actual exaflare impact advances once and removes its finished line");
        Check(f.ModuleHints(0).ForbiddenZones.Count == 0, "DMU finished exaflares release aggregate movement constraints");
    }

    private static void CheckDMUTowerCompletion()
    {
        using var f = new Fixture("DMU");
        var boss = OrchestraBoss(f);
        f.EnterState(4, 0x04000160);
        f.Activate("BossMod.Dawntrail.Ultimate.DMU.Celestriad");
        f.Activate("BossMod.Dawntrail.Ultimate.DMU.CatastrophicChoice");
        Actor[] towers = new Actor[12];
        for (var i = 0; i < towers.Length; ++i)
        {
            towers[i] = f.AddActor((ulong)(0x700 + i), (uint)OID.IceTower, ActorType.EventObj, Class.None, new(100, 86));
            f.World.Execute(new ActorState.OpEventObjectAnimation(towers[i].InstanceID, 0x10, 0x20));
        }
        f.World.Frame.Timestamp = f.Now.AddSeconds(6.1);
        for (var i = 0; i < 11; ++i)
            f.World.Execute(new ActorState.OpEventObjectAnimation(towers[i].InstanceID, 1, 0x40));
        f.EventCast(boss, (uint)AID.Quake);
        f.EventCast(boss, (uint)AID.Quake);
        f.Advance(0); // The actual 11 observed explosions advance through the second and third tower states.
        f.Advance(0.2);
        Check(f.Module.StateMachine.ActiveState?.ID == 0x04000175, "DMU final in/out cannot discard twelfth pending tower explosion");
        f.World.Execute(new ActorState.OpEventObjectAnimation(towers[11].InstanceID, 1, 0x40));
        f.Advance(0);
        Check(f.Module.StateMachine.ActiveState?.ID == 0x04000180, "DMU twelve observed tower explosions plus both in/out effects advance");
        Check(!f.Module.Components.Any(c => c.GetType().Name is "Celestriad" or "CatastrophicChoice"), "DMU tower/in-out components end with completed sequence");
        Check(f.Errors.Count == 0, "DMU tower completion has no missing-component fallback");
    }

    private static void CheckDMUInterleavedTowers()
    {
        using var f = new Fixture("DMU");
        OrchestraBoss(f);
        f.Module.ClearComponents(_ => true);
        var component = (BossMod.Components.GenericTowers)f.Activate("BossMod.Dawntrail.Ultimate.DMU.Celestriad");
        var tower = f.AddActor(0x800, (uint)OID.IceTower, ActorType.EventObj, Class.None, new(100, 86));
        f.World.Execute(new ActorState.OpEventObjectAnimation(tower.InstanceID, 0x10, 0x20));
        var first = component.Towers.Single().Activation;
        f.World.Frame.Timestamp = f.Now.AddSeconds(6);
        f.World.Execute(new ActorState.OpEventObjectAnimation(tower.InstanceID, 0x10, 0x20));
        var next = f.Now.AddSeconds(6.1f);
        Check(component.Towers.Count == 2, "DMU shared tower actor can glow next wave before its old explosion");
        f.World.Execute(new ActorState.OpEventObjectAnimation(tower.InstanceID, 1, 0x40));
        Check(component.NumCasts == 1 && component.Towers.Single().Activation == next && next > first,
            "DMU old tower explosion preserves already-glowing next wave");
        f.World.Execute(new ActorState.OpEventObjectAnimation(tower.InstanceID, 1, 0x40));
        Check(component.NumCasts == 1 && component.Towers.Count == 1, "DMU duplicate old explosion cannot resolve future tower");
        f.World.Execute(new ActorState.OpEventObjectAnimation(tower.InstanceID, 0x10, 0x20));
        Check(component.Towers.Count == 1, "DMU repeated next-wave glow stays deduplicated");
        f.World.Frame.Timestamp = next;
        f.World.Execute(new ActorState.OpEventObjectAnimation(tower.InstanceID, 1, 0x40));
        Check(component.NumCasts == 2 && component.Towers.Count == 0, "DMU future tower resolves only at its actual wave");
    }
}
