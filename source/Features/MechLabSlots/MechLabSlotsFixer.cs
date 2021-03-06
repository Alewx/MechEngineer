﻿using BattleTech.UI;
using System.Linq;

namespace MechEngineer.Features.MechLabSlots
{
    internal class MechLabSlotsFixer
    {
        internal static void FixSlots(WidgetLayout widgetLayout, int ___maxSlots)
        {
            // MechPropertiesWidget feature
            if (widgetLayout.widget == (widgetLayout.widget.parentDropTarget as MechLabPanel).centerTorsoWidget)
            {
                ___maxSlots -= MechLabSlotsFeature.settings.MechLabGeneralSlots;
            }

            ModifyLayoutSlotCount(widgetLayout, ___maxSlots);
        }

        internal static void ModifyLayoutSlotCount(WidgetLayout layout, int maxSlots)
        {
            var slots = layout.slots;
            var changedSlotCount = maxSlots - slots.Count;

            if (changedSlotCount == 0)
            {
                return;
            }

            var templateSlot = slots[0];

            // add missing
            int index = slots[0].GetSiblingIndex();
            for (var i = slots.Count; i < maxSlots; i++)
            {
                var newSlot = UnityEngine.Object.Instantiate(templateSlot, layout.layout_slots);
                //newSlot.localPosition = new Vector3(0, -(1 + i * SlotHeight), 0);
                newSlot.SetSiblingIndex(index + i);
                newSlot.name = "slot (" + i + ")";
                slots.Add(newSlot);
            }

            // remove abundant
            while (slots.Count > maxSlots)
            {
                var slot = slots.Last();
                slots.RemoveAt(slots.Count - 1);
                UnityEngine.Object.Destroy(slot.gameObject);
            }
        }
      
    }
}