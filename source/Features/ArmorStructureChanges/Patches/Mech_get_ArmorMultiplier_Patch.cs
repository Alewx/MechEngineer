﻿using System;
using BattleTech;
using Harmony;

namespace MechEngineer.Features.ArmorStructureChanges.Patches
{
    [HarmonyPatch(typeof(Mech), "get_ArmorMultiplier")]
    public static class Mech_get_ArmorMultiplier_Patch
    {
        public static void Postfix(Mech __instance, ref float __result)
        {
            try
            {
                __result = __result * ArmorStructureChangesFeature.GetArmorFactorForMech(__instance);
            }
            catch (Exception e)
            {
                Control.mod.Logger.LogError(e);
            }
        }
    }
}
