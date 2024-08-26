using System;
using System.Collections.Generic;
using System.Linq;
using BTD_Mod_Helper;
using Il2CppAssets.Scripts.Models.Effects;
using Il2CppAssets.Scripts.Models.Towers.Behaviors;
using Il2CppAssets.Scripts.Simulation.Towers.Behaviors;
using Il2CppAssets.Scripts.Unity.UI_New.InGame;
using SMath = Il2CppAssets.Scripts.Simulation.SMath.Math;

namespace Paragonomics;

public static class Calculations
{
    /// <summary>
    /// Avoid unnecessary calculations if we're confident what the next ask will be
    /// </summary>
    public static readonly Queue<int> DegreeCalculationHints = new();

    public static double DegreeToPower(long d) => d switch
    {
        // Special Handling for negatives
        <= 0 => -DegreeToPower(-d + 1),
        1 => 0,
        100 => 200000,
        _ => Math.Round((50 * Math.Pow(d, 3) + 5025 * Math.Pow(d, 2) + 168324 * d + 843000) / 600)
    };

    public static long PowerToDegree(double power)
    {
        switch (power)
        {
            case 0:
                return 1;
            case < 0:
                return Math.Min(-PowerToDegree(-power) + 1, -1);
            case 200000:
                return 100;
        }

        const int maxIterations = 100;
        if (!DegreeCalculationHints.TryDequeue(out var hint)) hint = 100;

        double d = hint;

        for (var i = 1; i < maxIterations; i++)
        {
            var currentPower = DegreeToPower((long) d);
            var nextPower = DegreeToPower((long) d + 1);

            if (currentPower <= power && nextPower > power) return (long) d;

            // Update d using the Newton-Raphson method
            var error = currentPower - power;
            var derivative = (150 * Math.Pow(d, 2) + 10050 * d + 168324) / 600;
            var delta = error / derivative;
            d -= delta;
            d = Math.Max(1, d);
        }

#if DEBUG
        ModHelper.Warning<ParagonomicsMod>(
            $"Did not converge within {maxIterations} iterations for power {power} and hint {hint}");
#endif
        
        return (int) d;
    }

    /// <summary>
    /// Version of <see cref="ParagonTower.GetDisplayDegreeIndex"/> that uses the degree directly
    /// </summary>
    /// <param name="paragon"></param>
    /// <param name="degree"></param>
    /// <returns></returns>
    public static int GetDisplayDegreeIndex(ParagonTower paragon, int degree)
    {
        var displayDegreePaths = paragon.paragonTowerModel.displayDegreePaths;

        if (!displayDegreePaths.Any()) return -1;

        var powerDegreeRequirements = paragon.Sim.model.paragonDegreeDataModel.powerDegreeRequirements;

        return Math.Min(degree / (powerDegreeRequirements.Length / displayDegreePaths.Length),
            powerDegreeRequirements.Length - 1);
    }

    /// <summary>
    /// A scale factor based on the relative cost of tower models and paragon upgrades
    /// </summary>
    /// <param name="paragon"></param>
    /// <returns></returns>
    public static float GetCostFactor(ParagonTower paragon)
    {
        var baseId = paragon.tower.towerModel.baseId;
        var gameModel = paragon.Sim.model;

        var totalTowersCost = Math.Max(gameModel.GetTower(baseId).cost, 1) * 3;
        var totalUpgradesCost = gameModel.GetTowersWithBaseId(baseId)
            .Where(tower => !tower.isParagon)
            .SelectMany(tower => tower.appliedUpgrades)
            .Distinct()
            .Select(gameModel.GetUpgrade)
            .Sum(upgrade => Math.Max(upgrade.cost, 1));
        var paragonUpgradesCost = Math.Max(gameModel.GetParagonUpgradeForTowerId(baseId).cost, 10);

        return (totalTowersCost + totalUpgradesCost) / (totalTowersCost + totalUpgradesCost + paragonUpgradesCost);
    }

    /// <summary>
    /// Version of <see cref="ParagonTower.GetDegreeMutator"/> that supports negatives and uses the degree directly
    /// </summary>
    public static ParagonTowerModel.PowerDegreeMutator GetDegreeMutator(ParagonTower paragon, int degree,
        AssetPathModel displayDegree)
    {
        var info = InGame.Bridge.Simulation.model.paragonDegreeDataModel;

        var degreeMinusOne = degree - 1;

        var attackCooldownReductionPercent = SMath.Sign(degreeMinusOne) *
            SMath.Round(SMath.Sqrt(SMath.Abs(degreeMinusOne) * info.attackCooldownReductionX) * 10) / 10;
        var percentPierceUp = degreeMinusOne * info.piercePercentPerDegree;
        var additionalPierceUp = degreeMinusOne * info.pierceIncreasePerDegree;
        var percentDamageUp = degreeMinusOne * info.damagePercentPerDegree;
        var additionalDamageUp = degreeMinusOne / info.damageIncreaseForDegrees * info.damageIncreasePerDegree;
        if (degree >= info.degreeCount)
        {
            percentPierceUp += info.piercePercentPerDegree;
            additionalPierceUp += info.piercePercentPerDegree;
            additionalDamageUp += info.damageIncreasePerDegree;
        }

        var bonusBossDamagePercent = 0f;
        if (degree >= info.bonusBossDamagePerDegrees)
        {
            bonusBossDamagePercent = degree / (float) info.bonusBossDamagePerDegrees * info.bonusBossDamagePercent;
        }

        var displayDegreeIndex = GetDisplayDegreeIndex(paragon, degree) + 1;

        // This part is added to better scale negative degrees
        if (degreeMinusOne < 0)
        {
            // Don't include flat modifiers since they can make things negative
            additionalDamageUp = SMath.Max(0, additionalDamageUp);
            additionalPierceUp = SMath.Max(0, additionalPierceUp);

            // Make the multiplicative bonuses scale to not go -100%
            // but also increase their base effect since not including the flat modifiers
            var factor = GetCostFactor(paragon);
            percentPierceUp = -(100 + 10000 / (-100 + percentPierceUp / SMath.Pow(factor, 2)));
            percentDamageUp = -(100 + 10000 / (-100 + percentDamageUp / SMath.Pow(factor, 2)));
        }

        return new ParagonTowerModel.PowerDegreeMutator(degree,
            attackCooldownReductionPercent,
            percentPierceUp,
            additionalPierceUp,
            percentDamageUp,
            (int) additionalDamageUp,
            displayDegreeIndex,
            displayDegree,
            paragon.paragonTowerModel.changeAttackDisplay,
            paragon.paragonTowerModel.changeAirUnitDisplay,
            bonusBossDamagePercent);
    }
}