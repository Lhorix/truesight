using HarmonyLib;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Verse;

namespace TrueSight
{
	[DefOf]
	public static class TS_DefOf
	{
		public static HediffDef TS_TrueSight;
	}
	[StaticConstructorOnStartup]
	public static class HarmonyPatches
	{
		static HarmonyPatches()
		{
			new Harmony("TrueSight.Mod").PatchAll();
		}

		public static bool ShouldHaveTrueSightHediff(this Pawn pawn)
		{
			return !pawn.Dead && pawn.Ideo != null && pawn.Ideo.IdeoApprovesOfBlindness()
				&& pawn.health?.hediffSet != null && pawn.health.hediffSet.GetFirstHediffOfDef(TS_DefOf.TS_TrueSight) is null
				&& !pawn.health.capacities.CapableOf(PawnCapacityDefOf.Sight);
		}
	}

	[HarmonyPatch]
	public static class StatWorker_Patches
	{
		public static IEnumerable<MethodBase> TargetMethods()
		{
			yield return AccessTools.Method(typeof(StatWorker), nameof(StatWorker.GetValueUnfinalized));
			yield return AccessTools.Method(typeof(StatWorker), nameof(StatWorker.GetExplanationUnfinalized));
		}
		public static void Prefix(StatWorker __instance, StatRequest req, out (PawnCapacityFactor originalFactor, float originalWeight) __state)
		{
			__state = default;
			if (__instance.stat == StatDefOf.ReadingSpeed && req.Thing is Pawn pawn)
			{
				Hediff_TrueSight trueSightHediff = pawn.health.hediffSet.GetFirstHediffOfDef(TS_DefOf.TS_TrueSight) as Hediff_TrueSight;
				if (trueSightHediff != null)
				{
					float blindsightLevel = trueSightHediff.Severity * 10f;
					float sightCapacityWeight = 1f;

					if (blindsightLevel >= 4f) sightCapacityWeight = 0f;
					else if (blindsightLevel >= 3f) sightCapacityWeight = 0.25f;
					else if (blindsightLevel >= 2f) sightCapacityWeight = 0.5f;
					else if (blindsightLevel >= 1f) sightCapacityWeight = 0.75f;

					for (int i = 0; i < __instance.stat.capacityFactors.Count; i++)
					{
						if (__instance.stat.capacityFactors[i].capacity == PawnCapacityDefOf.Sight)
						{
							float originalWeight = __instance.stat.capacityFactors[i].weight;
							PawnCapacityFactor originalCapacityFactor = __instance.stat.capacityFactors[i];
							__state = (originalCapacityFactor, originalWeight);
							__instance.stat.capacityFactors[i].weight = sightCapacityWeight;
							break;
						}
					}
				}
			}
		}
		public static void Postfix((PawnCapacityFactor originalFactor, float originalWeight) __state)
		{
			if (__state != default)
			{
				__state.originalFactor.weight = __state.originalWeight;
			}
		}
	}

	[HarmonyPatch(typeof(JobDriver_Blind), "Blind")]
	public static class JobDriver_Blind_Patch
	{
		public static void Postfix(Pawn pawn, Pawn doer)
		{
			if (pawn.ShouldHaveTrueSightHediff())
			{
				var hediff = HediffMaker.MakeHediff(TS_DefOf.TS_TrueSight, pawn);
				pawn.health.AddHediff(hediff);
			}
		}
	}

	[HarmonyPatch(typeof(Pawn), "SpawnSetup")]
	public static class Pawn_SpawnSetup_Patch
	{
		public static void Postfix(Pawn __instance)
		{
			if (__instance.ShouldHaveTrueSightHediff())
			{
				var hediff = HediffMaker.MakeHediff(TS_DefOf.TS_TrueSight, __instance);
				__instance.health.AddHediff(hediff);
			}
		}
	}

	[HarmonyPatch(typeof(Pawn_HealthTracker), "CheckForStateChange")]
	public static class Pawn_CheckForStateChange_Patch
	{
		public static void Postfix(Pawn ___pawn)
		{
			if (___pawn.ShouldHaveTrueSightHediff())
			{
				var hediff = HediffMaker.MakeHediff(TS_DefOf.TS_TrueSight, ___pawn);
				___pawn.health.AddHediff(hediff);
			}
		}
	}

	[HarmonyPatch(typeof(Hediff_Psylink), "ChangeLevel", new Type[] { typeof(int), typeof(bool) })]
	public static class Hediff_Psylink_ChangeLevel_Patch
	{
		public static void Postfix(Hediff_Psylink __instance)
		{
			if (__instance.pawn.ShouldHaveTrueSightHediff())
			{
				var hediff = HediffMaker.MakeHediff(TS_DefOf.TS_TrueSight, __instance.pawn);
				__instance.pawn.health.AddHediff(hediff);
			}
		}
	}

	// Handle ideological reform
	[HarmonyPatch(typeof(Ideo), "RecachePrecepts")]
	public static class Ideo_RecachePrecepts_Patch
	{
		public static void Postfix(Ideo __instance)
		{
			if (!__instance.IdeoApprovesOfBlindness()) return; // Don't bother checking unless blindness is approved of

			foreach (Pawn p in PawnsFinder.AllMapsCaravansAndTravelingTransportPods_Alive)
			{
				if (p.ShouldHaveTrueSightHediff())
				{
					var hediff = HediffMaker.MakeHediff(TS_DefOf.TS_TrueSight, p);
					p.health.AddHediff(hediff);
				}
			}
		}
	}

	// Handle conversion of already blind pawns
	[HarmonyPatch(typeof(Pawn_IdeoTracker), "SetIdeo")]
	public static class Ideo_Pawn_IdeoTracker_Patch
	{
		public static void Postfix(Pawn ___pawn)
		{
			if (___pawn.ShouldHaveTrueSightHediff())
			{
				var hediff = HediffMaker.MakeHediff(TS_DefOf.TS_TrueSight, ___pawn);
				___pawn.health.AddHediff(hediff);
			}
		}
	}

	public class Hediff_TrueSight : HediffWithComps
	{
		public override void PostAdd(DamageInfo? dinfo)
		{
			base.PostAdd(dinfo);
			TryChangeSeverity();
		}
		public override bool ShouldRemove => pawn.health.capacities.CapableOf(PawnCapacityDefOf.Sight) || !pawn.Ideo.IdeoApprovesOfBlindness();
		public override void Tick()
		{
			base.Tick();
			if (Find.TickManager.TicksGame % 60 == 0)
			{
				TryChangeSeverity();
			}
		}

		public void TryChangeSeverity()
		{
			if (ShouldChangeSeverity(out float newSeverity))
			{
				this.severityInt = newSeverity;
			}
		}

		private bool ShouldChangeSeverity(out float newSeverity)
		{
			if (!this.pawn.HasPsylink && this.severityInt > 0)
			{
				newSeverity = 0;
				return true;
			}
			var psylinkLevel = pawn.GetPsylinkLevel() / 10f;
			if (this.severityInt != psylinkLevel)
			{
				newSeverity = psylinkLevel;
				return true;
			}
			newSeverity = -1f;
			return false;
		}
	}
}
