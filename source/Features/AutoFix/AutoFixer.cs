﻿using System;
using System.Collections.Generic;
using System.Linq;
using BattleTech;
using CustomComponents;
using MechEngineer.Features.ArmorStructureRatio;
using MechEngineer.Features.DynamicSlots;
using MechEngineer.Features.Engines;
using MechEngineer.Features.Engines.Helper;
using MechEngineer.Features.OverrideTonnage;
using UnityEngine;

namespace MechEngineer.Features.AutoFix
{
    internal class AutoFixer : IAutoFixMechDef
    {
        internal static AutoFixer Shared = new AutoFixer();

        public void AutoFix(List<MechDef> mechDefs, SimGameState simgame)
        {
            // we dont fix save games anymore, have to have money and time to fix an ongoing campaign
            if (simgame != null)
            {
                return;
            }
            
            foreach (var mechDef in mechDefs)
            {
                try
                {
                    AutoFixMechDef(mechDef);
                }
                catch (Exception e)
                {
                    Control.mod.Logger.LogError(e);
                }
            }
        }

        public void AutoFixMechDef(MechDef mechDef)
        {
            if (!AutoFixerFeature.settings.MechDefEngine)
            {
                return;
            }

            if (AutoFixerFeature.settings.MechDefSkip.Contains(mechDef.Description.Id)
                || AutoFixerFeature.settings.MechDefSkip.Contains(mechDef.Chassis.Description.Id))
            {
                return;
            }

            if (AutoFixUtils.IsIgnoredByTags(mechDef.Chassis.ChassisTags, AutoFixerFeature.settings.ChassisDefTagsSkip)
                || AutoFixUtils.IsIgnoredByTags(mechDef.MechTags, AutoFixerFeature.settings.MechDefTagsSkip))
            {
                return;
            }
            
            //DumpAllAsTable();
            if (mechDef.Inventory.Any(c => c.Def.GetComponent<EngineCoreDef>() != null))
            {
                return;
            }

            Control.mod.Logger.Log($"Auto fixing mechDef={mechDef.Description.Id} chassisDef={mechDef.Chassis.Description.Id}");

            ArmorStructureRatioFeature.Shared.AutoFixMechDef(mechDef);
            
            var builder = new MechDefBuilder(mechDef.Chassis, mechDef.Inventory.ToList());
            var standardHeatSinkDef = mechDef.DataManager.GetDefaultEngineHeatSinkDef();
            var engineHeatSinkDef = builder.Inventory
                                        .Select(r => r.Def.GetComponent<CoolingDef>())
                                        .Where(d => d != null)
                                        .Select(d => mechDef.DataManager.HeatSinkDefs.Get(d.HeatSinkDefId))
                                        .Where(d => d != null)
                                        .Select(d => d.GetComponent<EngineHeatSinkDef>())
                                        .FirstOrDefault() ?? standardHeatSinkDef;

            float freeTonnage;
            {
                float currentTotalTonnage = 0, maxValue = 0;
                MechStatisticsRules.CalculateTonnage(mechDef, ref currentTotalTonnage, ref maxValue);
                var maxFreeTonnage = mechDef.Chassis.Tonnage - currentTotalTonnage;

                var initialTonnage = mechDef.Chassis.InitialTonnage;
                var originalInitialTonnage = ChassisHandler.GetOriginalInitialTonnage(mechDef.Chassis) ?? initialTonnage;
                var initialTonnageGain = Mathf.Max(0, originalInitialTonnage - initialTonnage);
                if (AutoFixerFeature.settings.MechDefAutoFixAgainstMaxFreeTonnage.Contains(mechDef.Description.Id))
                {
                    freeTonnage = maxFreeTonnage;
                }
                else
                {
                    var freeTonnageThreshold = AutoFixerFeature.settings.MechDefAutoFixInitialTonnageDiffThreshold;
                    freeTonnage = Mathf.Min(maxFreeTonnage, initialTonnageGain+freeTonnageThreshold);
                }

                Control.mod.Logger.LogDebug($"freeTonnage={freeTonnage}" +
                                            $" currentTotalTonnage={currentTotalTonnage}" +
                                            $" maxFreeTonnage={maxFreeTonnage}" +
                                            $" initialTonnageGain={initialTonnageGain}" +
                                            $" initialGainSmaller={initialTonnageGain < maxFreeTonnage}");
            }

            //Control.mod.Logger.LogDebug("C maxEngineTonnage=" + maxEngineTonnage);
            var standardWeights = new Weights(); // use default gyro and weights
            var standardHeatBlock = mechDef.DataManager.HeatSinkDefs.Get(AutoFixerFeature.settings.MechDefHeatBlockDef).GetComponent<EngineHeatBlockDef>();
            var standardCooling = mechDef.DataManager.HeatSinkDefs.Get(AutoFixerFeature.settings.MechDefCoolingDef).GetComponent<CoolingDef>();

            var engineCoreDefs = mechDef.DataManager.HeatSinkDefs
                .Select(hs => hs.Value)
                .Select(hs => hs.GetComponent<EngineCoreDef>())
                .Where(c => c != null)
                .OrderByDescending(x => x.Rating);

            Engine maxEngine = null;
            {
                //var heatSinks = builder.Inventory.Where(x => x.ComponentDefType == ComponentType.HeatSink && x.Def.Is<EngineHeatSinkDef>()).ToList();
                var jumpJetList = builder.Inventory.Where(x => x.ComponentDefType == ComponentType.JumpJet).ToList();
                var engines = new LinkedList<Engine>();
                
                foreach (var coreDef in engineCoreDefs)
                {
                    {
                        var engine = new Engine(standardCooling, standardHeatBlock, coreDef, standardWeights, new List<MechComponentRef>());
                        engines.AddFirst(engine);
                    }
                    
                    {
                        // remove superfluous jump jets
                        var maxJetCount = coreDef.GetMovement(mechDef.Chassis.Tonnage).JumpJetCount;
                        //Control.mod.Logger.LogDebug($"before Inventory.Count={builder.Inventory.Count} jumpJetList.Count={jumpJetList.Count} maxJetCount={maxJetCount}");
                        while (jumpJetList.Count > maxJetCount)
                        {
                            var lastIndex = jumpJetList.Count - 1;
                            var jumpJet = jumpJetList[lastIndex];
                            freeTonnage += jumpJet.Def.Tonnage;
                            builder.Remove(jumpJet);
                            jumpJetList.Remove(jumpJet);
                        }
                        //Control.mod.Logger.LogDebug($"after Inventory.Count={builder.Inventory.Count} jumpJetList.Count={jumpJetList.Count} maxJetCount={maxJetCount}");
                    }

                    foreach (var engine in engines)
                    {
//                        Control.mod.Logger.LogDebug($"D engine={engine.CoreDef} engine.TotalTonnage={engine.TotalTonnage} freeTonnage={freeTonnage}");
                        
                        if (engine.TotalTonnage <= freeTonnage)
                        {
                            maxEngine = engine;
                        }
                        else
                        {
                            break;
                        }
                    }
                    if (maxEngine != null)
                    {
                        break;
                    }
                }
            }

            if (maxEngine == null)
            {
                return;
            }

            Control.mod.Logger.LogDebug($" maxEngine={maxEngine.CoreDef} freeTonnage={freeTonnage}");
            {
                var dummyCore = builder.Inventory.FirstOrDefault(r => r.ComponentDefID == AutoFixerFeature.settings.MechDefCoreDummy);
                if (dummyCore != null)
                {
                    builder.Remove(dummyCore);
                }
            }

            // add engine
            builder.Add(maxEngine.CoreDef.Def,ChassisLocations.CenterTorso, true);

            if (!EngineFeature.settings.AllowMixingHeatSinkTypes)
            {
                // remove incompatible heat sinks
                var incompatibleHeatSinks = builder.Inventory
                    .Where(r => r.Def.Is<EngineHeatSinkDef>(out var hs) && hs.HSCategory != engineHeatSinkDef.HSCategory)
                    .ToList();
                foreach (var incompatibleHeatSink in incompatibleHeatSinks)
                {
                    builder.Remove(incompatibleHeatSink);
                }

                //Control.mod.Logger.LogDebug($"Inventory.Count={builder.Inventory.Count} incompatibleHeatSinks.Count={incompatibleHeatSinks.Count}");
                // add same amount of compatible heat sinks
                foreach (var unused in incompatibleHeatSinks)
                {
                    builder.Add(engineHeatSinkDef.Def);
                }

                //Control.mod.Logger.LogDebug($"Inventory.Count={builder.Inventory.Count}");
            }

            // add free heatsinks
            {
                //var maxFree = maxEngine.CoreDef.ExternalHeatSinksFreeMaxCount;
                //var current = maxEngine.ExternalHeatSinkCount;
                var maxFree = maxEngine.HeatSinkExternalFreeMaxCount;
                var current = 0; //we assume exiting heatsinks on the mech are additional and not free
                for (var i = current; i < maxFree; i++)
                {
                    if (!builder.Add(engineHeatSinkDef.Def))
                    {
                        break;
                    }
                }
                //Control.mod.Logger.LogDebug($"Inventory.Count={builder.Inventory.Count} maxFree={maxFree}");
            }
            
            // find any overused location
            if (builder.HasOveruseAtAnyLocation())
            {
                // heatsinks, upgrades
                var itemsToBeReordered = builder.Inventory
                    .Where(IsReorderable)
                    .OrderBy(c => MechDefBuilder.LocationCount(c.Def.AllowedLocations))
                    .ThenByDescending(c => c.Def.InventorySize)
                    .ThenByDescending(c =>
                    {
                        switch (c.ComponentDefType)
                        {
                            case ComponentType.Upgrade:
                                return 2;
                            case ComponentType.AmmunitionBox:
                                return 1;
                            default:
                                return 0;
                        }
                    })
                    .ToList();

                // remove all items that can be reordered: heatsinks, upgrades
                foreach (var item in itemsToBeReordered)
                {
                    builder.Remove(item);
                }

                // then add most restricting, and then largest items first (probably double head sinks)
                foreach (var item in itemsToBeReordered)
                {
                    // couldn't add everything
                    if (!builder.Add(item.Def))
                    {
                        return;
                    }
                }
            }

            mechDef.SetInventory(builder.Inventory.OrderBy(element => element, new OrderComparer()).ToArray());

            //{
            //    float currentTotalTonnage = 0, maxValue = 0;
            //    MechStatisticsRules.CalculateTonnage(mechDef, ref currentTotalTonnage, ref maxValue);
            //    Control.mod.Logger.LogDebug($" end currentTotalTonnage={currentTotalTonnage} mechDef.Chassis.Tonnage={mechDef.Chassis.Tonnage}");
            //}
        }

        private class OrderComparer : IComparer<MechComponentRef>
        {
            private readonly SorterComparer comparer = new SorterComparer();
            public int Compare(MechComponentRef x, MechComponentRef y)
            {
                return comparer.Compare(x?.Def, y?.Def);
            }
        }

        private static bool IsReorderable(MechComponentRef c)
        {
            var def = c?.Def;
            
            if (def == null)
            {
                return false;
            }

            if (c.IsFixed)
            {
                return false;
            }
            
            if (!(def.ComponentType >= ComponentType.AmmunitionBox && def.ComponentType <= ComponentType.Upgrade))
            {
                return false;
            }

            // items in arms and legs are usually bound to a certain side, so lets ignore them from relocation
            if (MechDefBuilder.LocationCount(def.AllowedLocations) <= 2)
            {
                return false;
            }

            if (def.Is<Category>(out var category) && category.CategoryDescriptor.UniqueForLocation)
            {
                return false;
            }

            return true;
        }
    }
}