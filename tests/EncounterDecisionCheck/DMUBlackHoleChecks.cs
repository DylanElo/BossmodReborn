using BossMod;
using BossMod.Dawntrail.Ultimate.DMU;

internal static partial class EncounterChecks
{
    static partial void CheckDMUBlackHole()
    {
        CheckBlackHolePickups();
        CheckBlackHoleBaitDirections();
        CheckStompAssignments();
    }

    private static void CheckBlackHolePickups()
    {
        using var f = new Fixture("DMU");
        DMUParty(f);
        var config = Service.Config.Get<DMUConfig>();
        config.YarnPirenStrategy = true;
        f.Activate("BossMod.Dawntrail.Ultimate.DMU.KefkaMax");
        var bh = f.Activate("BossMod.Dawntrail.Ultimate.DMU.BlackHole");
        f.Status(f.Boss, 0, new((uint)SID.KefkaMax, 0, f.Now.AddSeconds(180), 0));
        Sign[] signs = [Sign.Attack1, Sign.Attack2, Sign.Attack3, Sign.Bind1, Sign.Bind2, Sign.Bind3, Sign.Ignore1, Sign.Ignore2];
        for (var i = 0; i < 8; i++)
        {
            f.World.Execute(new WaymarkState.OpSignChange(signs[i], f.World.Party[i]!.InstanceID));
            f.Status(f.World.Party[i]!, 0, new((uint)(i < 3 ? SID.FirstInLine : i < 6 ? SID.SecondInLine : SID.ThirdInLine), 0, f.Now.AddSeconds(180), 0));
        }
        int[][] recipients = [[0], [0,1], [0,1,2], [3,1,2], [3,4,2], [3,4,5], [6,4,5], [6,7,5], [6,7], [7]];
        var beam = 0;
        var serial = 0ul;
        foreach (var wave in recipients)
        {
            var holes = new List<Actor>();
            foreach (var side in Enumerable.Range(0, wave.Length))
            {
                WDir[] dirs = [new(1, 0), new(0, 1), new(-1, 0)];
                var source = f.AddActor(0x500 + ++serial, (uint)OID.BlackHole, ActorType.Enemy, Class.None, new WPos(100, 100) + dirs[side] * 15f);
                var holder = f.AddActor(0x900 + serial, (uint)OID.Helper, ActorType.Enemy, Class.None, source.Position + dirs[side] * 12f);
                holes.Add(source);
                f.World.Execute(new ActorState.OpTether(source.InstanceID, new((uint)TetherID.BlackHoleTether, holder.InstanceID)));
            }
            for (var slot = 0; slot < 8; slot++)
            {
                var hints = f.Hints(bh, slot);
                var hasPickup = holes.Any(h => {
                    var target = f.World.Actors.Find(h.Tether.Target)!;
                    var point = h.Position + (target.Position - h.Position).Normalized() * 4f;
                    // The pickup circle excludes the arena centre; inherited outward beams do not.
                    return hints.ForbiddenZones.Any(z => z.shapeDistance.Distance(point) > 0 && z.shapeDistance.Distance(new(100,100)) < 0);
                });
                Check(hasPickup == wave.Contains(slot), $"DMU BH beam {beam}: observed marker slot {slot} pickup eligibility");
                var text=new BossComponent.TextHints();
                bh.AddHints(slot,f.World.Party[slot]!,text);
                Check(text.Any(t=>t.Item1=="Take your marked Black Hole tether!")==wave.Contains(slot),
                    $"DMU BH beam {beam}: text assignment matches actual pickup slot {slot}");
            }
            if (beam == 0)
            {
                config.YarnPirenStrategy = false;
                Check(f.Hints(bh, 0).ForbiddenZones.All(z => z.shapeDistance.Distance(new(100,100)) >= 0), "DMU BH default strategy keeps inherited avoidance only");
                config.YarnPirenStrategy = true;
                f.World.Execute(new WaymarkState.OpSignChange(Sign.Ignore2, f.World.Party[6]!.InstanceID));
                Check(f.Hints(bh, 0).ForbiddenZones.All(z => z.shapeDistance.Distance(new(100,100)) >= 0), "DMU BH duplicate markers fail closed");
                var unknownText=new BossComponent.TextHints();
                bh.AddHints(0,f.World.Party[0]!,unknownText);
                Check(!unknownText.Any(t=>t.Item1.Contains("marked Black Hole")),"DMU BH invalid marker text does not invent an assignment");
                f.World.Execute(new WaymarkState.OpSignChange(Sign.Ignore2, f.World.Party[7]!.InstanceID));
                var source = holes[0];
                f.World.Execute(new ActorState.OpTether(source.InstanceID, new((uint)TetherID.BlackHoleTether, f.World.Party[0]!.InstanceID)));
                var ownedCount = f.Hints(bh, 0).ForbiddenZones.Count;
                config.YarnPirenStrategy = false;
                Check(ownedCount == f.Hints(bh,0).ForbiddenZones.Count + 2, "DMU BH confirmed pickup replaces interception with directional bait constraints, preserving inherited safety");
                config.YarnPirenStrategy = true;
            }
            foreach (var hole in holes)
            {
                f.World.Execute(new ActorState.OpTether(hole.InstanceID, default));
                f.EventCast(hole, (uint)AID.Nothingness);
                beam++;
            }
        }
        Check(beam == 24 && f.Hints(bh, 7).ForbiddenZones.Count == 0, "DMU BH 24 actual beam events end pickup routing");
        config.YarnPirenStrategy = false;
    }

    private static void CheckBlackHoleBaitDirections()
    {
        foreach(var radial in new WDir[]{new(0,-15),new(15,0),new(0,15),new(-15,0)})
        foreach(var last in new[]{false,true})
        {
            using var f=new Fixture("DMU");
            DMUParty(f);
            var config=Service.Config.Get<DMUConfig>();config.YarnPirenStrategy=true;
            f.Activate("BossMod.Dawntrail.Ultimate.DMU.KefkaMax");
            var bh=f.Activate("BossMod.Dawntrail.Ultimate.DMU.BlackHole");
            f.Status(f.Boss,0,new((uint)SID.KefkaMax,0,f.Now.AddSeconds(180),0));
            Sign[] signs=[Sign.Attack1,Sign.Attack2,Sign.Attack3,Sign.Bind1,Sign.Bind2,Sign.Bind3,Sign.Ignore1,Sign.Ignore2];
            for(var i=0;i<8;i++)
            {
                f.World.Execute(new WaymarkState.OpSignChange(signs[i],f.World.Party[i]!.InstanceID));
                f.Status(f.World.Party[i]!,0,new((uint)(i<3?SID.FirstInLine:i<6?SID.SecondInLine:SID.ThirdInLine),0,f.Now.AddSeconds(180),0));
            }
            if(last)for(var i=0;i<23;i++)f.EventCast(f.Boss,(uint)AID.Nothingness);
            var slot=last?7:0;
            var hole=f.AddActor(0xB00,(uint)OID.BlackHole,ActorType.Enemy,Class.None,new WPos(100,100)+radial);
            f.World.Execute(new ActorState.OpTether(hole.InstanceID,new((uint)TetherID.BlackHoleTether,f.World.Party[slot]!.InstanceID)));
            var zones=f.Hints(bh,slot).ForbiddenZones;
            var tangent=last?radial.Normalized().OrthoR():radial.Normalized().OrthoL();
            var directional=zones[^2].shapeDistance;
            Check(directional.Distance(new WPos(100,100)+tangent*8f)>0 && directional.Distance(new WPos(100,100)-tangent*8f)<0,
                $"DMU BH {(last?"last CW":"first CCW")} actual cardinal {radial} bait direction");
            Check(zones[^1].shapeDistance.Distance(new(100,100))>0 && zones[^1].shapeDistance.Distance(hole.Position)<0,
                "DMU BH owned bait stays inside the observed hole with voidzone margin");
            var text=new BossComponent.TextHints();
            bh.AddHints(slot,f.World.Party[slot]!,text);
            Check(text.Any(t=>t.Item1==(last?"Bait Black Hole clockwise!":"Bait Black Hole counterclockwise!")),
                "DMU BH owned text uses same observed wave direction as navigation");
            config.YarnPirenStrategy=false;
        }
    }

    private static void CheckStompAssignments()
    {
        using var f = new Fixture("DMU");
        DMUParty(f);
        var config = Service.Config.Get<DMUConfig>();
        config.YarnPirenStrategy = true;
        var stack = f.Activate("BossMod.Dawntrail.Ultimate.DMU.KnockDown");
        var stomp = f.Activate("BossMod.Dawntrail.Ultimate.DMU.StompAMole");
        // Native stack marker identifies which role must stack; the other role takes the first tower pair.
        stack.OnEventIcon(f.World.Party[0]!, (uint)IconID.StackShare, f.World.Party[0]!.InstanceID);
        f.StartCast(f.Boss, (uint)AID.StompAMoleCast, 5);
        stomp.Update();
        for(var slot=0;slot<8;slot++)
            Check((f.Hints(stomp,slot).ForbiddenZones.Count > 0) == (slot>=4), $"DMU Stomp first pair follows observed support stack, slot {slot}");
        foreach(var slot in new[]{4,5,6,7})
        {
            var zone=f.Hints(stomp,slot).ForbiddenZones.Single();
            var left=slot is 4 or 6;
            Check(zone.shapeDistance.Distance(new(left?90:110,100))>0 && zone.shapeDistance.Distance(new(left?110:90,100))<0,
                $"DMU Stomp DPS pair selects correct actual tower, slot {slot}");
        }
        var helper=f.AddActor(0xD00,(uint)OID.Helper,ActorType.Enemy,Class.None,new(110,100));
        f.EventCast(helper,(uint)AID.StompAMoleTower);
        var helper2=f.AddActor(0xD01,(uint)OID.Helper,ActorType.Enemy,Class.None,new(90,100));
        f.EventCast(helper2,(uint)AID.StompAMoleTower);
        for(var slot=0;slot<8;slot++)
            Check((f.Hints(stomp,slot).ForbiddenZones.Count > 0) == (slot<4), $"DMU Stomp second pair swaps tower role, slot {slot}");
        foreach(var slot in new[]{0,1,2,3})
        {
            var zone=f.Hints(stomp,slot).ForbiddenZones.Single();
            var left=slot is 0 or 2;
            Check(zone.shapeDistance.Distance(new(left?90:110,100))>0 && zone.shapeDistance.Distance(new(left?110:90,100))<0,
                $"DMU Stomp support pair selects correct actual tower, slot {slot}");
        }
        config.YarnPirenStrategy=false;
        Check(f.Hints(stomp,0).ForbiddenZones.Count==0,"DMU Stomp default strategy unchanged");
    }
}
