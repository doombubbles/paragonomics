using System;
using System.Globalization;
using BTD_Mod_Helper.Api;
using BTD_Mod_Helper.Api.Components;
using BTD_Mod_Helper.Api.Enums;
using BTD_Mod_Helper.Extensions;
using HarmonyLib;
using Il2CppAssets.Scripts.Models.Effects;
using Il2CppAssets.Scripts.Models.Towers.Behaviors;
using Il2CppAssets.Scripts.Simulation.Towers.Behaviors;
using Il2CppAssets.Scripts.Unity.UI_New.InGame;
using Il2CppAssets.Scripts.Unity.UI_New.InGame.TowerSelectionMenu;
using UnityEngine;
using UnityEngine.UI;

namespace Paragonomics;

[HarmonyPatch(typeof(TowerSelectionMenu), nameof(TowerSelectionMenu.UpdateParagon))]
internal static class TowerSelectionMenu_UpdateParagon
{
    [HarmonyPostfix]
    internal static void Postfix(TowerSelectionMenu __instance)
    {
        var degree = __instance.paragonDegree.transform.parent.gameObject;

        if (!degree.GetComponent<Button>() &&
            (ParagonomicsMod.AllowDegreeSettingOutsideSandbox || __instance.Bridge.IsSandboxMode()))
        {
            var button = degree.AddComponent<Button>();
            button.transition = Selectable.Transition.Animation;

            var animator = degree.AddComponent<Animator>();
            animator.runtimeAnimatorController = Animations.GlobalButtonAnimation;
            animator.updateMode = AnimatorUpdateMode.UnscaledTime;

            degree.GetComponent<Image>().raycastTarget = true;

            button.SetOnClick(() =>
                ParagonomicsMod.DegreeBtnPressed(__instance.selectedTower.tower.GetTowerBehavior<ParagonTower>())
            );
        }

        if (!__instance.paragonDetails.GetComponentInChildrenByName<ModHelperButton>("ParagonomicsBtn"))
        {
            var title = __instance.paragonDetails.transform.Find("ParagonInfo/ParagonTitle").Cast<RectTransform>();

            var button = ModHelperButton.Create(new Info("ParagonomicsBtn")
                {
                    AnchorMin = title.anchorMin,
                    AnchorMax = title.anchorMax,
                    Pivot = title.pivot,
                    Position = title.localPosition,
                    Height = title.rect.height,
                    Width = title.rect.width + 50
                }, VanillaSprites.ParagonBtnLong,
                new Action(() => ParagonomicsMod.InvestBtnPressed(
                    __instance.selectedTower.tower.GetTowerBehavior<ParagonTower>()
                )));
            button.SetParent(title.parent);

            title.SetParent(button.transform);
            title.anchorMin = new Vector2(0, 0);
            title.anchorMax = new Vector2(1, 1);
            title.pivot = new Vector2(0.5f, 0.5f);
            title.sizeDelta = new Vector2(0, 0);
            title.localPosition = new Vector3(0, 10, 0);
        }
    }
}

[HarmonyPatch(typeof(ParagonTower), nameof(ParagonTower.GetDegreeMutator))]
internal static class ParagonTower_GetDegreeMutator
{
    [HarmonyPostfix]
    internal static void Postfix(ParagonTower __instance, AssetPathModel displayDegree, float investment,
        ref ParagonTowerModel.PowerDegreeMutator __result)
    {
        var paragonDegreeDataModel = __instance.Sim.model.paragonDegreeDataModel;

        if (investment < 0 || (investment > paragonDegreeDataModel.MaxInvestment && ParagonomicsMod.NoDegreeLimit))
        {
            __result = Calculations.GetDegreeMutator(__instance, ParagonomicsMod.GetDegree(investment),
                displayDegree);
        }
    }
}

[HarmonyPatch(typeof(TowerSelectionMenu), nameof(TowerSelectionMenu.UpgradeTower), typeof(int), typeof(bool))]
internal static class TowerSelectionMenu_UpgradeTower
{
    [HarmonyPrefix]
    internal static void Prefix(TowerSelectionMenu __instance, int index)
    {
        if (index >= __instance.upgradeButtons.Length ||
            __instance.upgradeButtons[index]?.upgradeButton?.IsParagon != true) return;

        ParagonomicsMod.NegativeParagonPopup(__instance, index);
    }
}

[HarmonyPatch(typeof(UpgradeButton), nameof(UpgradeButton.UpdateCostVisuals))]
internal static class UpgradeButton_UpdateCostVisuals
{
    [HarmonyPostfix]
    internal static void Postfix(UpgradeButton __instance)
    {
        var cash = InGame.Bridge.GetCash();
        if (__instance.IsParagon && cash < __instance.upgradeCost && __instance.tts != null &&
            cash * ParagonomicsMod.MinimumCostFactor >= __instance.upgradeCost)
        {
            var cost = __instance.upgradeCost.ToString(CultureInfo.InvariantCulture);
            __instance.Cost.SetText($"<color=green>{cost[..^2]}</color>{cost[^2..]}");
        }
    }
}