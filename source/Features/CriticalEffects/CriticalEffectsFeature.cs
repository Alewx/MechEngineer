﻿using System.Collections.Generic;
using System.Linq;
using BattleTech;
using CustomComponents;
using FluffyUnderware.DevTools.Extensions;
using MechEngineer.Features.CriticalEffects.Patches;
using MechEngineer.Features.LocationalEffects;
using UnityEngine;

namespace MechEngineer.Features.CriticalEffects
{
    internal class CriticalEffectsFeature : Feature<CriticalEffectsSettings>
    {
        internal static readonly CriticalEffectsFeature Shared = new CriticalEffectsFeature();

        internal override bool Enabled => base.Enabled && LocationalEffectsFeature.Shared.Loaded;

        internal override CriticalEffectsSettings Settings => Control.settings.CriticalEffects;

        internal static CriticalEffectsSettings settings => Shared.Settings;

        internal override void SetupResources(Dictionary<string, Dictionary<string, VersionManifestEntry>> customResources)
        {
            Resources = SettingsResourcesTools.Enumerate<EffectData>("MECriticalEffects", customResources)
                .ToDictionary(entry => entry.Description.Id);
        }

        private static Dictionary<string, EffectData> Resources { get; set; } = new Dictionary<string, EffectData>();

        internal static EffectData GetEffectData(string effectId)
        {
            if (Resources.TryGetValue(effectId, out var effectData))
            {
                return effectData;
            }
            
            Control.mod.Logger.LogError($"Can't find critical effect id '{effectId}'");
            return null;
        }

        public void ProcessWeaponHit(MechComponent mechComponent, WeaponHitInfo hitInfo, ref ComponentDamageLevel damageLevel)
        {
            if (mechComponent.parent == null)
            {
                return;
            }

            if (mechComponent.DamageLevel == ComponentDamageLevel.Destroyed) // already destroyed
            {
                return;
            }

            var criticalEffects = mechComponent.GetCriticalEffects();
            if (criticalEffects == null)
            {
                return;
            }

            var actor = mechComponent.parent;

            damageLevel = ComponentDamageLevel.Penalized;

            int critsPrev;
            int critsNext;
            int critsAdded;
            {
                critsPrev = criticalEffects.HasLinked ? mechComponent.CriticalSlotsHitLinked() : mechComponent.CriticalSlotsHit();
                var critsMax = criticalEffects.MaxHits;

                var slots = mechComponent.CriticalSlots(); // critical slots left

                var locationDestroyed = actor.StructureForLocation(mechComponent.Location) <= 0f;
                var critsHit = locationDestroyed ? slots : Mathf.Min(1, slots);

                critsNext = Mathf.Min(critsMax, critsPrev + critsHit);
                if (critsNext >= critsMax)
                {
                    damageLevel = ComponentDamageLevel.Destroyed;
                }
                if (criticalEffects.HasLinked)
                {
                    mechComponent.CriticalSlotsHitLinked(critsNext);
                }

                critsAdded = Mathf.Max(critsNext - critsPrev, 0);
                
                var slotsHitPrev = mechComponent.CriticalSlotsHit();
                mechComponent.CriticalSlotsHit(slotsHitPrev + critsAdded);

                Control.mod.Logger.LogDebug(
                    $"uid={mechComponent.uid} (Id={mechComponent.Description.Id} Location={mechComponent.Location}) " +
                    $"critsAdded={critsAdded} critsMax={critsMax} " +
                    $"critsPrev={critsPrev} critsNext={critsNext} " +
                    $"critsHit={critsHit} " +
                    $"slots={slots} slotsHitPrev={slotsHitPrev} " +
                    $"damageLevel={damageLevel} " +
                    $"HasLinked={criticalEffects.HasLinked}"
                );
            }

            {
                SetDamageLevel(mechComponent, hitInfo, damageLevel);
            
                if (criticalEffects.HasLinked)
                {
                    var scopedId = mechComponent.ScopedId(criticalEffects.LinkedStatisticName, true);
                
                    Control.mod.Logger.LogDebug($"HasLinked scopeId={scopedId}");
                    foreach (var otherMechComponent in actor.allComponents)
                    {
                        if (otherMechComponent.DamageLevel == ComponentDamageLevel.Destroyed)
                        {
                            continue;
                        }

                        var otherCriticalEffects = otherMechComponent.GetCriticalEffects();
                        if (otherCriticalEffects == null)
                        {
                            continue;
                        }
                        
                        if (!otherCriticalEffects.HasLinked)
                        {
                            continue;
                        }
                    
                        var otherScopedId = otherMechComponent.ScopedId(otherCriticalEffects.LinkedStatisticName, true);
                        if (scopedId == otherScopedId)
                        {
                            SetDamageLevel(otherMechComponent, hitInfo, damageLevel);
                        }
                    }
                }
            }

            {
                // cancel effects
                var effectIds = new string[0];
                
                if (critsPrev > 0 && critsPrev <= criticalEffects.PenalizedEffectIDs.Length)
                {
                    effectIds = effectIds.AddRange(criticalEffects.PenalizedEffectIDs[critsPrev - 1]);
                }
                
                if (damageLevel == ComponentDamageLevel.Destroyed)
                {
                    effectIds = effectIds.AddRange(criticalEffects.OnDestroyedDisableEffectIds);
                }
                
                foreach (var effectId in effectIds)
                {
                    var util = new EffectIdUtil(effectId, mechComponent, criticalEffects);
                    util.CancelCriticalEffect();
                }
            }

            {
                // create effects
                var effectIds = new string[0];
                
                if (critsNext > 0 && critsNext <= criticalEffects.PenalizedEffectIDs.Length)
                {
                    effectIds = criticalEffects.PenalizedEffectIDs[critsNext - 1];
                }
                
                if (damageLevel == ComponentDamageLevel.Destroyed)
                {
                    effectIds = criticalEffects.OnDestroyedEffectIDs;
                }
                
                // collect disabled effects, probably easier to cache these in a mech statistic
                var disabledEffectIds = DisabledScopedIdsOnActor(actor);
                //Control.mod.Logger.LogDebug($"disabledEffectIds={string.Join(",", disabledEffectIds.ToArray())}");
                foreach (var effectId in effectIds)
                {
                    var scopedId = mechComponent.ScopedId(effectId, criticalEffects.HasLinked);
                    if (disabledEffectIds.Contains(scopedId))
                    {
                        continue;
                    }
                    
                    var util = new EffectIdUtil(effectId, mechComponent, criticalEffects);
                    util.CreateCriticalEffect(damageLevel < ComponentDamageLevel.Destroyed);
                }
            }

            if (damageLevel == ComponentDamageLevel.Destroyed)
            {
                if (criticalEffects.DeathMethod != DeathMethod.NOT_SET)
                {
                    actor.FlagForDeath(
                        $"{mechComponent.UIName} DESTROYED",
                        criticalEffects.DeathMethod,
                        DamageType.Combat,
                        mechComponent.Location,
                        hitInfo.stackItemUID,
                        hitInfo.attackerId,
                        false);
                    actor.HandleDeath(hitInfo.attackerId);
                }

                if (!string.IsNullOrEmpty(criticalEffects.OnDestroyedVFXName))
                {
                    actor.GameRep.PlayVFX(mechComponent.Location, criticalEffects.OnDestroyedVFXName, true, Vector3.zero, true, -1f);
                }

                if (!string.IsNullOrEmpty(criticalEffects.OnDestroyedAudioEventName))
                {
                    WwiseManager.PostEvent(criticalEffects.OnDestroyedAudioEventName, actor.GameRep.audioObject);
                }
            }
        }

        private static void SetDamageLevel(MechComponent mechComponent, WeaponHitInfo hitInfo, ComponentDamageLevel damageLevel)
        {
            Control.mod.Logger.LogDebug($"damageLevel={damageLevel} uid={mechComponent.uid} (Id={mechComponent.Description.Id} Location={mechComponent.Location})");
            mechComponent.StatCollection.ModifyStat(
                hitInfo.attackerId,
                hitInfo.stackItemUID,
                "DamageLevel",
                StatCollection.StatOperation.Set,
                damageLevel);
        }

        private static HashSet<string> DisabledScopedIdsOnActor(AbstractActor actor)
        {
            var disabledEffectIds = new HashSet<string>();
            
            foreach (var mc in actor.allComponents)
            {
                if (mc.IsFunctional)
                {
                    continue;
                }

                var ce = mc.GetCriticalEffects();
                if (ce == null)
                {
                    continue;
                }

                foreach (var effectId in ce.OnDestroyedDisableEffectIds)
                {
                    var scopedId = mc.ScopedId(effectId, ce.HasLinked);
                    disabledEffectIds.Add(scopedId);
                }
            }

            return disabledEffectIds;
        }
        
        private class EffectIdUtil
        {
            private string effectId;
            private MechComponent mechComponent;
            private CriticalEffects ce;
    
            internal EffectIdUtil(string effectId, MechComponent mechComponent, CriticalEffects ce)
            {
                this.effectId = effectId;
                this.mechComponent = mechComponent;
                this.ce = ce;
            }
            
            internal void CreateCriticalEffect(bool tracked = true)
            {
                var scopedId = ScopedId();

                var effectData = GetEffectData(effectId);
                if (effectData == null)
                {
                    return;
                }
                if (effectData.targetingData.effectTriggerType != EffectTriggerType.Passive) // we only support passive for now
                {
                    return;
                }
                
                var actor = mechComponent.parent;

                LocationalEffectsFeature.ProcessLocationalEffectData(ref effectData, mechComponent);
                
                Control.mod.Logger.LogDebug($"Creating scopedId={scopedId} statName={effectData.statisticData.statName}");
                actor.Combat.EffectManager.CreateEffect(
                    effectData, scopedId, -1,
                    actor, actor,
                    default(WeaponHitInfo), 0, false);

                //DebugUtils.LogActor("CreateCriticalEffect", actor);
    
                if (tracked)
                {
                    // make sure created effects are removed once component got destroyed
                    mechComponent.createdEffectIDs.Add(scopedId);
                }
            }
    
            internal void CancelCriticalEffect()
            {
                var scopedId = ScopedId();
    
                var actor = mechComponent.parent;
                var statusEffects = actor.Combat.EffectManager
                    .GetAllEffectsWithID(scopedId)
                    .Where(e => e.Target == actor);
    
                Control.mod.Logger.LogDebug($"Canceling scopedId={scopedId}");
                foreach (var statusEffect in statusEffects)
                {
                    Control.mod.Logger.LogDebug($"Canceling statName={statusEffect.EffectData.statisticData.statName}");
                    actor.CancelEffect(statusEffect);
                }
                mechComponent.createdEffectIDs.Remove(scopedId);
            }
            
            private string ScopedId()
            {
                var id = $"MECriticalHitEffect_{effectId}_{mechComponent.parent.GUID}";
                return mechComponent.ScopedId(id, ce.HasLinked);
            }
        }
    }
}