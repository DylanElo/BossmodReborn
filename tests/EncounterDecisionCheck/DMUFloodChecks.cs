using BossMod;
using BossMod.Dawntrail.Ultimate.DMU;
using System.Numerics;

internal static partial class EncounterChecks
{
    private static void CheckFloodNavigation()
    {
        using var f = new Fixture("DMU");
        DMUParty(f);
        var flood = f.Activate("BossMod.Dawntrail.Ultimate.DMU.ChaoticFlood");
        f.Activate("BossMod.Dawntrail.Ultimate.DMU.ChaoticFloodStack");
        Service.Config.Get<DMUConfig>().YarnPirenStrategy = true;
        Check(f.Hints(flood, 0).ForbiddenZones.Count == 0, "DMU flood: unobserved waves do not route");
        Actor[] casters = new Actor[8];
        for (var i = 0; i < 8; ++i)
        {
            var position = i switch { < 2 => new WPos(100, 100), < 4 => new WPos(112, 100), < 6 => new WPos(100, 100), _ => new WPos(100, 112) };
            casters[i] = f.AddActor((ulong)(0x900 + i), 0, ActorType.Enemy, Class.None, position);
            if (i >= 4)
                f.World.Execute(new ActorState.OpMove(casters[i].InstanceID, new Vector4(position.X, 0, position.Z, MathF.PI / 2)));
            f.StartCast(casters[i], 49539, 1.5f);
        }
        flood.Update();
        for (var i = 0; i < 8; ++i) f.EventCast(casters[i], 49539);
        f.World.Execute(new ActorState.OpTarget(f.Boss.InstanceID, f.World.Party[0]!.InstanceID));
        f.StartCast(f.Boss, 49471, 5);
        var unsafeIndicator = new WPos(98, 100);
        Check(f.ModuleHints(0, PartyRolesConfig.Assignment.MT).ForbiddenZones.Any(z => z.shapeDistance.Distance(unsafeIndicator) < 0),
            "DMU flood: actual observed strip rejects original two-yalm direction indicator");
        var selected = new WPos(93.5f, 100);
        for (var i = 0; i < 8; ++i)
        {
            var zones = f.ModuleHints(i, (PartyRolesConfig.Assignment)i).ForbiddenZones;
            Check(zones.Count > 0 && zones.All(z => z.shapeDistance.Distance(selected) >= 0),
                $"DMU flood: selected wave-clear circle survives strips and eight-player stack for slot{i}");
        }
        f.EventCast(casters[0], 49769);
        f.EventCast(casters[1], 49769);
        selected = new(100, 106.5f);
        Check(f.ModuleHints(0, PartyRolesConfig.Assignment.MT).ForbiddenZones.All(z => z.shapeDistance.Distance(selected) >= 0),
            "DMU flood: actual pair of hits rotates into next observed wave-clear destination");
        Service.Config.Get<DMUConfig>().YarnPirenStrategy = false;
        Check(!f.Hints(flood, 0).ForbiddenZones.Any(z => z.shapeDistance is SDInvertedCircle), "DMU flood: disabled Yarn strategy preserves native hazard-only navigation");
        Service.Config.Get<DMUConfig>().YarnPirenStrategy = true;
        for (var i = 2; i < 8; ++i) f.EventCast(casters[i], 49769);
        Check(!f.Hints(flood, 0).ForbiddenZones.Any(z => z.shapeDistance is SDInvertedCircle), "DMU flood: eighth actual hit releases route");
        Service.Config.Get<DMUConfig>().YarnPirenStrategy = false;
    }
}
