namespace BossMod.Dawntrail.Savage.M12SLindwurm;

// Ability milestones follow OverlayPlugin/cactbot ui/raidboss/data/07-dt/raid/r12s.txt.
// Count events rather than assuming that the targetable boss also casts the helper actions.
sealed class TimelineEvents(BossModule module) : BossComponent(module)
{
    private readonly Dictionary<uint, DateTime> _lastCast = [];
    public DateTime Last(AID aid) => _lastCast.GetValueOrDefault((uint)aid);
    public override void OnEventCast(Actor caster, ActorCastEvent spell)
        => _lastCast[spell.Action.ID] = WorldState.CurrentTime;
}

sealed class M12SLindwurmStates : StateMachineBuilder
{
    public M12SLindwurmStates(BossModule module) : base(module)
    {
        DeathPhase(default, InitialPhase);
    }

    private State Event(uint id, float delay, AID aid, string name) => Events(id, delay, name, aid);

    private State Events(uint id, float delay, string name, params AID[] aids)
    {
        var notBefore = DateTime.MaxValue;
        // Helper emissions may repeat; only an event in this mechanic's own resolution window can advance it.
        return ComponentCondition<TimelineEvents>(id, delay, comp => aids.Any(aid => comp.Last(aid) >= notBefore), name, 30f)
            .OnEnter(() => notBefore = Module.WorldState.CurrentTime.AddSeconds(Math.Max(0, delay - 1f)));
    }

    private void InitialPhase(uint id)
    {
        ActivateMechanics(Event(id, 15.6f, AID.TheFixer, "The Fixer").SetHint(StateMachine.StateHint.Raidwide));
        Event(id + 0x10, 25.2f, AID.MortalSlayer0, "Mortal Slayer: baits");
        Events(id + 0x20, 9.1f, "Mortal Slayer: final baits", AID.MortalSlayer1, AID.MortalSlayer2);
        Event(id + 0x30, 20.1f, AID.GrotesquerieAct1, "Grotesquerie: Act 1");
        Event(id + 0x40, 9.2f, AID.PhagocyteSpotlight0, "Phagocyte Spotlights");
        Event(id + 0x50, 8.5f, AID.RavenousReach1, "Ravenous Reach");
        Event(id + 0x60, 0.3f, AID.FourthWallFusion0, "Fourth-wall Fusion + Dramatic Lysis");
        Event(id + 0x70, 8.7f, AID.Burst, "Burst");
        Event(id + 0x80, 0.6f, AID.FourthWallFusion2, "Fourth-wall Fusion + Visceral Burst");
        Event(id + 0x90, 10f, AID.TheFixer, "The Fixer").SetHint(StateMachine.StateHint.Raidwide);
        Event(id + 0x100, 12.2f, AID.GrotesquerieAct2, "Grotesquerie: Act 2");
        Event(id + 0x110, 9.2f, AID.PhagocyteSpotlight1, "Phagocyte Spotlights");
        Event(id + 0x120, 6f, AID.PhagocyteSpotlight1, "Cruel Coil + final spotlight");
        Event(id + 0x130, 6f, AID.Skinsplitter0, "Skinsplitter 1");
        Event(id + 0x140, 5f, AID.Skinsplitter0, "Skinsplitter 2 + tether 1");
        Event(id + 0x150, 5f, AID.Skinsplitter0, "Skinsplitter 3 + tether 2");
        Event(id + 0x160, 2f, AID.RoilingMass1, "Roiling Mass 1");
        Event(id + 0x170, 3f, AID.Skinsplitter0, "Skinsplitter 4 + tether 3");
        Event(id + 0x180, 2f, AID.RoilingMass1, "Roiling Mass 2");
        Event(id + 0x190, 3f, AID.Skinsplitter0, "Skinsplitter 5 + tether 4");
        Event(id + 0x1A0, 2f, AID.RoilingMass1, "Roiling Mass 3");
        Event(id + 0x1B0, 3f, AID.Skinsplitter0, "Skinsplitter 6");
        Event(id + 0x1C0, 2f, AID.RoilingMass1, "Roiling Mass 4");
        Event(id + 0x1D0, 3f, AID.Skinsplitter0, "Skinsplitter 7");
        Event(id + 0x1E0, 6f, AID.Constrictor3, "Constrictor");
        Event(id + 0x1F0, 11.5f, AID.Splattershed2, "Splattershed").SetHint(StateMachine.StateHint.Raidwide);
        Event(id + 0x200, 17.7f, AID.GrotesquerieAct3, "Grotesquerie: Act 3");
        Targetable(id + 0x210, false, 3.1f);
        Event(id + 0x220, 2f, AID.FeralFission, "Feral Fission");
        Events(id + 0x230, 4.6f, "Bring Down the House", AID.BringDownTheHouse0, AID.BringDownTheHouse1);
        Event(id + 0x240, 2.4f, AID.DramaticLysis1, "Dramatic Lysis");
        Targetable(id + 0x250, true, 2.6f);
        Event(id + 0x260, 7.4f, AID.SplitScourge1, "Split Scourge");
        Event(id + 0x270, 2.4f, AID.VenomousScourge, "Venomous Scourge");
        Event(id + 0x280, 9.2f, AID.TheFixer, "The Fixer").SetHint(StateMachine.StateHint.Raidwide);
        Event(id + 0x290, 12.1f, AID.GrotesquerieCurtainCall, "Grotesquerie: Curtain Call");
        Event(id + 0x2A0, 14.8f, AID.RavenousReach1, "Ravenous Reach + Cell Shedding");
        Event(id + 0x2B0, 12.9f, AID.Burst, "Burst");
        Event(id + 0x2C0, 9.1f, AID.Splattershed2, "Splattershed").SetHint(StateMachine.StateHint.Raidwide);
        Event(id + 0x2D0, 25.8f, AID.MortalSlayer0, "Mortal Slayer: baits");
        Events(id + 0x2E0, 9.1f, "Mortal Slayer: final baits", AID.MortalSlayer1, AID.MortalSlayer2);
        SlaughtershedCast(id + 0x2F0, 15.1f);

        // Keep 0x10000 entering on the first Slaughtershed cast event, as in existing plans.
        SlaughtershedSequence(id + 0x10000, 1);
        SlaughtershedCast(id + 0x10100, 6.6f);
        SlaughtershedSequence(id + 0x20000, 2);
        SlaughtershedCast(id + 0x20100, 6.7f);
        SlaughtershedSequence(id + 0x30000, 3);

        // The ending depends on HP; B538 can resolve as the transition raidwide or as enrage.
        ComponentCondition<TimelineEvents>(id + 0x40000, 18.1f,
            comp => comp.Last(AID.RefreshingOverkill0) != default || comp.Last(AID.RefreshingOverkill1) != default || comp.Last(AID.RefreshingOverkill2) != default,
            "Refreshing Overkill (HP-dependent)", float.MaxValue);
        Targetable(id + 0x40010, false, 0.1f, "Transition / enrage");
        SimpleState(id + 0x40020, 6f, "Transition / enrage resolution");
    }

    private State SlaughtershedCast(uint id, float delay)
        => Events(id, delay, "Slaughtershed (cast)", AID.Slaughtershed1, AID.Slaughtershed2);

    private void SlaughtershedSequence(uint id, int sequence)
    {
        Event(id, 2.4f, AID.Slaughtershed0, $"Slaughtershed {sequence}")
            .SetHint(StateMachine.StateHint.Raidwide)
            .ActivateOnEnter<SerpentineScourge>(sequence == 1)
            .ActivateOnEnter<RaptorKnuckles>(sequence == 1);
        Event(id + 0x10, 8.7f, AID.FourthWallFusion1, "Fourth-wall Fusion + Dramatic Lysis");
        Event(id + 0x20, 0.4f, AID.Burst, "Burst");
        Events(id + 0x30, 6.1f, "Cleave / knockback 1", AID.SerpentineScourge2, AID.RaptorKnuckles2);
        Events(id + 0x40, sequence == 1 ? 4.7f : 4.6f, "Cleave / knockback 2", AID.SerpentineScourge2, AID.RaptorKnuckles2);
    }

    private void ActivateMechanics(State state)
    {
        state.ActivateOnEnter<TimelineEvents>()
        .ActivateOnEnter<Slaughtershed>()
        .ActivateOnEnter<TheFixer>()
        .ActivateOnEnter<MortalSlayer>()
        .ActivateOnEnter<BurstingGrotesquerieAct1>()
        .ActivateOnEnter<SharedGrotesquerieAct1>()
        .ActivateOnEnter<DirectedGrotesquerieAct1>()
        .ActivateOnEnter<PhagocyteSpotlight0>()
        .ActivateOnEnter<RavenousReach1>()
        .ActivateOnEnter<DramaticLysis0>()
        .ActivateOnEnter<FourthWallFusion0>()
        .ActivateOnEnter<Burst>()
        .ActivateOnEnter<VisceralBurst>()
        .ActivateOnEnter<FourthWallFusion2>()
        .ActivateOnEnter<GrotesquerieAct2>()
        .ActivateOnEnter<PhagocyteSpotlight1>()
        .ActivateOnEnter<CruelCoil>()
        .ActivateOnEnter<unk_46194>()
        .ActivateOnEnter<DramaticLysis2>()
        .ActivateOnEnter<UnmitigatedExplosion>()
        .ActivateOnEnter<Constrictor>()
        .ActivateOnEnter<Splattershed2>()
        .ActivateOnEnter<GrotesquerieAct3>()
        .ActivateOnEnter<GrandEntrance1>()
        .ActivateOnEnter<GrandEntrance3>()
        .ActivateOnEnter<GrandEntrance0>()
        .ActivateOnEnter<BringDownTheHouse0>()
        .ActivateOnEnter<BringDownTheHouse1>()
        .ActivateOnEnter<DramaticLysis1>()
        .ActivateOnEnter<SplitScourge1>()
        .ActivateOnEnter<GrandEntrance2>()
        .ActivateOnEnter<BringDownTheHouse2>()
        .ActivateOnEnter<GrotesquerieCurtainCall>()
        .ActivateOnEnter<Slaughtershed0>()
        .ActivateOnEnter<DramaticLysis4>()
        .ActivateOnEnter<DramaticLysis>()
        .ActivateOnEnter<FourthWallFusion1>()
        .ActivateOnEnter<RaptorKnuckles2>()
        .ActivateOnEnter<SerpentineScourge2>()
        .ActivateOnEnter<RefreshingOverkill2>()
        .ActivateOnEnter<RefreshingOverkill1>()
        .ActivateOnEnter<unk_48028>()
        .ActivateOnEnter<unk_46395>()
        .ActivateOnEnter<ArcadiaAflame>()
        .ActivateOnEnter<TopTierSlam1>()
        .ActivateOnEnter<WingedScourge2>()
        .ActivateOnEnter<MightyMagic1>()
        .ActivateOnEnter<EsotericFinisher>()
        .ActivateOnEnter<FirefallSplash1>()
        .ActivateOnEnter<ManaBurst1>()
        .ActivateOnEnter<HeavySlam0>()
        .ActivateOnEnter<UnmitigatedImpact>()
        .ActivateOnEnter<HeavySlam2>();
    }

}