using System;
using MelonLoader;
using BTD_Mod_Helper;
using BTD_Mod_Helper.Api.Enums;
using BTD_Mod_Helper.Api.ModOptions;
using BTD_Mod_Helper.Extensions;
using HarmonyLib;
using Il2CppAssets.Scripts.Models.Towers.Upgrades;
using Il2CppAssets.Scripts.Simulation.Towers;
using Il2CppAssets.Scripts.Simulation.Towers.Behaviors;
using Il2CppAssets.Scripts.Unity;
using Il2CppAssets.Scripts.Unity.UI_New.InGame;
using Il2CppAssets.Scripts.Unity.UI_New.InGame.TowerSelectionMenu;
using Il2CppAssets.Scripts.Unity.UI_New.Popups;
using Paragonomics;

[assembly: MelonInfo(typeof(ParagonomicsMod), ModHelperData.Name, ModHelperData.Version, ModHelperData.RepoOwner)]
[assembly: MelonGame("Ninja Kiwi", "BloonsTD6")]
[assembly: MelonGame("Ninja Kiwi", "BloonsTD6-Epic")]
[assembly: MelonOptionalDependencies("BuffsInShop")]

namespace Paragonomics;

public class ParagonomicsMod : BloonsTD6Mod
{
    public const int MinimumCostFactor = 100;

    public static readonly ModSettingBool AllowDegreeSettingOutsideSandbox = new(false)
    {
        description = "Allows the cheat-y Set Degree feature to be used outside Sandbox mode.",
        icon = VanillaSprites.SandboxBtn
    };

    public static readonly ModSettingBool NoDegreeLimit = new(false)
    {
        description = "Removes the standard degree 100 limit for the purposes of investing and degree setting.",
        icon = VanillaSprites.PerfectParagonIcon
    };

    public static readonly ModSettingBool LinearScalingPastLimit = new(false)
    {
        description =
            "Makes the power investment requirement scaling be linear when the degree increases beyond 100, rather than continuing to be exponentially more."
    };

    public override object? Call(string operation, params object[] p) => operation switch
    {
        nameof(GetDegree) when p.CheckTypes(out float investment) => GetDegree(investment),
        _ => null
    };

    public static void DegreeBtnPressed(ParagonTower paragon)
    {
        Calculations.DegreeCalculationHints.Enqueue(paragon.GetCurrentDegree());
        PopupScreen.instance.SafelyQueue(screen => screen.ShowSetValuePopup(
            "Set Paragon Degree",
            "Directly set the degree of this paragon at no cost.",
            new Action<int>(degree => SetDegree(paragon, degree)),
            GetDegree(paragon.investmentInfo.totalInvestment)));
    }

    public static UpgradeModel GetParagonUpgrade(Tower tower)
    {
        var gameModel = InGame.instance != null ? InGame.Bridge.Model : Game.instance.model;

        return tower.IsMutatedBy("HonoraryParagon") && ModHelper.HasMod("HonoraryParagons", out var honoraryParagons)
            ? honoraryParagons.Call<UpgradeModel>("GetParagonUpgrade", gameModel, tower.towerModel)
            : gameModel.GetParagonUpgradeForTowerId(tower.towerModel.baseId);
    }

    public static bool HasEnoughToShowPopup(Tower tower)
    {
        var upgrade = GetParagonUpgrade(tower);

        if (InGame.Bridge.GetCash() * MinimumCostFactor >= upgrade.cost) return true;

        StartPopup();
        PopupScreen.instance.SafelyQueue(screen => screen.ShowOkPopup(
            "You must have at least 1% of the Paragon's Cost in cash.",
            new Action(FinishPopup)
        ));

        return false;
    }

    public static void InvestBtnPressed(ParagonTower paragon, Action<double>? okCallback = null,
        Action? cancelCallback = null)
    {
        if (!HasEnoughToShowPopup(paragon.tower)) return;

        StartPopup();
        PopupScreen.instance.SafelyQueue(screen => screen.ShowParagonConfirmationPopup(
            PopupScreen.Placement.inGameCenter,
            "Invest in Paragon",
            "Add to your current Paragon Power investment.",
            new Action<double>(cash =>
            {
                FinishPopup();
                InvestInParagon(paragon, cash);
                okCallback?.Invoke(cash);
            }),
            "Do It",
            new Action(() =>
            {
                FinishPopup();
                cancelCallback?.Invoke();
            }),
            "Cancel",
            Popup.TransitionAnim.Scale,
            (int) InGame.Bridge.GetCash(),
            int.MaxValue,
            0));
    }

    public static void NegativeParagonPopup(TowerSelectionMenu tsm, int index)
    {
        if (!HasEnoughToShowPopup(tsm.selectedTower.tower)) return;

        var upgrade = GetParagonUpgrade(tsm.selectedTower.tower);

        var minCost = upgrade.cost / MinimumCostFactor;

        if (tsm.Bridge.GetCash() >= upgrade.cost || tsm.Bridge.GetCash() < minCost ||
            !tsm.selectedTower.CanUpgradeToParagon(true)) return;

        StartPopup();
        PopupScreen.instance.SafelyQueue(screen => screen.ShowParagonConfirmationPopup(
            PopupScreen.Placement.inGameCenter,
            "Subprime Paragon",
            "You dont have enough cash to initiate a full Paragon sacrifice. However, you can spend the cash you do have to get a weaker, Negative-Degree Paragon and invest more later.",
            new Action<double>(amount =>
            {
                FinishPopup();
                tsm.isUpgradePopupShowing = false;
                var cost = amount + minCost;

                var tower = tsm.selectedTower.tower;
                tsm.UpgradeTower(upgrade, index, (float) cost, cost - upgrade.cost);
                tower.worth -= upgrade.cost;
                tower.worth += (float) cost;
            }),
            "Do It",
            new Action(FinishPopup),
            "Cancel",
            Popup.TransitionAnim.Scale,
            (int) InGame.Bridge.GetCash() - minCost,
            upgrade.cost,
            minCost
        ));
    }

    public static void StartPopup()
    {
        InGame.instance.StopClock();
        InGame.instance.hotkeys.SuppressHotkeys = true;
        TowerSelectionMenu.instance.isUpgradePopupShowing = true;
    }

    public static void FinishPopup()
    {
        InGame.instance.ResumeClock();
        InGame.instance.hotkeys.SuppressHotkeys = false;
        TowerSelectionMenu.instance.isUpgradePopupShowing = false;
    }

    public static int GetDegree(float investment)
    {
        var paragonDegreeDataModel = InGame.Bridge.Model.paragonDegreeDataModel;
        var powerDegreeRequirements = paragonDegreeDataModel.powerDegreeRequirements;

        if (investment < 0 || (investment > paragonDegreeDataModel.MaxInvestment && NoDegreeLimit))
        {
            return (int) Calculations.PowerToDegree(investment);
        }

        var degree = 0;
        while (degree < powerDegreeRequirements.Length && investment >= powerDegreeRequirements[degree])
        {
            degree++;
        }

        return degree;
    }

    public static void SetDegree(ParagonTower paragon, int degree)
    {
        var gameModel = paragon.Sim.model;
        var degreeDataModel = gameModel.paragonDegreeDataModel;

        if (degree < 1 || degree > degreeDataModel.powerDegreeRequirements.Length)
        {
            paragon.investmentInfo = new ParagonTower.InvestmentInfo
            {
                totalInvestment = (float) Calculations.DegreeToPower(degree)
            };
            Calculations.DegreeCalculationHints.Enqueue(degree);
        }
        else
        {
            paragon.investmentInfo = new ParagonTower.InvestmentInfo
            {
                totalInvestment = degreeDataModel.powerDegreeRequirements[degree - 1]
            };
        }

        OnDegreeChanged(paragon);
    }

    public static void InvestInParagon(ParagonTower paragon, double investment)
    {
        var degreeDataModel = paragon.Sim.model.paragonDegreeDataModel;

        var paragonCost = GetParagonUpgrade(paragon.tower).cost;

        var powerFromMoneySpent = (float) investment * degreeDataModel.moneySpentOverX /
                                  ((1 + degreeDataModel.paidContributionPenalty) * Math.Max(paragonCost, 1));

        paragon.investmentInfo = paragon.investmentInfo with
        {
            totalInvestment = paragon.investmentInfo.totalInvestment + powerFromMoneySpent
        };

        OnDegreeChanged(paragon);

        InGame.Bridge.SetCash(InGame.Bridge.GetCash() - investment, InGame.Bridge.GetInputId());
    }


    public static void OnDegreeChanged(ParagonTower paragon)
    {
        paragon.UpdateDegree();
        paragon.PlayParagonUpgradeSound();
        paragon.Finish();
        paragon.entity.GetBehaviorsInDependants<TowerCreateParagonTower>().ForEach(tower => tower.towerAdded = false);

        if (TowerSelectionMenu.instance?.selectedTower?.tower == paragon.tower)
        {
            TowerSelectionMenu.instance.OnTowerSelectionPanelForceUpdate();
        }
    }
}
