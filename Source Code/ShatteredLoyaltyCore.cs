using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;
using UnityEngine;

namespace ShatteredLoyalty
{
    // ==========================================
    // 1. CLASSE DE CONFIGURAÇÕES DO MOD
    // ==========================================
    public class ShatteredLoyaltySettings : ModSettings
    {
        public bool losePassions = true;
        public bool gainTrait = true;
        public bool applyCatatonia = true;
        public float resistanceMultiplier = 1.0f;

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref losePassions, "losePassions", true);
            Scribe_Values.Look(ref gainTrait, "gainTrait", true);
            Scribe_Values.Look(ref applyCatatonia, "applyCatatonia", true);
            Scribe_Values.Look(ref resistanceMultiplier, "resistanceMultiplier", 1.0f);
        }
    }

    // ==========================================
    // 2. CLASSE DA TELA DE OPÇÕES NO MENU
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
    // 4. EXTENSÕES PARA SUPORTE 1.4, 1.5 E 1.6
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
    // 5. WORKGIVER NATIVO COM COOLDOWN RIGOROSO (8 HORAS)
    // ==========================================
    public class WorkGiver_Warden_BreakLoyalty : WorkGiver_Warden
    {
        // Dicionário público para poder ser atualizado pelo JobDriver
        public static Dictionary<Pawn, int> lastAttemptTicks = new Dictionary<Pawn, int>();

        public override bool HasJobOnThing(Pawn pawn, Thing t, bool forced = false)
        {
            Pawn prisoner = t as Pawn;
            if (prisoner == null || !prisoner.IsPrisonerOfColony || prisoner.guest == null) return false;

            PrisonerInteractionModeDef currentMode = prisoner.guest.GetInteractionMode();
            if (currentMode == null || currentMode.defName != "QuebrarLealdade") return false;

            if (!prisoner.Awake() || prisoner.InAggroMentalState) return false;

            // Cooldown de 8 horas in-game (20000 ticks) aplicado a todos (mesmo com force)
            if (lastAttemptTicks.TryGetValue(prisoner, out int lastTick))
            {
                if (Find.TickManager.TicksGame - lastTick < 20000)
                {
                    return false;
                }
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

    // ==========================================
    // 6. O TRABALHO DO CARCEREIRO (JOB DRIVER)
    // ==========================================
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
            applyEffect.initAction = () => {

                // REGISTA O COOLDOWN EXATO NO MOMENTO EM QUE A AÇÃO ACONTECE
                WorkGiver_Warden_BreakLoyalty.lastAttemptTicks[Prisoner] = Find.TickManager.TicksGame;

                pawn.interactions.TryInteractWith(Prisoner, InteractionDefOf.Chitchat);

                if (!Prisoner.guest.IsUnwaveringlyLoyal()) return;

                if (Prisoner.guest.resistance <= 0f)
                {
                    Prisoner.guest.resistance = 10.0f;
                }

                float negotiationAbility = pawn.GetStatValue(StatDefOf.NegotiationAbility);
                float reduction = negotiationAbility * ShatteredLoyaltyMod.settings.resistanceMultiplier;

                Prisoner.guest.resistance = Mathf.Max(0f, Prisoner.guest.resistance - reduction);

                Messages.Message($"[Shattered Loyalty] {pawn.NameShortColored} reduziu a resistência de {Prisoner.NameShortColored} para {Prisoner.guest.resistance:F1}", Prisoner, MessageTypeDefOf.NeutralEvent, true);

                if (Prisoner.guest.resistance <= 0f)
                {
                    Log.Warning($"[Shattered Loyalty] Resistência de {Prisoner.NameShortColored} zerou! Aplicando consequências...");

                    Prisoner.guest.SetUnwaveringlyLoyal(false);

                    if (ShatteredLoyaltyMod.settings.gainTrait)
                    {
                        TraitDef menteFraturada = DefDatabase<TraitDef>.GetNamedSilentFail("MenteFraturada");
                        if (menteFraturada != null)
                        {
                            if (!Prisoner.story.traits.HasTrait(menteFraturada))
                            {
                                Trait novoTraco = new Trait(menteFraturada, 0);
                                Prisoner.story.traits.GainTrait(novoTraco);
                                Log.Message("[Shattered Loyalty] Traço Mente Fraturada aplicado com sucesso!");
                            }
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
                        if (catatonia != null)
                        {
                            Prisoner.health.AddHediff(catatonia);
                        }
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
}