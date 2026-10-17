namespace BossMod.Dawntrail.Savage.M11STheTyrant;

sealed class M11STheTyrantStates : StateMachineBuilder
{
    public M11STheTyrantStates(BossModule module) : base(module)
    {
        DeathPhase(default, SinglePhase)
            .ActivateOnEnter<ArenaChanges>()
            .ActivateOnEnter<TimelineEvents>();
    }

    // Event timings: OverlayPlugin/cactbot ui/raidboss/data/07-dt/raid/r11s.txt.
    // Its branch label offsets are virtual; both weapon orders rejoin at the same elapsed times.
    private void SinglePhase(uint id)
    {
        CrownOfArcadia(id, 5.6f, 5f, 1); // 10.6
        Event(id + 0x10u, 7.1f, "Raw Steel Trophy (axe/scythe)", AID.RawSteelTrophyAxe, AID.RawSteelTrophyScythe);
        Event(id + 0x1000u, 7.4f, "Raw Steel resolves", AID.RawSteel_Impact, AID.RawSteel_HeavyHitter)
            .SetHint(StateMachine.StateHint.Tankbuster);
        Event(id + 0x1010u, 13.3f, "Trophy Weapons", AID.TrophyWeapons);
        Event(id + 0x1020u, 9.8f, "Assault Evolved: three weapons", AID.AssaultEvolved);
        Event(id + 0x1030u, 24.5f, "Trophy Weapons + Void Stardust", AID.TrophyWeapons);
        Event(id + 0x1040u, 7.1f, "Void Stardust", AID.VoidStardust);
        Event(id + 0x1050u, 10.1f, "Comet / Crushing Comet", AID.Comet, AID.CrushingComet);
        Event(id + 0x1060u, 2.8f, "Assault Evolved: three weapons", AID.AssaultEvolved);
        Event(id + 0x1070u, 23.5f, "Crushing Comet / Comet", AID.Comet, AID.CrushingComet);
        Event(id + 0x1080u, 6f, "Crown of Arcadia", AID.CrownOfArcadia).SetHint(StateMachine.StateHint.Raidwide);
        Event(id + 0x1090u, 12.4f, "Dance of Domination", AID.DanceOfDominationTrophy);
        Event(id + 0x1098u, 6.7f, "Multi-hit raidwide starts", AID.DanceOfDomination1).SetHint(StateMachine.StateHint.Raidwide);
        Event(id + 0x10A0u, 4.2f, "Multi-hit raidwide ends", AID.DanceOfDomination3).SetHint(StateMachine.StateHint.Raidwide);
        Event(id + 0x10B0u, 5f, "Eye of the Hurricane", AID.EyeOfTheHurricane);
        Event(id + 0x10C0u, 5.9f, "Raw Steel Trophy (opposite weapon)", AID.RawSteelTrophyAxe, AID.RawSteelTrophyScythe);
        Event(id + 0x10D0u, 7.4f, "Raw Steel resolves", AID.RawSteel_Impact, AID.RawSteel_HeavyHitter)
            .SetHint(StateMachine.StateHint.Tankbuster);
        Event(id + 0x10E0u, 15.1f, "Charybdistopia: HP to 1", AID.Charybdistopia).SetHint(StateMachine.StateHint.Raidwide);
        Event(id + 0x10F0u, 5.7f, "Ultimate Trophy Weapons", AID.UltimateTrophyWeapons);
        Event(id + 0x1100u, 7.8f, "Assault Apex: six weapons", AID.AssaultApex);
        Event(id + 0x1110u, 34.9f, "Immortal Reign", AID.ImmortalReign);
        Event(id + 0x1120u, 13.1f, "One and Only", AID.OneAndOnly1).SetHint(StateMachine.StateHint.Raidwide);
        OrbitalOmen(id + 0x1200u, 12.2f);
        Event(id + 0x1300u, 17.3f, "Meteorain", AID.Meteorain);
        Event(id + 0x1310u, 8f, "Fireball stack 1", AID.FearsomeFireball1);
        Event(id + 0x1320u, 10.1f, "Fireball stack 2", AID.FearsomeFireball1);
        Event(id + 0x1330u, 10f, "Fireball stack 3", AID.FearsomeFireball1);
        Event(id + 0x1340u, 10f, "Fireball stack 4", AID.FearsomeFireball1);
        Event(id + 0x1350u, 12.7f, "Triple Tyrannhilation", AID.TripleTyrannhilation2);
        Event(id + 0x1358u, 1.1f, "Triple Tyrannhilation damage starts", AID.Shockwave).SetHint(StateMachine.StateHint.Raidwide);
        Event(id + 0x1360u, 3.6f, "Triple Tyrannhilation ends", AID.TripleTyrannhilationEnd);
        Event(id + 0x1370u, 12.2f, "Flatliner", AID.Flatliner);
        Event(id + 0x1380u, 2f, "Arena splits + knockback", AID.Flatliner1).SetHint(StateMachine.StateHint.Knockback);
        Flatliner(id + 0x1390u, 9.1f, 5f); // switch components on Majestic Meteor cast start
        CastEnd(id + 0x20u, 5f, "Majestic Meteor"); // retained legacy entry anchor: 370.9
        Event(id + 0x1400u, 13.1f, "Towers + knockback 1", AID.ExplosionTower).SetHint(StateMachine.StateHint.Knockback);
        Event(id + 0x1410u, 14.9f, "Meteors + Fire Breath 1", AID.MajesticMeteorain, AID.MajesticMeteowrathFirst, AID.FireBreath1);
        Event(id + 0x1420u, 17.2f, "Towers + knockback 2", AID.ExplosionTower).SetHint(StateMachine.StateHint.Knockback);
        Event(id + 0x1430u, 14.9f, "Meteors + Fire Breath 2", AID.MajesticMeteorain, AID.MajesticMeteowrathFirst, AID.FireBreath1);
        Event(id + 0x1440u, 9.3f, "Massive Meteor: five stacks", AID.MassiveMeteor);
        Event(id + 0x1450u, 13.8f, "Arcadion Avalanche: safe platform", AID.ArcadionAvalanche_Pick1, AID.ArcadionAvalanche_Pick2, AID.ArcadionAvalanche_Pick3, AID.ArcadionAvalanche_Pick4);
        Event(id + 0x1460u, 9.6f, "Platform destroyed", AID.ArcadionAvalanche_Smash1, AID.ArcadionAvalanche_Smash2, AID.ArcadionAvalanche_Smash3, AID.ArcadionAvalanche_Smash4);
        Event(id + 0x1470u, 10.7f, "Crown of Arcadia", AID.CrownOfArcadia).SetHint(StateMachine.StateHint.Raidwide);
        OrbitalOmen(id + 0x1500u, 11.1f);
        Event(id + 0x1600u, 9.1f, "Crown of Arcadia", AID.CrownOfArcadia).SetHint(StateMachine.StateHint.Raidwide);
        SplitArena(id + 0x1610u, 7.3f); // switch components on Ecliptic Stampede cast start
        CastEnd(id + 0x30u, 5f, "Ecliptic Stampede"); // retained legacy entry anchor: 529.1
        Event(id + 0x1700u, 7.1f, "Atomic Impact starts", AID.AtomicImpact);
        Event(id + 0x1710u, 16f, "Cosmic Kiss + Weighty Impact towers", AID.CosmicKissTower);
        Event(id + 0x1720u, 9.6f, "Majestic Meteowrath", AID.MajesticMeteowrathSecond);
        Event(id + 0x1730u, 4.3f, "Two-way / Four-way Fireball", AID.TwoWayFireballEnd, AID.FourWayFireballEnd);
        Event(id + 0x1740u, 8.3f, "Crown of Arcadia", AID.CrownOfArcadia).SetHint(StateMachine.StateHint.Raidwide);
        Event(id + 0x1800u, 13.9f, "Heartbreak Kick: five soaks", AID.HeartbreakKickTower);
        Event(id + 0x1810u, 19.1f, "Heartbreak Kick: six soaks", AID.HeartbreakKickTower);
        Event(id + 0x1820u, 21.1f, "Heartbreak Kick: seven soaks", AID.HeartbreakKickTower);
        Cast(id + 0x1900u, AID.EnrageCast, 18.1f, 8f, "Heartbreaker enrage");
    }

    private State Event(uint id, float delay, string name, params AID[] actions)
        => ComponentCondition<TimelineEvents>(id, delay, c => c.Matched, name, 30f)
            .ExecOnEnter<TimelineEvents>(c => c.Watch(actions, delay));

    private void OrbitalOmen(uint id, float delay)
    {
        Event(id, delay, "Great Wall of Fire", AID.GreatWallOfFire).SetHint(StateMachine.StateHint.Tankbuster);
        Event(id + 0x10u, 13.5f, "Orbital Omen", AID.OrbitalOmen);
        Event(id + 0x20u, 8.7f, "Fire and Fury", AID.FireAndFury);
    }
    private void CrownOfArcadia(uint id, float delay, float cast, int seq)
    {
        Cast(id, AID.CrownOfArcadia, delay, cast, "Crown of Arcadia")
            .SetHint(StateMachine.StateHint.Raidwide)
            .ActivateOnEnter<CrownOfArcadia>()
            .ActivateOnExit<RawSteelTrophyAxe>()
            .ActivateOnExit<RawSteelTrophyScythe>()
            .ActivateOnExit<AssaultWeaponTimeline>()
            .ActivateOnExit<AssaultFloorPredictor>()
            .ActivateOnExit<AssaultWeaponSafeSpots>()
            .ActivateOnExit<AssaultEvolvedSword>()
            .ActivateOnExit<AssaultEvolvedAxeStack>()
            .ActivateOnExit<AssaultEvolvedScythe>()
            .ActivateOnExit<Cometite>()
            .ActivateOnExit<Comet>()
            .ActivateOnExit<CrushingComet>()
            .ActivateOnExit<EyeOfTheHurricane>()
            .ActivateOnExit<Explosion>()
            .ActivateOnExit<MaelstromVoidZones>()
            .ActivateOnExit<MaelstromGustCones>()
            .ActivateOnExit<MaelstromBaitSafeSpots>()
            .ActivateOnExit<GreatWallOfFire>()
            .ActivateOnExit<GreatWallOfFireExplosion>()
            .ActivateOnExit<OrbitalOmen>()
            .ActivateOnExit<FireAndFury>()
            .ActivateOnExit<MeteorainComets>()
            .ActivateOnExit<FearsomeFireball>()
            .ActivateOnExit<CosmicKiss>()
            .ActivateOnExit<CometTethers>()
            .ActivateOnExit<TripleTyrannhilation>()
            .ActivateOnExit<Flatliner>()
            .ActivateOnExit<FlatlinerKB>();
    }
    private void Flatliner(uint id, float delay, float cast)
    {
        CastStart(id, AID.MajesticMeteor, delay, "Majestic Meteor")
            .DeactivateOnExit<RawSteelTrophyAxe>()
            .DeactivateOnExit<RawSteelTrophyScythe>()
            .DeactivateOnExit<AssaultWeaponTimeline>()
            .DeactivateOnExit<AssaultFloorPredictor>()
            .DeactivateOnExit<AssaultWeaponSafeSpots>()
            .DeactivateOnExit<AssaultEvolvedSword>()
            .DeactivateOnExit<AssaultEvolvedAxeStack>()
            .DeactivateOnExit<AssaultEvolvedScythe>()
            .DeactivateOnExit<Cometite>()
            .DeactivateOnExit<Comet>()
            .DeactivateOnExit<CrushingComet>()
            .DeactivateOnExit<EyeOfTheHurricane>()
            .DeactivateOnExit<Explosion>()
            .DeactivateOnExit<MaelstromVoidZones>()
            .DeactivateOnExit<MaelstromGustCones>()
            .DeactivateOnExit<MaelstromBaitSafeSpots>()
            .DeactivateOnExit<FearsomeFireball>()
            .DeactivateOnExit<CosmicKiss>()
            .DeactivateOnExit<CometTethers>()
            .DeactivateOnExit<TripleTyrannhilation>()
            .DeactivateOnExit<Flatliner>()
            .DeactivateOnExit<FlatlinerKB>()
            .DeactivateOnExit<GreatWallOfFire>().ActivateOnExit<GreatWallOfFire>()
            .DeactivateOnExit<GreatWallOfFireExplosion>().ActivateOnExit<GreatWallOfFireExplosion>()
            .DeactivateOnExit<OrbitalOmen>().ActivateOnExit<OrbitalOmen>()
            .DeactivateOnExit<FireAndFury>().ActivateOnExit<FireAndFury>()
            .ActivateOnExit<ExplosionTowers>()
            .ActivateOnExit<ExplosionTowerKnockback>()
            .ActivateOnExit<MajesticMeteor>()
            .ActivateOnExit<Tether1>()
            .ActivateOnExit<Tether2>()
            .ActivateOnExit<FireBreath>()
            .ActivateOnExit<MeteorainPortals>()
            .ActivateOnExit<MeteorMechanicHints>()
            .ActivateOnExit<ExplosionTowerHints>()
            .ActivateOnExit<MassiveMeteor>()
            .ActivateOnExit<ArcadionAvalanche>()
            .ActivateOnExit<ArcadionAvalancheSmash>()
            .ActivateOnExit<MajesticMeteorStorm>()
            .ActivateOnExit<MammothMeteor>()
            .ActivateOnExit<AtomicImpact>()
            .ActivateOnExit<AtomicImpactVoidZones>()
            .ActivateOnExit<CosmicKissTowers>()
            .ActivateOnExit<WeightyImpactTowers>()
            .ActivateOnExit<TwoWayFireball>()
            .ActivateOnExit<FourWayFireball>()
            .ActivateOnExit<HeartBreakerTower>();
    }
    private void SplitArena(uint id, float delay)
    {
        CastStart(id, AID.EclipticStampede, delay, "Ecliptic Stampede")
            .DeactivateOnExit<FireBreath>()
            .DeactivateOnExit<MeteorainPortals>()
            .DeactivateOnExit<MeteorMechanicHints>()
            .DeactivateOnExit<ArcadionAvalanche>()
            .DeactivateOnExit<ArcadionAvalancheSmash>()
            .DeactivateOnExit<ExplosionTowers>()
            .DeactivateOnExit<ExplosionTowerKnockback>()
            .DeactivateOnExit<ExplosionTowerHints>()
            .DeactivateOnExit<CrownOfArcadia>().ActivateOnExit<CrownOfArcadia>()
            .DeactivateOnExit<GreatWallOfFire>().ActivateOnExit<GreatWallOfFire>()
            .DeactivateOnExit<GreatWallOfFireExplosion>().ActivateOnExit<GreatWallOfFireExplosion>()
            .DeactivateOnExit<OrbitalOmen>().ActivateOnExit<OrbitalOmen>()
            .DeactivateOnExit<FireAndFury>().ActivateOnExit<FireAndFury>()
            .DeactivateOnExit<Tether1>().ActivateOnExit<Tether1>()
            .DeactivateOnExit<Tether2>().ActivateOnExit<Tether2>()
            .DeactivateOnExit<MassiveMeteor>().ActivateOnExit<MassiveMeteor>()
            .DeactivateOnExit<MammothMeteor>().ActivateOnExit<MammothMeteor>()
            .DeactivateOnExit<AtomicImpact>().ActivateOnExit<AtomicImpact>()
            .ActivateOnExit<AtomicImpactBaitPath>()
            .DeactivateOnExit<AtomicImpactVoidZones>().ActivateOnExit<AtomicImpactVoidZones>()
            .DeactivateOnExit<CosmicKissTowers>().ActivateOnExit<CosmicKissTowers>()
            .DeactivateOnExit<WeightyImpactTowers>().ActivateOnExit<WeightyImpactTowers>()
            .DeactivateOnExit<TwoWayFireball>().ActivateOnExit<TwoWayFireball>()
            .DeactivateOnExit<FourWayFireball>().ActivateOnExit<FourWayFireball>()
            .DeactivateOnExit<HeartBreakerTower>().ActivateOnExit<HeartBreakerTower>();
    }
}
