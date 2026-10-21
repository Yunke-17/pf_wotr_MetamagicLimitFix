using System;
using System.Linq;
using HarmonyLib;
using Kingmaker.RuleSystem.Rules;
using Kingmaker.UnitLogic;
using Kingmaker.UnitLogic.Abilities;
using Kingmaker.UnitLogic.FactLogic;
using UnityModManagerNet;

namespace MetamagicFix
{
    /// <summary>
    /// Metamagic Limit Fix (standalone MOD).
    ///
    /// Vanilla RuleCollectMetamagic.AddMetamagic (:47) predicts the whole metamagic set
    /// from a SINGLE metamagic's isolated cost, with a hard-coded 10 level cap:
    ///     if (m_SpellLevel + component.Metamagic.DefaultCost() > 10) return;
    /// So a 9th-level spell + Persistent (base +2) is rejected even with Favorite Metamagic (-1).
    ///
    /// Fix: let the original method run, then in a Postfix re-check with the DYNAMIC cap
    ///   limit = spellbook.MaxSpellLevel (9th-level books allow up to 10)
    ///   cost  = final cost of that metamagic (favorite -1, floor 0)
    /// and only add back the ones vanilla wrongly rejected.
    ///
    /// Always on by design: no settings UI, no localization.
    /// The Rift Mage MOD ships the same fix for its own archetype and yields to this MOD
    /// whenever this assembly is present.
    /// </summary>
    public static class Main
    {
        internal static UnityModManager.ModEntry.ModLogger Log;
        public static bool Enabled { get; private set; }

        /// <summary>Always true; kept so the Rift Mage MOD can detect that this MOD is active.</summary>
        public static bool FixEnabled { get { return true; } }

        public static bool Load(UnityModManager.ModEntry modEntry)
        {
            Log = modEntry.Logger;

            try
            {
                var harmony = new Harmony(modEntry.Info.Id);
                harmony.PatchAll(typeof(Main).Assembly);

                Enabled = true;
                Log.Log("MetamagicFix loaded (always on).");
                return true;
            }
            catch (Exception e)
            {
                Log.Error("Failed to load: " + e);
                return false;
            }
        }
    }

    [HarmonyPatch(typeof(RuleCollectMetamagic), nameof(RuleCollectMetamagic.AddMetamagic))]
    internal static class MetamagicLimitPatch
    {
        [HarmonyPostfix]
        private static void Postfix(RuleCollectMetamagic __instance, Feature metamagicFeature, int ___m_SpellLevel)
        {
            if (!Main.Enabled) return;
            if (__instance == null || metamagicFeature == null) return;

            var component = metamagicFeature.GetComponent<AddMetamagicFeat>();
            if (component == null) return;

            // Already accepted by the original method -> nothing to do.
            if (__instance.SpellMetamagics.Contains(metamagicFeature)) return;

            // Mirror the vanilla guard: an invalid base spell level must not be restored.
            if (___m_SpellLevel < 0 || ___m_SpellLevel >= 10) return;

            var spellbook = __instance.Spellbook;
            var spell = __instance.Spell;
            if (spellbook == null || spell == null) return;

            var metamagic = component.Metamagic;

            // The spell itself does not support this metamagic -> do not restore.
            if ((spell.AvailableMetamagic & metamagic) != metamagic) return;

            var caster = spellbook.Owner?.Unit;
            int cost = GetSingleCost(metamagic, caster);

            // Dynamic cap, same shape as the UI layer (AbilityDataVM.cs:92).
            int max = spellbook.MaxSpellLevel;
            int limit = (max == 9) ? max + 1 : max;

            if (___m_SpellLevel + cost <= limit)
            {
                __instance.SpellMetamagics.Add(metamagicFeature);
            }
        }

        /// <summary>Final cost of a single metamagic (favorite -1, floor 0).</summary>
        private static int GetSingleCost(Metamagic metamagic, Kingmaker.EntitySystem.Entities.UnitEntityData caster)
        {
            int cost = metamagic.DefaultCost();
            if (IsFavoriteMetamagic(metamagic, caster)) cost--;
            if (metamagic != Metamagic.CompletelyNormal) cost = Math.Max(0, cost);
            return cost;
        }

        /// <summary>Mirror of the Favorite Metamagic check in RuleApplyMetamagic.cs:38.</summary>
        private static bool IsFavoriteMetamagic(Metamagic metamagic, Kingmaker.EntitySystem.Entities.UnitEntityData caster)
        {
            if (caster == null) return false;
            var f = caster.State?.Features;
            if (f == null) return false;

            switch (metamagic)
            {
                case Metamagic.Empower: return f.FavoriteMetamagicEmpower;
                case Metamagic.Maximize: return f.FavoriteMetamagicMaximize;
                case Metamagic.Quicken: return f.FavoriteMetamagicQuicken;
                case Metamagic.Reach: return f.FavoriteMetamagicReach;
                case Metamagic.Extend: return f.FavoriteMetamagicExtend;
                case Metamagic.Selective: return f.FavoriteMetamagicSelective;
                case Metamagic.Bolstered: return f.FavoriteMetamagicBolstered;
                case Metamagic.Persistent: return f.FavoriteMetamagicPersistent;
                case Metamagic.Piercing: return f.FavoriteMetamagicPiercing;
                case Metamagic.Intensified: return f.FavoriteMetamagicIntensified;
                default: return false;
            }
        }
    }
}
