using HarmonyLib;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.Sound;

namespace ShatteredLoyalty
{
    // ==========================================
    // 1. CLASSE DE CONFIGURAÇÕES DO MOD
    // ==========================================
    public class ShatteredLoyaltySettings : ModSettings
    {
        public bool losePassions = true;
        public bool gainTrait = true;           // Traço da conversa (MenteFraturada)
        public bool gainTortureTrait = true;    // Traço da tortura (TraumaDaTortura)
        public bool applyCatatonia = true;
        public float resistanceMultiplier = 1.0f;

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref losePassions, "losePassions", true);
            Scribe_Values.Look(ref gainTrait, "gainTrait", true);
            Scribe_Values.Look(ref gainTortureTrait, "gainTortureTrait", true);
            Scribe_Values.Look(ref applyCatatonia, "applyCatatonia", true);
            Scribe_Values.Look(ref resistanceMultiplier, "resistanceMultiplier", 1.0f);
        }
    }

    // ==========================================
    // 2. TELA DE OPÇÕES NO MENU DO JOGO
    // ==========================================
    public class ShatteredLoyaltyMod : Mod
    {
        public static ShatteredLoyaltySettings settings;

        public ShatteredLoyaltyMod(ModContentPack content) : base(content)
        {
            settings = GetSettings<ShatteredLoyaltySettings>();
        }

        public override void DoSettingsWindowContents(Rect inRect)
        {
            Listing_Standard listingStandard = new Listing_Standard();
            listingStandard.Begin(inRect);

            listingStandard.CheckboxLabeled("ShatteredLoyalty_LosePassions".Translate(), ref settings.losePassions, "ShatteredLoyalty_LosePassionsDesc".Translate());
            listingStandard.CheckboxLabeled("ShatteredLoyalty_GainTrait".Translate(), ref settings.gainTrait, "ShatteredLoyalty_GainTraitDesc".Translate());

            // Nova opção para o Traço de Tortura
            listingStandard.CheckboxLabeled("ShatteredLoyalty_GainTortureTrait".Translate(), ref settings.gainTortureTrait, "ShatteredLoyalty_GainTortureTraitDesc".Translate());

            listingStandard.CheckboxLabeled("ShatteredLoyalty_ApplyCatatonia".Translate(), ref settings.applyCatatonia, "ShatteredLoyalty_ApplyCatatoniaDesc".Translate());

            listingStandard.Gap();

            listingStandard.Label("ShatteredLoyalty_BreakSpeed".Translate(settings.resistanceMultiplier.ToString("F1")));
            settings.resistanceMultiplier = listingStandard.Slider(settings.resistanceMultiplier, 0.1f, 5.0f);
            listingStandard.Label("ShatteredLoyalty_BreakSpeedDesc".Translate());

            listingStandard.End();
            base.DoSettingsWindowContents(inRect);
        }

        public override string SettingsCategory()
        {
            return "Shattered Loyalty";
        }
    }

    // ==========================================
    // 3. INICIALIZAÇÃO DO HARMONY
    // ==========================================
    [StaticConstructorOnStartup]
    public static class ModInit
    {
        static ModInit()
        {
            var harmony = new Harmony("SeuNome.ShatteredLoyalty");
            harmony.PatchAll(Assembly.GetExecutingAssembly());
            Log.Message("[Shattered Loyalty] Mod carregado com sucesso!");
        }
    }

    // ==========================================
    // 4. EXTENSÕES DE COMPATIBILIDADE (1.4, 1.5 E 1.6)
    // ==========================================
    public static class GuestTrackerExtensions
    {
        public static PrisonerInteractionModeDef GetInteractionMode(this Pawn_GuestTracker guest)
        {
            if (guest == null) return null;
            var t = Traverse.Create(guest);
            if (t.Field("interactionMode").FieldExists()) return t.Field("interactionMode").GetValue<PrisonerInteractionModeDef>();
            if (t.Property("InteractionMode").PropertyExists()) return t.Property("InteractionMode").GetValue<PrisonerInteractionModeDef>();
            return null;
        }

        public static bool IsUnwaveringlyLoyal(this Pawn_GuestTracker guest)
        {
            if (guest == null) return false;
            var t = Traverse.Create(guest);

            if (t.Field("recruitable").FieldExists()) return !t.Field("recruitable").GetValue<bool>();
            if (t.Property("Recruitable").PropertyExists()) return !t.Property("Recruitable").GetValue<bool>();

            if (t.Field("unwaveringlyLoyal").FieldExists()) return t.Field("unwaveringlyLoyal").GetValue<bool>();
            if (t.Property("UnwaveringlyLoyal").PropertyExists()) return !t.Property("UnwaveringlyLoyal").GetValue<bool>();

            return false;
        }

        public static void SetUnwaveringlyLoyal(this Pawn_GuestTracker guest, bool value)
        {
            if (guest == null) return;
            var t = Traverse.Create(guest);

            if (t.Field("recruitable").FieldExists()) { t.Field("recruitable").SetValue(!value); return; }
            if (t.Property("Recruitable").PropertyExists()) { t.Property("Recruitable").SetValue(!value); return; }

            if (t.Field("unwaveringlyLoyal").FieldExists()) t.Field("unwaveringlyLoyal").SetValue(value);
            else if (t.Property("UnwaveringlyLoyal").PropertyExists()) t.Property("UnwaveringlyLoyal").SetValue(value);
        }
    }

    // ==========================================
    // 5. MÉTODO 1: CONVERSA / PSICOLÓGICO
    // ==========================================
    public class WorkGiver_Warden_BreakLoyalty : WorkGiver_Warden
    {
        public static Dictionary<Pawn, int> lastAttemptTicks = new Dictionary<Pawn, int>();

        public override bool HasJobOnThing(Pawn pawn, Thing t, bool forced = false)
        {
            Pawn prisoner = t as Pawn;
            if (prisoner == null || !prisoner.IsPrisonerOfColony || prisoner.guest == null) return false;

            PrisonerInteractionModeDef currentMode = prisoner.guest.GetInteractionMode();
            if (currentMode == null || currentMode.defName != "QuebrarLealdade") return false;

            if (!prisoner.Awake() || prisoner.InAggroMentalState) return false;

            if (lastAttemptTicks.TryGetValue(prisoner, out int lastTick))
            {
                if (Find.TickManager.TicksGame - lastTick < 20000) return false;
            }

            return prisoner.guest.PrisonerIsSecure && prisoner.Spawned && pawn.CanReserveAndReach(prisoner, PathEndMode.Touch, Danger.Deadly);
        }

        public override Job JobOnThing(Pawn pawn, Thing t, bool forced = false)
        {
            if (!HasJobOnThing(pawn, t, forced)) return null;

            Pawn prisoner = t as Pawn;
            if (prisoner == null) return null;

            JobDef customJob = DefDatabase<JobDef>.GetNamedSilentFail("Job_QuebrarLealdade");
            if (customJob != null)
            {
                return JobMaker.MakeJob(customJob, prisoner);
            }

            return null;
        }
    }

    public class JobDriver_QuebrarLealdade : JobDriver
    {
        protected Pawn Prisoner => (Pawn)job.targetA.Thing;

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            return pawn.Reserve(Prisoner, job, 1, -1, null, errorOnFailed);
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedOrNull(TargetIndex.A);
            this.FailOnNotAwake(TargetIndex.A);
            this.FailOn(() => !Prisoner.IsPrisonerOfColony || !Prisoner.guest.PrisonerIsSecure);

            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch);

            Toil waitToil = Toils_General.Wait(250);
            waitToil.socialMode = RandomSocialMode.Normal;
            yield return waitToil;

            Toil applyEffect = new Toil();
            applyEffect.initAction = () =>
            {
                WorkGiver_Warden_BreakLoyalty.lastAttemptTicks[Prisoner] = Find.TickManager.TicksGame;

                pawn.interactions.TryInteractWith(Prisoner, InteractionDefOf.Chitchat);

                if (!Prisoner.guest.IsUnwaveringlyLoyal()) return;

                if (Prisoner.guest.resistance <= 0f) Prisoner.guest.resistance = 10.0f;

                float negotiationAbility = pawn.GetStatValue(StatDefOf.NegotiationAbility);
                float reduction = negotiationAbility * ShatteredLoyaltyMod.settings.resistanceMultiplier;

                Prisoner.guest.resistance = Mathf.Max(0f, Prisoner.guest.resistance - reduction);

                // Substitua a linha antiga de Messages.Message por esta:
                Messages.Message("ShatteredLoyalty_MessageBreak".Translate(pawn.NameShortColored, Prisoner.NameShortColored, Prisoner.guest.resistance.ToString("F1")), Prisoner, MessageTypeDefOf.NeutralEvent, true);

                if (Prisoner.guest.resistance <= 0f)
                {
                    Prisoner.guest.SetUnwaveringlyLoyal(false);

                    if (ShatteredLoyaltyMod.settings.gainTrait)
                    {
                        TraitDef menteFraturada = DefDatabase<TraitDef>.GetNamedSilentFail("MenteFraturada");
                        if (menteFraturada != null && !Prisoner.story.traits.HasTrait(menteFraturada))
                        {
                            Prisoner.story.traits.GainTrait(new Trait(menteFraturada, 0));
                        }
                    }

                    if (ShatteredLoyaltyMod.settings.losePassions)
                    {
                        foreach (SkillRecord skill in Prisoner.skills.skills)
                        {
                            if (skill.passion != Passion.None && Rand.Value < 0.5f)
                            {
                                skill.passion = Passion.None;
                            }
                        }
                    }

                    if (ShatteredLoyaltyMod.settings.applyCatatonia)
                    {
                        HediffDef catatonia = DefDatabase<HediffDef>.GetNamedSilentFail("CatatonicBreakdown");
                        if (catatonia != null) Prisoner.health.AddHediff(catatonia);
                    }

                    Find.LetterStack.ReceiveLetter(
                        "ShatteredLoyalty_LetterTitle".Translate(),
                        "ShatteredLoyalty_LetterDesc".Translate(Prisoner.NameShortColored),
                        LetterDefOf.NegativeEvent,
                        Prisoner
                    );
                }
            };

            yield return applyEffect;
        }
    }

    // ==========================================
    // 6. MÉTODO 2: TORTURA FÍSICA
    // ==========================================
    public class WorkGiver_Warden_Torture : WorkGiver_Warden
    {
        public static Dictionary<Pawn, int> lastTortureTicks = new Dictionary<Pawn, int>();

        public override bool HasJobOnThing(Pawn pawn, Thing t, bool forced = false)
        {
            Pawn prisoner = t as Pawn;
            if (prisoner == null || !prisoner.IsPrisonerOfColony || prisoner.guest == null) return false;

            PrisonerInteractionModeDef currentMode = prisoner.guest.GetInteractionMode();
            if (currentMode == null || currentMode.defName != "TorturarPrisioneiro") return false;

            if (!prisoner.Awake() || prisoner.InAggroMentalState) return false;

            if (lastTortureTicks.TryGetValue(prisoner, out int lastTick))
            {
                if (Find.TickManager.TicksGame - lastTick < 20000) return false;
            }

            return prisoner.guest.PrisonerIsSecure && prisoner.Spawned && pawn.CanReserveAndReach(prisoner, PathEndMode.Touch, Danger.Deadly);
        }

        public override Job JobOnThing(Pawn pawn, Thing t, bool forced = false)
        {
            if (!HasJobOnThing(pawn, t, forced)) return null;

            Pawn prisoner = t as Pawn;
            if (prisoner == null) return null;

            JobDef customJob = DefDatabase<JobDef>.GetNamedSilentFail("Job_TorturarPrisioneiro");
            if (customJob != null)
            {
                return JobMaker.MakeJob(customJob, prisoner);
            }

            return null;
        }
    }

    // ==========================================
    // JOB DRIVER: EXECUÇÃO DA TORTURA COM EFEITOS E ANIMAÇÕES
    // ==========================================
    public class JobDriver_TorturarPrisioneiro : JobDriver
    {
        protected Pawn Prisoner => (Pawn)job.targetA.Thing;

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            return pawn.Reserve(Prisoner, job, 1, -1, null, errorOnFailed);
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedOrNull(TargetIndex.A);
            this.FailOnNotAwake(TargetIndex.A);
            this.FailOn(() => !Prisoner.IsPrisonerOfColony || !Prisoner.guest.PrisonerIsSecure);

            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch);

            // TOIL DE TORTURA CONTINUADA (5 Segundos = 300 Ticks)
            Toil tortureToil = new Toil();
            tortureToil.defaultCompleteMode = ToilCompleteMode.Delay;
            tortureToil.defaultDuration = 300;
            tortureToil.WithProgressBarToilDelay(TargetIndex.A);

            tortureToil.initAction = () =>
            {
                // Vira o carcereiro de frente para o prisioneiro
                pawn.rotationTracker.FaceTarget(Prisoner);
            };

            tortureToil.tickAction = () =>
            {
                pawn.rotationTracker.FaceTarget(Prisoner);

                // A cada 60 ticks (~1 segundo in-game), desfere um golpe
                if (Find.TickManager.TicksGame % 60 == 0)
                {
                    // 1. Som de impacto corporal
                    SoundDefOf.Pawn_Melee_Punch_HitPawn.PlayOneShot(new TargetInfo(Prisoner.Position, Prisoner.Map));

                    // 2. Gera mancha de sangue real no chão na posição do prisioneiro
                    FilthMaker.TryMakeFilth(Prisoner.Position, Prisoner.Map, ThingDefOf.Filth_Blood);

                    // 3. Aplica dano contínuo em partes aleatórias do corpo (2 a 4 de dano contuso por golpe)
                    DamageInfo dinfo = new DamageInfo(DamageDefOf.Blunt, Rand.RangeInclusive(2, 4), 0f, -1f, pawn);
                    Prisoner.TakeDamage(dinfo);
                }
            };

            yield return tortureToil;

            // TOIL DE FINALIZAÇÃO (Processa o resultado final da sessão)
            Toil executeTorture = new Toil();
            executeTorture.initAction = () =>
            {
                WorkGiver_Warden_Torture.lastTortureTicks[Prisoner] = Find.TickManager.TicksGame;

                if (!Prisoner.guest.IsUnwaveringlyLoyal()) return;

                if (Prisoner.guest.resistance <= 0f) Prisoner.guest.resistance = 10.0f;

                // Redução de Resistência
                float baseReduction = pawn.GetStatValue(StatDefOf.NegotiationAbility) * 2.0f;
                float reduction = baseReduction * ShatteredLoyaltyMod.settings.resistanceMultiplier;

                Prisoner.guest.resistance = Mathf.Max(0f, Prisoner.guest.resistance - reduction);

                // Substitua a linha antiga de Messages.Message por esta:
                Messages.Message("ShatteredLoyalty_MessageTorture".Translate(pawn.NameShortColored, Prisoner.NameShortColored, Prisoner.guest.resistance.ToString("F1")), Prisoner, MessageTypeDefOf.NegativeEvent, true);

                // Processa o colapso do prisioneiro ao zerar a resistência
                if (Prisoner.guest.resistance <= 0f)
                {
                    Prisoner.guest.SetUnwaveringlyLoyal(false);

                    // Traço Mente Mutilada / Trauma da Tortura (Lê a nova opção do menu)
                    if (ShatteredLoyaltyMod.settings.gainTortureTrait)
                    {
                        TraitDef traumaTrait = DefDatabase<TraitDef>.GetNamedSilentFail("TraumaDaTortura");
                        if (traumaTrait != null && !Prisoner.story.traits.HasTrait(traumaTrait))
                        {
                            Prisoner.story.traits.GainTrait(new Trait(traumaTrait, 0));
                        }
                    }

                    // Perda de Paixões e Danos nas Habilidades
                    if (ShatteredLoyaltyMod.settings.losePassions)
                    {
                        foreach (SkillRecord skill in Prisoner.skills.skills)
                        {
                            if (skill.passion != Passion.None && Rand.Value < 0.8f)
                            {
                                skill.passion = Passion.None;
                            }

                            if (skill.Level > 0)
                            {
                                skill.Level = Mathf.Max(0, skill.Level - Rand.RangeInclusive(1, 3));
                            }
                        }
                    }

                    // Catatonia Garantida
                    if (ShatteredLoyaltyMod.settings.applyCatatonia)
                    {
                        HediffDef catatonia = DefDatabase<HediffDef>.GetNamedSilentFail("CatatonicBreakdown");
                        if (catatonia != null) Prisoner.health.AddHediff(catatonia);
                    }

                    Find.LetterStack.ReceiveLetter(
                        "ShatteredLoyalty_LetterTitle".Translate(),
                        "ShatteredLoyalty_LetterDesc".Translate(Prisoner.NameShortColored),
                        LetterDefOf.NegativeEvent,
                        Prisoner
                    );
                }
            };

            yield return executeTorture;
        }
    }
}