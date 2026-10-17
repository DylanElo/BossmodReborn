using BossMod;
using DA = BossMod.Dawntrail.Ultimate.DMU.AID;
using DO = BossMod.Dawntrail.Ultimate.DMU.OID;

internal static partial class EncounterChecks
{
    static partial void CheckDMU()
    {
        CheckDMUForsakenBait();
        using var f = new Fixture("DMU");
        var kefka = f.AddActor(0x500, (uint)DO.KefkaP5, ActorType.Enemy, Class.None, new(100, 100));
        f.AddPlayer(0);
        var helper = f.AddActor(0x501, (uint)DO.Helper, ActorType.Enemy, Class.None, new(100, 100));
        var helper2 = f.AddActor(0x502, (uint)DO.Helper, ActorType.Enemy, Class.None, new(100, 100));
        f.EnterState(4, 0x04000000);
        f.Tick(); // Populate DMU's real phase-specific boss accessor.
        var states = f.States().ToArray();
        Check(states.Select(x => x.ID).Distinct().Count() == states.Length, "DMU unique native state IDs");
        Check(states.All(x => !x.Name.Contains("Unknown")), "DMU no unknown timeline placeholder");
        Check(f.State(0x01000230).Comment.Contains("UltimateEmbrace"), "DMU P2 final embrace has explicit stable ID");
        Check(!states.Any(x => x.ID == 0x01000222), "DMU P2 no duplicate-ID auto-increment artifact");
        Check((f.State(0x02000075).EndHint & StateMachine.StateHint.Tankbuster) != 0, "DMU TB damage exports forecast flag");

        CheckRepeater(0x04000010, 0x04000015, 0x04000020, 3.2);
        CheckRepeater(0x04000180, 0x04000185, 0x04000190, 4.0);

        f.EnterState(4, 0x04000310);
        f.Errors.Clear();
        Check(Math.Abs((f.Module.StateMachine.NextTransitionWithFlag(StateMachine.StateHint.Raidwide) - f.Now).TotalSeconds - 11.3) < 0.001,
            "DMU Forsaken forecast points to damage, not cast start");
        f.StartCast(kefka, (uint)DA.ForsakenCast, 10);
        f.Advance(1.3);
        Check(f.Module.StateMachine.ActiveState?.ID == 0x04000311, "DMU native Forsaken cast starts");
        f.Advance(10);
        Check(f.Module.StateMachine.ActiveState?.ID == 0x04000311, "DMU live Forsaken cast holds cast-end state");
        f.EventCast(kefka, (uint)DA.ForsakenCast);
        f.FinishCast(kefka);
        f.Advance(0);
        Check(f.Module.StateMachine.ActiveState?.ID == 0x04000320, "DMU first actual Forsaken damage starts bait wave");
        var baitStates = new uint[] { 0x04000320, 0x04000340, 0x04000360, 0x04000380 };
        var raidStates = new uint[] { 0x04000330, 0x04000350, 0x04000370 };
        var baitDelays = new[] { 6.0, 5.0, 5.0, 5.0 };
        var raidDelays = new[] { 2.2, 3.1, 3.2 };
        for (var wave = 0; wave < 4; ++wave)
        {
            f.Advance(baitDelays[wave]);
            f.EventCast(helper, (uint)DA.ForsakenGround);
            f.Advance(0);
            Check(f.Module.StateMachine.ActiveState?.ID == baitStates[wave], $"DMU wave {wave + 1} needs actual bait event; no timer/wrong-action advance");
            f.EventCast(helper, (uint)DA.ForsakenAOEBait);
            f.EventCast(helper2, (uint)DA.ForsakenAOEBait); // Extra same-wave helpers must not spend a future wave.
            f.Advance(0);
            if (wave < 3)
            {
                Check(f.Module.StateMachine.ActiveState?.ID == raidStates[wave], $"DMU bait wave {wave + 1} waits for distinct boss raidwide");
                f.Advance(raidDelays[wave]);
                Check(f.Module.StateMachine.ActiveState?.ID == raidStates[wave], $"DMU raidwide {wave + 2} waits for boss damage");
                f.EventCast(kefka, (uint)DA._Ability_Forsaken2);
                f.Advance(0);
                Check(f.Module.StateMachine.ActiveState?.ID == baitStates[wave + 1], $"DMU actual raidwide {wave + 2} resets bait-wave count");
            }
        }
        Check(f.Module.StateMachine.ActiveState?.ID == 0x04000390, "DMU four actual bait waves reach enrage start");
        Check(!Has("P5ForsakenBait") && !Has("P5ForsakenRaidWide") && !Has("P5ForsakenStack"), "DMU final bait and stack lifecycle ends");
        Check(Has("P5ForsakenGround"), "DMU permanent Forsaken ground persists into enrage");
        f.StartCast(kefka, (uint)DA._Ability_ForsakenNull, 26);
        f.Advance(5.2);
        Check(f.Module.StateMachine.ActiveState?.ID == 0x04000391, "DMU native Forsaken Null cast begins");
        f.Advance(26);
        Check(f.Module.StateMachine.ActiveState?.ID == 0x04000391, "DMU enrage does not finish while cast remains active");
        f.EventCast(kefka, (uint)DA._Ability_ForsakenNull);
        f.FinishCast(kefka);
        f.Advance(0);
        Check(f.Module.StateMachine.ActiveState?.ID == 0x04000391 && f.State(0x04000391).Update!(26) == 0,
            "DMU actual enrage cast end resolves terminal state without advancing a nonexistent phase");
        Check(f.Errors.Count == 0, "DMU final sequence has no overdue/missing-component fallback");
        f.Module.StateMachine.Reset();
        Check(f.Module.Components.Count == 0, "DMU reset clears permanent ground and counters");
        f.EnterState(4, 0x04000310);
        f.StartCast(kefka, (uint)DA.ForsakenCast, 10);
        f.Advance(1.3);
        f.EventCast(kefka, (uint)DA.ForsakenCast);
        f.FinishCast(kefka);
        f.Advance(10);
        f.Advance(6);
        Check(f.Module.StateMachine.ActiveState?.ID == 0x04000320, "DMU fresh run has no stale bait-wave count");

        bool Has(string name) => f.Module.Components.Any(x => x.GetType().Name == name);

        void CheckRepeater(uint castID, uint firstHitID, uint afterID, double delay)
        {
            f.EnterState(4, castID);
            f.Errors.Clear();
            Check(Math.Abs((f.Module.StateMachine.NextTransitionWithFlag(StateMachine.StateHint.Raidwide) - f.Now).TotalSeconds - delay - 5.1) < 0.001,
                $"DMU Repeater {castID:X} forecasts first damage after cast");
            f.StartCast(kefka, (uint)DA.UltimaRepeaterCast, 4);
            f.Advance(delay);
            f.Advance(4);
            f.FinishCast(kefka);
            f.Advance(0);
            Check(f.Module.StateMachine.ActiveState?.ID == firstHitID && Has("UltimaRepeater"), $"DMU Repeater {castID:X} survives visual cast end");
            var intervals = new[] { 1.1, 0.8, 0.7, 0.8 };
            for (var hit = 0; hit < 4; ++hit)
            {
                f.Advance(intervals[hit]);
                f.EventCast(helper, (uint)DA.ForsakenGround);
                f.Advance(0);
                Check(f.Module.StateMachine.ActiveState?.ID == firstHitID + hit, $"DMU Repeater {castID:X} hit {hit + 1} waits for correct event");
                f.EventCast(kefka, (uint)DA.UltimaRepeaterRaidwide);
                f.Advance(0);
                Check(f.Module.StateMachine.ActiveState?.ID == (hit == 3 ? afterID : firstHitID + hit + 1), $"DMU Repeater {castID:X} hit {hit + 1} advances real state");
            }
            Check(!Has("UltimaRepeater") && Has("FellForces"), $"DMU Repeater {castID:X} ends after exactly four hits");
            Check(f.Errors.Count == 0, $"DMU Repeater {castID:X} has no overdue/missing-component fallback");
            f.Module.StateMachine.Reset();
            Check(f.Module.Components.Count == 0, $"DMU Repeater {castID:X} reset clears all components");
        }
    }

    private static void CheckDMUForsakenBait()
    {
        using var f = new Fixture("DMU");
        var player = f.AddPlayer(0, Class.MCH, new(105, 100));
        var helper = f.AddActor(0x510, (uint)DO.Helper, ActorType.Enemy, Class.None, new(110, 100));
        var otherHelper = f.AddActor(0x511, (uint)DO.Helper, ActorType.Enemy, Class.None, new(90, 100));
        var component = f.Activate("BossMod.Dawntrail.Ultimate.DMU.P5ForsakenBait");
        component.Update();
        Check(f.Hints(component, 0).ForbiddenZones.Count == 0, "DMU Forsaken bait: unknown random target does not invent hazard");
        f.StartCast(helper, (uint)DA.ForsakenAOEBait, 5);
        component.Update();
        var first = f.Hints(component, 0).ForbiddenZones.Single();
        Check(first.shapeDistance.Distance(helper.Position) < 0 && first.shapeDistance.Distance(new(100, 110)) >= 0,
            "DMU Forsaken bait: actual helper ground cast supplies real 8y hazard");
        Check(Math.Abs((first.activation - f.Now).TotalSeconds - 5 - ActorCastInfo.NPCFinishDelay) < 0.001,
            "DMU Forsaken bait: hazard uses native helper snapshot deadline");
        f.EventCast(helper, (uint)DA.ForsakenGround);
        Check(f.Hints(component, 0).ForbiddenZones.Count == 1, "DMU Forsaken bait: unrelated ground event does not clear active cast");
        f.StartCast(otherHelper, (uint)DA.ForsakenAOEBait, 5);
        Check(f.Hints(component, 0).ForbiddenZones.Count == 2, "DMU Forsaken bait: concurrent observed helpers tracked independently");
        f.EventCast(helper, (uint)DA.ForsakenAOEBait);
        f.FinishCast(helper);
        Check(f.Hints(component, 0).ForbiddenZones.Count == 1 && f.Hints(component, 0).ForbiddenZones.Single().shapeDistance.Distance(otherHelper.Position) < 0,
            "DMU Forsaken bait: first helper resolution preserves second hazard");
        f.EventCast(otherHelper, (uint)DA.ForsakenAOEBait);
        f.FinishCast(otherHelper);
        component.Update();
        Check(f.Hints(component, 0).ForbiddenZones.Count == 0, "DMU Forsaken bait: completed casts release hazard without stale forecast");
        Check(((BossMod.Components.CastCounter)component).NumCasts == 2, "DMU Forsaken bait: actual effects retain timeline counter contract");
        f.StartCast(helper, (uint)DA.ForsakenAOEBait, 5);
        Check(f.Hints(component, 0).ForbiddenZones.Count == 1, "DMU Forsaken bait: later wave re-arms from observed cast");
        f.FinishCast(helper);
        Check(f.Hints(component, 0).ForbiddenZones.Count == 0, "DMU Forsaken bait: interrupted cast releases hazard");
    }
}
