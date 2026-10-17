using System.Numerics;
using System.Reflection;
using BossMod;
using BossMod.Components;
using BossMod.Dawntrail.Ultimate.DMU;

internal static partial class EncounterChecks
{
    static partial void CheckDMUPiren()
    {
        Service.Config.Get<DMUConfig>().YarnPirenStrategy = true;
        Check(typeof(BossModule).Assembly.GetType("BossMod.Dawntrail.Ultimate.DMU.ForsakenShapes")!.GetMethod("DrawPirenSafeSpot", BindingFlags.Public | BindingFlags.Instance) != null,
            "DMU Piren tested DLL includes current production geometry");
        CheckPlayerSourcedBaitAim();
        for (var supportStack = 0; supportStack < 4; ++supportStack)
            for (var dpsStack = 4; dpsStack < 8; ++dpsStack)
                foreach (var supportCone in new[] { true, false })
                    for (var rotation = 0; rotation < 8; ++rotation)
                        CheckPirenTowers(supportStack, dpsStack, supportCone, rotation);
        CheckPirenCloneWait(false);
        CheckPirenCloneWait(true);
        Service.Config.Get<DMUConfig>().YarnPirenStrategy = false;
    }

    private static void CheckPlayerSourcedBaitAim()
    {
        foreach (var shape in new AOEShape[] { new AOEShapeCone(40, 45f.Degrees()), new AOEShapeRect(40, 2) })
        {
            using var f = new Fixture("DMU");
            DMUParty(f);
            var component = (GenericBaitAway)f.Activate("BossMod.Dawntrail.Ultimate.DMU.ForsakenBaitsCone");
            var source = f.World.Party[0]!;
            var target = f.World.Party[1]!;
            var victim = f.World.Party[2]!;
            var npcSource = f.AddActor(0x700, (uint)OID.Helper, ActorType.Enemy, Class.None, new(100, 100));
            var otherSource = f.AddActor(0x701, (uint)OID.Helper, ActorType.Enemy, Class.None, new(100, 115));
            Move(source, new(100, 100));
            Move(target, new(100, 110));
            Move(victim, new(110, 100));
            for (var slot = 3; slot < 8; ++slot)
                Move(f.World.Party[slot]!, new(100, 80));
            component.CurrentBaits.Add(new(source, target, shape));
            Check(f.Hints(component, 1).ForbiddenZones.All(z => z.shapeDistance.Distance(target.Position) >= 0),
                $"Native {shape.GetType().Name}: player emitter does not make every bait aim illegal");
            Move(victim, new(100, 108));
            Check(f.Hints(component, 1).ForbiddenZones.Any(z => z.shapeDistance.Distance(target.Position) < 0),
                $"Native {shape.GetType().Name}: bait target still cannot aim through another player");
            Check(f.Hints(component, 2).ForbiddenZones.Any(z => z.shapeDistance.Distance(victim.Position) < 0),
                $"Native {shape.GetType().Name}: actual cone/line still forbids a clipped bystander");
            Move(victim, new(110, 100));
            component.CurrentBaits.Add(new(otherSource, f.World.Party[3]!, shape));
            Check(f.Hints(component, 1).ForbiddenZones.Last().shapeDistance.Distance(target.Position) < 0,
                $"Native {shape.GetType().Name}: another simultaneous bait remains a real hazard");
            component.CurrentBaits.Clear();
            Move(source, new(100, 80));
            component.CurrentBaits.Add(new(npcSource, target, shape));
            Check(f.Hints(component, 1).ForbiddenZones.All(z => z.shapeDistance.Distance(target.Position) >= 0),
                $"Native {shape.GetType().Name}: NPC-source valid bait behavior remains unchanged");
            Move(victim, new(100, 108));
            Check(f.Hints(component, 1).ForbiddenZones.Any(z => z.shapeDistance.Distance(target.Position) < 0),
                $"Native {shape.GetType().Name}: NPC-source clipped-player avoidance remains unchanged");

            void Move(Actor actor, WPos position) => f.World.Execute(new ActorState.OpMove(actor.InstanceID, new(position.X, 0, position.Z, 0)));
        }
    }

    private static void CheckPirenTowers(int initialSupportStack, int initialDpsStack, bool supportCone, int rotation)
    {
        using var f = new Fixture("DMU");
        DMUParty(f);
        var shapes = f.Activate("BossMod.Dawntrail.Ultimate.DMU.ForsakenShapes");
        var towers = (GenericTowers)f.Activate("BossMod.Dawntrail.Ultimate.DMU.PathOfLight");
        var oddSolver = f.Activate("BossMod.Dawntrail.Ultimate.DMU.ForsakenSolverSet1");
        var evenSolver = f.Activate("BossMod.Dawntrail.Ultimate.DMU.ForsakenSolverSet2");
        var spreads = f.Activate("BossMod.Dawntrail.Ultimate.DMU.ForsakenBaitsSpreadStacks");
        var cones = f.Activate("BossMod.Dawntrail.Ultimate.DMU.ForsakenBaitsCone");
        var helper = f.AddActor(0x300, (uint)OID.Helper, ActorType.Enemy, Class.None, new(100, 100));
        var pairSupport = initialSupportStack is 0 or 2 ? 0 : 1;
        var pairDps = initialDpsStack is 4 or 6 ? 4 : 5;
        var angle = (-rotation * 45f).Degrees();
        var allRoutesSafe = true;
        var allAssignmentsCorrect = true;
        var allHintPointsCorrect = true;
        var firstRouteFailure = "";
        var firstSafetyFailure = "";
        const float a = 5.656854f;
        for (var wave = 1; wave <= 8; ++wave)
        {
            var alternate = wave is >= 4 and <= 7;
            var tank = alternate ? 1 - pairSupport : pairSupport;
            var healer = tank + 2;
            var melee = alternate ? 9 - pairDps : pairDps;
            var ranged = melee + 2;
            int[] takers = [healer, tank, melee, ranged];
            var stackSupport = initialSupportStack < 2 ? tank : healer;
            var stackDps = initialDpsStack < 6 ? melee : ranged;
            uint[] icons = Enumerable.Range(0, 8).Select(slot => (slot < 4 == supportCone) ? (uint)IconID.TowerConeIcon : (uint)IconID.TowerSpreadIcon).ToArray();
            icons[stackSupport] = icons[stackDps] = (uint)IconID.TowerStackIcon;
            if ((wave & 1) == 0)
            {
                // Even waves resolve cones/spreads; stacks belong to odd-wave snapshots.
                icons[healer] = (uint)(stackSupport == healer ? IconID.TowerConeIcon : IconID.TowerSpreadIcon);
                icons[tank] = (uint)(stackSupport == tank ? IconID.TowerConeIcon : IconID.TowerSpreadIcon);
                icons[melee] = (uint)(stackDps == melee ? IconID.TowerConeIcon : IconID.TowerSpreadIcon);
                icons[ranged] = (uint)(stackDps == ranged ? IconID.TowerConeIcon : IconID.TowerSpreadIcon);
            }
            if (wave == 8)
            {
                icons[healer] = icons[ranged] = (uint)IconID.TowerSpreadIcon;
                icons[tank] = icons[melee] = (uint)IconID.TowerConeIcon;
            }
            for (var slot = 0; slot < 8; ++slot)
                f.World.Execute(new ActorState.OpIcon(f.World.Party[slot]!.InstanceID, icons[slot], 0));
            shapes.Update();
            if (wave == 8)
            {
                Check(Read<BitMask>(shapes, "swSoakers").None() && Read<BitMask>(shapes, "seSoakers").None(), "DMU Piren tower 8: no invented marker priority");
                f.World.Execute(new WaymarkState.OpSignChange(Sign.Ignore1, f.World.Party[healer]!.InstanceID));
                f.World.Execute(new WaymarkState.OpSignChange(Sign.Bind1, f.World.Party[melee]!.InstanceID));
                f.World.Execute(new WaymarkState.OpSignChange(Sign.Bind2, f.World.Party[tank]!.InstanceID));
                f.World.Execute(new WaymarkState.OpSignChange(Sign.Ignore2, f.World.Party[ranged]!.InstanceID));
                shapes.Update();
            }
            var left = Read<BitMask>(shapes, "swSoakers");
            var right = Read<BitMask>(shapes, "seSoakers");
            var expectedLeft = wave == 8 ? new BitMask((1ul << healer) | (1ul << melee))
                : (wave & 1) == 0 || supportCone ? new BitMask((1ul << healer) | (1ul << tank))
                : new BitMask((1ul << stackSupport) | (1ul << (icons[melee] == (uint)IconID.TowerConeIcon ? melee : ranged)));
            allAssignmentsCorrect &= left == expectedLeft && (left | right).NumSetBits() == 4 && (left & right).None();
            f.World.Execute(new WorldState.OpMapEffect((byte)((3 + rotation) % 8 + 1), 0x00020001));
            f.World.Execute(new WorldState.OpMapEffect((byte)((5 + rotation) % 8 + 1), 0x00020001));
            towers.Update();
            oddSolver.Update();
            evenSolver.Update();
            var expected = new WPos[8];
            for (var slot = 0; slot < 8; ++slot)
            {
                var odd = (wave & 1) != 0;
                var supportHelper = slot < 4 && !takers.Contains(slot);
                var dpsHelper = slot >= 4 && !takers.Contains(slot);
                WDir offset;
                if (supportHelper)
                    offset = slot is 2 or 3 ? (odd ? new(-a, a + 5.1f) : new(-a - 4.5f, a)) : (odd ? new(-2.5f, 2.8f) : new(-4.2f, -4.2f));
                else if (dpsHelper)
                    offset = odd ? new(2.4f, 2.8f) : slot is 4 or 5 ? new(4.2f, -4.2f) : new(a + 4.5f, a);
                else if (icons[slot] == (uint)IconID.TowerSpreadIcon)
                    offset = new(left[slot] ? -a : a, a + 3.8f);
                else if (icons[slot] == (uint)IconID.TowerConeIcon)
                    offset = new(left[slot] ? -a : a, odd ? a + 3 : 2);
                else
                    offset = left[slot] ? new(-a, a - 1) : new(4.2f, 2.8f);
                expected[slot] = f.Module.Center + offset.Rotate(angle);
                f.World.Execute(new ActorState.OpMove(f.World.Party[slot]!.InstanceID, new(expected[slot].X, 0, expected[slot].Z, 0)));
            }
            spreads.Update();
            cones.Update();
            foreach (var stack in ((UniformStackSpread)spreads).Stacks)
                allRoutesSafe &= f.World.Party.WithSlot().Count(p => p.Item2.Position.InCircle(stack.Target.Position, stack.Radius)) == 3;
            for (var slot = 0; slot < 8; ++slot)
            {
                var assignment = (PartyRolesConfig.Assignment)slot;
                var solver = (wave & 1) != 0 ? oddSolver : evenSolver;
                var navigation = f.Hints(solver, slot, assignment);
                var routeOK = navigation.ForbiddenZones.Count == 1 && navigation.ForbiddenZones.All(z => z.shapeDistance.Distance(expected[slot]) >= 0);
                if (!routeOK && firstRouteFailure.Length == 0)
                    firstRouteFailure = $"wave{wave} slot{slot} expected{expected[slot]} zones{navigation.ForbiddenZones.Count} distances[{string.Join(',', navigation.ForbiddenZones.Select(z => z.shapeDistance.Distance(expected[slot])))}]";
                allHintPointsCorrect &= routeOK;
                var combined = new AIHints();
                foreach (var component in f.Module.Components)
                    component.AddAIHints(slot, f.World.Party[slot]!, assignment, combined);
                var safetyOK = combined.ForbiddenZones.All(z => z.shapeDistance.Distance(expected[slot]) >= -0.001);
                if (!safetyOK && firstSafetyFailure.Length == 0)
                    firstSafetyFailure = $"wave{wave} slot{slot} point{expected[slot]} " + string.Join(';', f.Module.Components.Select(component =>
                    {
                        var componentHints = f.Hints(component, slot, assignment);
                        return component.GetType().Name + "[" + string.Join(',', componentHints.ForbiddenZones.Select(z => z.shapeDistance.Distance(expected[slot]))) + "]";
                    }));
                allRoutesSafe &= safetyOK;
            }
            foreach (var tower in towers.Towers.ToArray())
            {
                f.World.Execute(new ActorState.OpMove(helper.InstanceID, new(tower.Position.X, 0, tower.Position.Z, 0)));
                f.EventCast(helper, (uint)AID.ThePathOfLight);
            }
            shapes.Update();
        }
        var label = $"support {initialSupportStack}, DPS {initialDpsStack}, cone {supportCone}, rotation {rotation}";
        Check(allAssignmentsCorrect, "DMU Piren actual pairs/priority/markers all eight waves: " + label);
        Check(allHintPointsCorrect, "DMU Piren actual navigation all roles and waves: " + label + firstRouteFailure);
        Check(allRoutesSafe, "DMU Piren routes survive native tower/cone/stack/spread safety: " + label + firstSafetyFailure);

        static T Read<T>(object instance, string name) => (T)instance.GetType().GetField(name)!.GetValue(instance)!;
    }

    private static void CheckPirenCloneWait(bool future)
    {
        using var f = new Fixture("DMU");
        DMUParty(f);
        var shapes = f.Activate("BossMod.Dawntrail.Ultimate.DMU.ForsakenShapes");
        var towers = (GenericTowers)f.Activate("BossMod.Dawntrail.Ultimate.DMU.PathOfLight");
        var component = f.Activate("BossMod.Dawntrail.Ultimate.DMU.AllThingsEnding");
        var lockedCones = f.Activate("BossMod.Dawntrail.Ultimate.DMU.AllThingsEndingCasts");
        var clone = f.AddActor(0x400, (uint)OID.P2KefkaHelpers, ActorType.Enemy, Class.None, new(100, 100));
        f.World.Execute(new WorldState.OpMapEffect(4, 0x00020001));
        f.World.Execute(new WorldState.OpMapEffect(6, 0x00020001));
        component.Update();
        f.StartCast(clone, (uint)(future ? AID.FuturesEndCast : AID.PastsEndCast), 5);
        Check(f.Hints(component, 0).ForbiddenZones.Count == 0, $"DMU Piren {(future ? "future" : "past")}: no wait-route before clone snapshot");
        f.EventCast(clone, (uint)(future ? AID.FuturesEndSpread1 : AID.PastsEndSpread1));
        component.GetType().GetField("aoesLocked")!.SetValue(component, false); // Native timeline's post-snapshot activation callback.
        component.Update();
        var midpoint = towers.Towers[0].Position + (towers.Towers[1].Position - towers.Towers[0].Position) * 0.5f;
        var south = (midpoint - f.Module.Center).Normalized();
        var destination = future ? midpoint - south * 13 : midpoint + south * 2;
        var waitRoute = f.Hints(component, 0).ForbiddenZones.Where(z => z.shapeDistance is SDInvertedCircle).ToArray();
        Check(waitRoute.Length == 1 && waitRoute[0].shapeDistance.Distance(destination) >= 0, $"DMU Piren {(future ? "future" : "past")}: actual post-bait waiting side");
        Check(f.Hints(component, 0).ForbiddenZones.All(z => z.shapeDistance.Distance(destination) >= 0),
            "DMU Piren required pre-snapshot bait is not forbidden by its predicted kick cone");
        var castRotation = clone.AngleTo(destination) + (future ? default : 180f.Degrees());
        f.World.Execute(new ActorState.OpMove(clone.InstanceID, new Vector4(100, 0, 100, castRotation.Rad)));
        f.StartCast(clone, (uint)AID.AllThingsEnding, 5);
        component.Update();
        Check(!f.Hints(component, 0).ForbiddenZones.Any(z => z.shapeDistance is SDInvertedCircle), "DMU Piren kick cast releases waiting route for native cone avoidance");
        var kickHints = f.Hints(lockedCones, 0);
        Check(kickHints.ForbiddenZones.Count > 0 && kickHints.ForbiddenZones.All(z => z.shapeDistance.Distance(midpoint) >= 0),
            "DMU Piren locked native kick cone leaves the tower side safe");
        if (future)
            Check(kickHints.ForbiddenZones.Any(z => z.shapeDistance.Distance(destination) < 0),
                "DMU Piren future kick actually forbids old waiting side after snapshot");
    }
}
