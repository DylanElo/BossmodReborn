using System.Numerics;
using BossMod;
using M9 = BossMod.Dawntrail.Savage.M09SVampFatale;
using M10 = BossMod.Dawntrail.Savage.M10STheXtremes;
using M11 = BossMod.Dawntrail.Savage.M11STheTyrant;
using M12 = BossMod.Dawntrail.Savage.M12SLindwurm;

internal static partial class EncounterChecks
{
    static partial void CheckSavageNavigation()
    {
        RunGroup("M9 navigation", CheckM9Navigation);
        RunGroup("M10 navigation", CheckM10Navigation);
        RunGroup("M11 navigation", CheckM11Navigation);
        RunGroup("M11 weapon navigation", CheckM11WeaponNavigation);
        RunGroup("M12 navigation", CheckM12Navigation);
    }

    private static void AssignNavigationParty(Fixture f, WPos[]? positions = null)
    {
        var roles = Service.Config.Get<PartyRolesConfig>();
        for (var slot = 0; slot < 8; ++slot)
        {
            f.AddPlayer(slot, slot < 2 ? Class.WAR : slot < 4 ? Class.WHM : slot < 6 ? Class.VPR : Class.MCH, positions?[slot]);
            roles.Assignments[(ulong)(slot + 1)] = (PartyRolesConfig.Assignment)slot;
        }
    }

    private static float NavigationGoal(AIHints hints, WPos point) => hints.GoalZones.Sum(goal => goal(point));
    private static bool NavigationForbidden(AIHints hints, WPos point) => hints.ForbiddenZones.Any(zone => zone.shapeDistance.Contains(point));
    private static void StartNavigationCast(Fixture f, Actor caster, uint action, float duration, Angle rotation = default)
        => f.World.Execute(new ActorState.OpCastInfo(caster.InstanceID, new() { Action = new(ActionType.Spell, action), TotalTime = duration, Location = new Vector3(caster.Position.X, 0, caster.Position.Z), Rotation = rotation }));
    private static void NavigationStage(BossComponent component, string value)
    {
        var field = component.GetType().GetField("CurrentState")!;
        field.SetValue(component, Enum.Parse(field.FieldType, value));
    }
    private static void NavigationDash(Fixture f, Actor caster, uint action)
        => f.World.Execute(new ActorState.OpCastEvent(caster.InstanceID, new(new(ActionType.Spell, action), 0, 0, 1, new Vector3(caster.Position.X, 0, caster.Position.Z), 0, 0, caster.Rotation)));

    private static void CheckM9Navigation()
    {
        using var f = new Fixture("M09SVampFatale");
        AssignNavigationParty(f);
        var config = Service.Config.Get<M9.M09SVampFataleConfig>();
        config.EnableTowerOrder = true;
        var towers = f.Activate("BossMod.Dawntrail.Savage.M09SVampFatale.BloodyBondage");
        WPos[] spots = [new(100, 84), new(116, 100), new(100, 116), new(84, 100)];
        foreach (var index in new[] { 2, 0, 3, 1 })
        {
            var caster = f.AddActor((ulong)(0x300 + index), 0, ActorType.Enemy, Class.None, spots[index]);
            f.World.Execute(new ActorState.OpCastInfo(caster.InstanceID, new() { Action = new(ActionType.Spell, (uint)M9.AID.BloodyBondage), TotalTime = 8, Location = new Vector3(spots[index].X, 0, spots[index].Z) }));
        }
        foreach (var strategy in new[] { M9.M09SVampFataleConfig.AssignmentStrategy.Hector, M9.M09SVampFataleConfig.AssignmentStrategy.Game8 })
        {
            config.Assignments = strategy;
            var order = strategy == M9.M09SVampFataleConfig.AssignmentStrategy.Game8 ? new[] { 0, 3, 1, 2 } : new[] { 0, 1, 2, 3 };
            foreach (var slot in new[] { 0, 2, 4, 6 })
            {
                var hints = f.Hints(towers, slot, (PartyRolesConfig.Assignment)slot);
                var mine = spots[order[slot / 2]];
                if (strategy == M9.M09SVampFataleConfig.AssignmentStrategy.Game8)
                {
                    Check(!NavigationForbidden(hints, mine) && spots.Where(p => p != mine).All(p => NavigationForbidden(hints, p)), $"M9 Game8: role {slot} assigned tower legal and other towers forbidden");
                    Check(hints.GoalZones.Count == 0 && hints.ForbiddenZones.All(z => Math.Abs((z.activation - f.Now).TotalSeconds - 8 - ActorCastInfo.NPCFinishDelay) < 0.001), $"M9 Game8: role {slot} travels by actual cast deadline without immediate uptime override");
                }
                else
                    Check(NavigationGoal(hints, mine) > NavigationGoal(hints, spots[(order[slot / 2] + 1) % 4]), $"M9 {strategy}: real tower AI assigns role {slot} after permuted cast creation");
            }
            Check(NavigationForbidden(f.Hints(towers, 1), spots[0]), $"M9 {strategy}: second lightparty avoids first-wave towers");
        }
        config.EnableStaticAetherlettingPuddle = true;
        var puddles = f.Activate("BossMod.Dawntrail.Savage.M09SVampFatale.AetherlettingPuddle");
        for (var slot = 0; slot < 8; ++slot)
        {
            var caster = f.AddActor((ulong)(0x380 + slot), 0, ActorType.Enemy, Class.None, new(100, 100));
            f.StartCast(caster, (uint)M9.AID.AetherlettingPuddle, 5, f.World.Party[slot]!.InstanceID);
        }
        config.Assignments = M9.M09SVampFataleConfig.AssignmentStrategy.Game8;
        int[] jpIndices = [4, 3, 6, 1, 7, 2, 5, 0];
        float[] angles = [157.5f, 112.5f, 67.5f, 22.5f, -157.5f, -112.5f, -67.5f, -22.5f];
        for (var slot = 0; slot < 8; ++slot)
        {
            var hints = f.Hints(puddles, slot, (PartyRolesConfig.Assignment)slot);
            var mine = f.Module.Center + 18f * angles[jpIndices[slot]].Degrees().ToDirection();
            Check(NavigationGoal(hints, mine) > NavigationGoal(hints, f.Module.Center), $"M9 Game8: actual spread caster assigns JP role {slot} edge bait");
        }
        config.EnableStaticAetherlettingPuddle = false;
        config.EnableTowerOrder = false;
        config.EnableDeathmatch = true;
        var deathmatch = f.Activate("BossMod.Dawntrail.Savage.M09SVampFatale.BloodyBondageUndeadDeathmatch");
        WPos[][] deathmatchSpots = [[new(100, 84), new(100, 116)], [new(84, 100), new(116, 100)]];
        for (var variant = 0; variant < deathmatchSpots.Length; ++variant)
        {
            var helpers = new Actor[2];
            foreach (var index in new[] { 1, 0 })
            {
                var spot = deathmatchSpots[variant][index];
                helpers[index] = f.AddActor((ulong)(0x3A0 + variant * 2 + index), 0, ActorType.Enemy, Class.None, spot);
                f.World.Execute(new ActorState.OpCastInfo(helpers[index].InstanceID, new() { Action = new(ActionType.Spell, (uint)M9.AID.BloodyBondageUndeadDeathmatch), TotalTime = 8, Location = new Vector3(spot.X, 0, spot.Z) }));
            }
            for (var slot = 0; slot < 8; ++slot)
            {
                var hints = f.Hints(deathmatch, slot, (PartyRolesConfig.Assignment)slot);
                Check(NavigationGoal(hints, deathmatchSpots[variant][slot % 2]) > NavigationGoal(hints, deathmatchSpots[variant][1 - slot % 2]), $"M9 Game8 Deathmatch axis {variant}: role {slot} actual tower output respects MT-group north/west, ST-group south/east");
            }
            foreach (var helper in helpers)
                f.FinishCast(helper);
        }
        config.EnableDeathmatch = false;
    }

    private static void CheckM10Navigation()
    {
        using var f = new Fixture("M10STheXtremes");
        AssignNavigationParty(f);
        var config = Service.Config.Get<M10.M10STheXtremesConfig>();
        config.HintOption = M10.Strategy.game8;
        config.EnableStrategyAI = true;
        f.World.Execute(new ActorState.OpMove(f.Boss.InstanceID, new Vector4(100, 0, 80, 0)));
        var floater = f.Activate("BossMod.Dawntrail.Savage.M10STheXtremes.FlameFloater");
        f.StartCast(f.Boss, (uint)M10.AID.FlameFloaterCast, 7);
        f.Status(f.World.Party[0]!, 0, new((uint)M10.SID.FirstInLine, 0, f.Now.AddSeconds(15), 0));
        f.Status(f.World.Party[1]!, 0, new((uint)M10.SID.SecondInLine, 0, f.Now.AddSeconds(15), 0));
        Check(f.Hints(floater, 0).GoalZones.Count == 1, "M10 Game8: actual first tether receives bait goal");
        Check(f.Hints(floater, 1).GoalZones.Count == 0, "M10 Game8: future tether does not cross current dash early");
        Check(NavigationForbidden(f.Hints(floater, 0), f.Boss.Position), "M10 Game8: current tether must stretch away from live boss");
        f.AddActor(0x400, (uint)M10.OID.PuddleFlameFloater, ActorType.EventObj, Class.None, new(100, 80));
        Check(f.Hints(floater, 0).GoalZones.Count == 0 && f.Hints(floater, 1).GoalZones.Count == 1, "M10 Game8: actual first puddle hands route to second tether");
        config.EnableStrategyAI = false;
        Check(f.Hints(floater, 1).GoalZones.Count == 0, "M10: disabled strategy AI preserves generic avoidance");
        config.EnableStrategyAI = true;
        f.Activate("BossMod.Dawntrail.Savage.M10STheXtremes.DebuffTracker");
        var fire = f.Activate("BossMod.Dawntrail.Savage.M10STheXtremes.AlleyOopInferno");
        NavigationStage(fire, "Snaking");
        for (var slot = 0; slot < 8; ++slot)
            f.Status(f.World.Party[slot]!, 1, new(slot % 2 == 0 ? (uint)M10.SID.Firesnaking : (uint)M10.SID.Watersnaking, 0, f.Now.AddSeconds(30), 0));
        foreach (var slot in new[] { 0, 2, 4, 6 })
        {
            var helper = f.AddActor((ulong)(0x440 + slot), 0, ActorType.Enemy, Class.None, new(100, 100));
            f.StartCast(helper, (uint)M10.AID.AlleyOopInferno, 5, f.World.Party[slot]!.InstanceID);
            var hints = f.Hints(fire, slot, (PartyRolesConfig.Assignment)slot);
            var z = slot switch { 0 => 99f, 2 => 111f, 4 => 105f, _ => 93f };
            Check(NavigationGoal(hints, new(119, z)) > NavigationGoal(hints, new(100, 100)), $"M10 Game8: live fire spread role {slot} uses R/T/M/H wall ordering");
            Check(!NavigationForbidden(hints, new(119, z)) && NavigationForbidden(hints, new(100, 90)) && hints.ForbiddenZones.Any(zone => zone.shapeDistance is SDInvertedCircle && Math.Abs((zone.activation - f.Now).TotalSeconds - 5 - ActorCastInfo.NPCFinishDelay) < 0.001), $"M10 Game8: fire role {slot} edge route is required by native spread deadline even while casting");
        }
        Check(f.Hints(fire, 1, PartyRolesConfig.Assignment.OT).GoalZones.Count == 0, "M10: water debuff does not receive fire route");
        NavigationStage(fire, "None");
        Check(f.Hints(fire, 0, PartyRolesConfig.Assignment.MT).GoalZones.Count == 0, "M10: inactive fire stage releases route despite old spread markers");
        var blue = f.AddActor(0x460, (uint)M10.OID.DeepBlue, ActorType.Enemy, Class.None, new(85, 85));
        var water = f.Activate("BossMod.Dawntrail.Savage.M10STheXtremes.AlleyOopWater");
        NavigationStage(water, "Snaking");
        f.StartCast(blue, (uint)M10.AID.AlleyOopDoubleDipCast, 5);
        foreach (var slot in new[] { 1, 3, 5, 7 })
        {
            Check(f.Hints(water, slot, (PartyRolesConfig.Assignment)slot).GoalZones.Count == 1, $"M10 Game8: active water cone role {slot} receives stage route");
            Check(f.Hints(water, slot, (PartyRolesConfig.Assignment)slot).ForbiddenZones.Any(zone => zone.shapeDistance is SDInvertedCircle && Math.Abs((zone.activation - f.Now).TotalSeconds - 5 - ActorCastInfo.NPCFinishDelay) < 0.001), $"M10 Game8: water role {slot} route uses observed cone snapshot deadline");
        }
        Check(f.Hints(water, 0, PartyRolesConfig.Assignment.MT).GoalZones.Count == 0, "M10: fire debuff does not receive water route");
        f.EventCast(blue, (uint)M10.AID.AlleyOopDoubleDip1);
        Check(f.Hints(water, 1, PartyRolesConfig.Assignment.OT).GoalZones.Count == 0, "M10: cone snapshot releases route before after-cones");
        config.EnableStrategyAI = false;
    }

    private static void CheckM11Navigation()
    {
        using var f = new Fixture("M11STheTyrant");
        const float offset = 4.596194f;
        WPos[] destinations = [new(100 + offset, 87 - offset), new(100 - offset, 113 + offset), new(87 - offset, 100 - offset), new(113 + offset, 100 + offset), new(87 - offset, 100 + offset), new(100 + offset, 113 + offset), new(100 - offset, 87 - offset), new(113 + offset, 100 - offset)];
        AssignNavigationParty(f, destinations);
        var cones = f.Activate("BossMod.Dawntrail.Savage.M11STheTyrant.MaelstromGustCones");
        f.Activate("BossMod.Dawntrail.Savage.M11STheTyrant.MaelstromVoidZones");
        var routes = f.Activate("BossMod.Dawntrail.Savage.M11STheTyrant.MaelstromBaitSafeSpots");
        WPos[] sources = [new(100, 87), new(113, 100), new(100, 113), new(87, 100)];
        for (var index = 0; index < sources.Length; ++index)
            f.AddActor((ulong)(0x500 + index), (uint)M11.OID.Maelstrom, ActorType.Enemy, Class.None, sources[index]);
        routes.Update();
        for (var slot = 0; slot < 8; ++slot)
        {
            var hints = f.ModuleHints(slot, (PartyRolesConfig.Assignment)slot);
            Check(NavigationGoal(hints, destinations[slot]) > NavigationGoal(hints, new(100, 100)), $"M11: role {slot} receives actual cardinal tornado bait goal");
            Check(!NavigationForbidden(hints, destinations[slot]), $"M11: role {slot} own bait is legal with live cones and voidzones combined");
        }
        f.EventCast(f.Boss, (uint)M11.AID.PowerfulGust);
        Check(f.Hints(routes, 0).GoalZones.Count == 0 && f.Hints(cones, 0).ForbiddenZones.Count == 0, "M11: Gust resolve releases authored route and cones");
    }

    private static void CheckM12Navigation()
    {
        using var f = new Fixture("M12SLindwurm");
        AssignNavigationParty(f);
        var reach = f.Activate("BossMod.Dawntrail.Savage.M12SLindwurm.RavenousReach1");
        var curtain = f.Activate("BossMod.Dawntrail.Savage.M12SLindwurm.GrotesquerieCurtainCall");
        StartNavigationCast(f, f.Boss, (uint)M12.AID.RavenousReach1, 10.3f);
        var player = f.World.Party[0]!;
        f.Status(player, 0, new((uint)M12.SID.RottingFlesh, 0, f.Now.AddSeconds(12), 0));
        var inside = new WPos(100, 110);
        var outside = new WPos(110, 100);
        var required = f.ModuleHints(0, PartyRolesConfig.Assignment.MT);
        Check(!NavigationForbidden(required, inside) && NavigationForbidden(required, outside), "M12: real Rotting Flesh status requires taking live cone");
        Check(NavigationForbidden(f.ModuleHints(1, PartyRolesConfig.Assignment.OT), inside), "M12: player without Rotting Flesh still avoids same live cone");
        foreach (var degrees in new[] { 90f, 180f, 270f })
        {
            f.FinishCast(f.Boss);
            var rotation = degrees.Degrees();
            StartNavigationCast(f, f.Boss, (uint)M12.AID.RavenousReach1, 10.3f, rotation);
            var hit = f.Module.Center + 10f * rotation.ToDirection();
            var miss = f.Module.Center + 10f * (rotation + 90f.Degrees()).ToDirection();
            var hints = f.ModuleHints(0, PartyRolesConfig.Assignment.MT);
            Check(!NavigationForbidden(hints, hit) && NavigationForbidden(hints, miss), $"M12: combined required-hit hint rotates with actual {degrees} degree cone");
        }
        f.FinishCast(f.Boss);
        StartNavigationCast(f, f.Boss, (uint)M12.AID.RavenousReach1, 10.3f);
        f.Status(player, 1, new((uint)M12.SID.BondsOfFleshAlpha, 0, f.Now.AddSeconds(3), 0));
        Check(f.Hints(curtain, 0).ForbiddenZones.Count == 0, "M12: pending chains do not pull uncleansed player out of required cone");
        f.Status(player, 0, default);
        Check(NavigationForbidden(f.Hints(curtain, 0), new(115, 100)), "M12: after cleanse regroup before actual chain transformation");
        Check(NavigationForbidden(f.Hints(reach, 0), inside), "M12: losing cleanse status immediately restores generic cone safety");
        f.EventCast(f.Boss, (uint)M12.AID.RavenousReach1);
        f.FinishCast(f.Boss);
        var sludge = f.Activate("BossMod.Dawntrail.Savage.M12SLindwurm.DramaticLysis1");
        var helper = f.AddActor(0x600, 0, ActorType.Enemy, Class.None, new(100, 100));
        StartNavigationCast(f, helper, (uint)M12.AID.DramaticLysis1, 5);
        foreach (var slot in Enumerable.Range(0, 8))
        {
            var actor = f.World.Party[slot]!;
            f.Status(actor, 1, default);
            f.Status(actor, 2, new(slot < 4 ? (uint)M12.SID.UnbreakableFlesh0 : (uint)M12.SID.UnbreakableFlesh1, 0, f.Now.AddSeconds(10), 0));
            var hints = f.ModuleHints(slot, (PartyRolesConfig.Assignment)slot);
            var west = new WPos(82, 88 + 8 * (slot % 4));
            var east = new WPos(118, 88 + 8 * (slot % 4));
            var destination = slot < 4 ? west : east;
            Check(slot < 4 ? NavigationGoal(hints, west) > NavigationGoal(hints, east) : NavigationGoal(hints, east) > NavigationGoal(hints, west), $"M12: actual chain status role {slot} splits to correct side without guessed partner");
            Check(!NavigationForbidden(hints, destination) && NavigationForbidden(hints, new(100, 100)), $"M12: role {slot} chain destination is legal after cone resolves with live sludge hazard");
            f.Status(actor, 2, default);
            Check(f.Hints(curtain, slot, (PartyRolesConfig.Assignment)slot).GoalZones.Count == 0, $"M12: role {slot} chain loss releases route for immediate explosion escape");
        }
    }

    private static void CheckM11WeaponNavigation()
    {
        using var f = new Fixture("M11STheTyrant");
        AssignNavigationParty(f);
        f.Activate("BossMod.Dawntrail.Savage.M11STheTyrant.AssaultWeaponTimeline");
        f.Activate("BossMod.Dawntrail.Savage.M11STheTyrant.AssaultEvolvedSword");
        f.Activate("BossMod.Dawntrail.Savage.M11STheTyrant.AssaultEvolvedAxeStack");
        f.Activate("BossMod.Dawntrail.Savage.M11STheTyrant.AssaultEvolvedScythe");
        var floor = f.Activate("BossMod.Dawntrail.Savage.M11STheTyrant.AssaultFloorPredictor");
        var route = f.Activate("BossMod.Dawntrail.Savage.M11STheTyrant.AssaultWeaponSafeSpots");
        StartNavigationCast(f, f.Boss, (uint)M11.AID.UltimateTrophyWeapons, 2);
        var weapons = new Actor[3];
        for (var i = 0; i < weapons.Length; ++i)
        {
            weapons[i] = f.AddActor((ulong)(0x700 + i), 0, ActorType.Enemy, Class.None, new(100, 100));
            f.World.Execute(new ActorState.OpMove(weapons[i].InstanceID, new Vector4(100, 0, 100, (i * 90f).Degrees().Rad)));
            f.World.Execute(new ActorState.OpPlayActionTimelineEvent(weapons[i].InstanceID, (ushort)(0x11D1 + i)));
        }
        f.FinishCast(f.Boss);
        StartNavigationCast(f, f.Boss, (uint)M11.AID.AssaultApex, 2);
        route.Update();
        WPos[] swordSpots = Enumerable.Range(0, 8).Select(slot => f.Module.Center + 8f * (slot % 2 == 0 ? 135f : -135f).Degrees().ToDirection()).ToArray();
        for (var slot = 0; slot < 8; ++slot)
            f.World.Execute(new ActorState.OpMove(f.World.Party[slot]!.InstanceID, new Vector4(swordSpots[slot].X, 0, swordSpots[slot].Z, 0)));
        NavigationDash(f, weapons[0], (uint)M11.AID.AssaultEvolved_SwordDash);
        StartNavigationCast(f, weapons[0], (uint)M11.AID.AssaultEvolved_SwordCross, 5);
        floor.Update();
        route.Update();
        for (var slot = 0; slot < 8; ++slot)
        {
            var hints = f.ModuleHints(slot, (PartyRolesConfig.Assignment)slot);
            Check(!NavigationForbidden(hints, swordSpots[slot]) && NavigationForbidden(hints, swordSpots[slot ^ 1]), $"M11 Sword role {slot}: assigned healer line legal, opposite lightparty line forbidden with cross active");
            Check(NavigationGoal(hints, swordSpots[slot]) > NavigationGoal(hints, new(100, 100)), $"M11 Sword role {slot}: native timeline selects matching lightparty goal");
        }
        f.Advance(5.4);
        f.FinishCast(weapons[0]);
        route.Update();
        var axeSpot = new WPos(100, 109);
        NavigationDash(f, weapons[1], (uint)M11.AID.AssaultEvolved_AxeDash);
        StartNavigationCast(f, weapons[1], (uint)M11.AID.AssaultEvolved_AxeAOE, 5);
        floor.Update();
        route.Update();
        Check(NavigationGoal(f.ModuleHints(0, PartyRolesConfig.Assignment.MT), axeSpot) > 0 && !NavigationForbidden(f.ModuleHints(0, PartyRolesConfig.Assignment.MT), axeSpot), "M11 Axe: native sword resolution advances to legal party-stack goal outside axe circle");
        f.Advance(5.4);
        f.FinishCast(weapons[1]);
        f.EventCast(weapons[1], (uint)M11.AID.AssaultEvolved_HeavyWeight);
        route.Update();
        float[] offsets = [0, 180, 90, -90, 135, -135, 45, -45];
        WPos[] scytheSpots = offsets.Select(angle => f.Module.Center + 3.5f * (180f + angle).Degrees().ToDirection()).ToArray();
        for (var slot = 0; slot < 8; ++slot)
            f.World.Execute(new ActorState.OpMove(f.World.Party[slot]!.InstanceID, new Vector4(scytheSpots[slot].X, 0, scytheSpots[slot].Z, 0)));
        NavigationDash(f, weapons[2], (uint)M11.AID.AssaultEvolved_ScytheDash);
        StartNavigationCast(f, weapons[2], (uint)M11.AID.AssaultEvolved_ScytheDonut, 5, 180f.Degrees());
        floor.Update();
        route.Update();
        for (var slot = 0; slot < 8; ++slot)
        {
            var hints = f.ModuleHints(slot, (PartyRolesConfig.Assignment)slot);
            Check(!NavigationForbidden(hints, scytheSpots[slot]) && NavigationForbidden(hints, scytheSpots[(slot + 1) % 8]), $"M11 Scythe role {slot}: own cone legal, other player's cone forbidden inside donut");
            Check(NavigationGoal(hints, scytheSpots[slot]) > NavigationGoal(hints, new(100, 100)), $"M11 Scythe role {slot}: live weapon rotation selects correct spread goal");
        }
        f.Advance(5.4);
        f.FinishCast(weapons[2]);
        route.Update();
        Check(f.Hints(route, 0, PartyRolesConfig.Assignment.MT).GoalZones.Count == 0, "M11: final weapon resolve releases authored movement");
    }
}
