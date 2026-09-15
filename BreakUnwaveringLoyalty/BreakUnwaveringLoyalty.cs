using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace BreakUnwaveringLoyalty
{
    [StaticConstructorOnStartup]
    public static class ModMain
    {
        static ModMain()
        {
            var harmony = new Harmony("grok.breakunwaveringloyalty");
            harmony.PatchAll(Assembly.GetExecutingAssembly());
            Log.Message("[Break Unwavering Loyalty] Carregado com sucesso.");
        }
    }

    [DefOf]
    public static class BUL_DefOf
    {
        public static PrisonerInteractionModeDef BreakUnwaveringLoyalty;

        static BUL_DefOf()
        {
            DefOfHelper.EnsureInitializedInCtor(typeof(BUL_DefOf));
        }
    }

    // =====================================================
    // 1. Faz o WorkGiver_Warden_Chat reconhecer o nosso modo
    // =====================================================
    [HarmonyPatch(typeof(WorkGiver_Warden_Chat), "JobOnThing")]
    public static class Patch_WorkGiver_Warden_Chat_JobOnThing
    {
        public static void Postfix(Pawn pawn, Thing t, bool forced, ref Job __result)
        {
            // Se já tem job, deixa quieto
            if (__result != null) return;

            Pawn prisoner = t as Pawn;
            if (prisoner == null || !prisoner.IsPrisonerOfColony) return;
            if (prisoner.guest == null) return;

            // Só age se o modo for o nosso E o cara ainda for unwaveringly loyal
            if (prisoner.guest.IsInteractionEnabled(BUL_DefOf.BreakUnwaveringLoyalty) && !prisoner.guest.Recruitable)
            {
                // Força o mesmo job que o Reduce Resistance / Recruit usa
                __result = JobMaker.MakeJob(JobDefOf.PrisonerAttemptRecruit, prisoner);
            }
        }
    }

    // =====================================================
    // 2. Quando a conversa acontece, quebra a lealdade
    // =====================================================
    [HarmonyPatch(typeof(InteractionWorker_RecruitAttempt), "Interacted")]
    public static class Patch_InteractionWorker_RecruitAttempt_Interacted
    {
        // Guardamos se era o nosso modo antes da interação
        public static void Prefix(Pawn initiator, Pawn recipient, out bool __state)
        {
            __state = false;

            if (recipient?.guest == null) return;
            if (!recipient.guest.IsInteractionEnabled(BUL_DefOf.BreakUnwaveringLoyalty)) return;
            if (recipient.guest.Recruitable) return; // já era recrutável

            __state = true;
        }

        public static void Postfix(Pawn initiator, Pawn recipient, bool __state)
        {
            if (!__state) return;
            if (recipient?.guest == null) return;

            // Chance de quebrar a lealdade
            float chance = 0.18f; // base 18%

            // Bônus do Social do warden
            if (initiator?.skills != null)
            {
                chance += initiator.skills.GetSkill(SkillDefOf.Social).Level * 0.012f;
            }

            // Bônus/malus do humor do prisioneiro
            if (recipient.needs?.mood != null)
            {
                chance += (recipient.needs.mood.CurLevelPercentage - 0.5f) * 0.25f;
            }

            chance = Mathf.Clamp(chance, 0.08f, 0.65f);

            if (Rand.Chance(chance))
            {
                // Quebrou!
                recipient.guest.Recruitable = true;

                // Dá uma resistência inicial para não ser recrutado de graça
                if (recipient.guest.resistance < 1f)
                {
                    recipient.guest.resistance = Rand.Range(10f, 20f);
                }

                // Feedback
                Messages.Message(
                    "MessageLoyaltyBroken".Translate(recipient.LabelShort),
                    recipient,
                    MessageTypeDefOf.PositiveEvent
                );

                Find.LetterStack.ReceiveLetter(
                    "LetterLabelLoyaltyBroken".Translate(),
                    "LetterTextLoyaltyBroken".Translate(recipient.LabelShort),
                    LetterDefOf.PositiveEvent,
                    recipient
                );
            }
        }
    }
}
