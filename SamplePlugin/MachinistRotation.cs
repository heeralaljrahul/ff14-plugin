using System;
using System.Collections.Generic;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;

namespace SamplePlugin;

public class MachinistRotation : IDisposable
{
    // Machinist Action IDs - Single Target
    public const uint HeatedSplitShot = 7411;
    public const uint HeatedSlugShot = 7412;
    public const uint HeatedCleanShot = 7413;
    public const uint Drill = 16498;
    public const uint AirAnchor = 16500;
    public const uint ChainSaw = 25788;
    public const uint Excavator = 36981;
    public const uint FullMetalField = 36982;
    public const uint GaussRound = 2874;
    public const uint Ricochet = 2890;
    public const uint DoubleCheck = 36979;
    public const uint Checkmate = 36980;
    public const uint Hypercharge = 17209;
    public const uint HeatBlast = 7410;
    public const uint BlazingShot = 36978;
    public const uint Wildfire = 2878;
    public const uint Reassemble = 2876;
    public const uint BarrelStabilizer = 7414;

    // Reference to settings
    private MachinistSettings Settings => Plugin.PluginInterface.GetPluginConfig() is Configuration config
        ? config.Machinist
        : new MachinistSettings();

    // Rotation state
    public bool IsEnabled { get; set; }
    public bool IsInOpener { get; private set; }
    public int OpenerStep { get; private set; }
    public string LastAction { get; private set; } = "";
    public string NextAction { get; private set; } = "";
    public string RotationStatus { get; private set; } = "Idle";

    // Heat tracking - read from game gauge
    public int CurrentHeat { get; private set; }
    public int CurrentBattery { get; private set; }

    // Timing - minimal lockout just to prevent spam
    private DateTime lastActionTime = DateTime.MinValue;
    private DateTime lastGcdTime = DateTime.MinValue;
    private const float MinActionDelay = 0.1f; // Minimum delay between action attempts

    // Hypercharge state
    private bool isInHypercharge;
    private int hyperchargeStacks;

    // Opener sequence - optimized for max DPS
    private readonly List<(uint ActionId, bool IsOGcd, string Name)> openerSequence =
    [
        (Reassemble, true, "Reassemble"),
        (AirAnchor, false, "Air Anchor"),
        (GaussRound, true, "Gauss Round"),
        (Ricochet, true, "Ricochet"),
        (Drill, false, "Drill"),
        (BarrelStabilizer, true, "Barrel Stabilizer"),
        (ChainSaw, false, "Chain Saw"),
        (GaussRound, true, "Gauss Round"),
        (Ricochet, true, "Ricochet"),
        (Excavator, false, "Excavator"),
        (Reassemble, true, "Reassemble"),
        (FullMetalField, false, "Full Metal Field"),
        (GaussRound, true, "Gauss Round"),
        (Ricochet, true, "Ricochet"),
        (Hypercharge, true, "Hypercharge"),
        (HeatBlast, false, "Heat Blast"),
        (Wildfire, true, "Wildfire"),
        (HeatBlast, false, "Heat Blast"),
        (GaussRound, true, "Gauss Round"),
        (HeatBlast, false, "Heat Blast"),
        (Ricochet, true, "Ricochet"),
        (HeatBlast, false, "Heat Blast"),
        (GaussRound, true, "Gauss Round"),
        (HeatBlast, false, "Heat Blast"),
        (Ricochet, true, "Ricochet"),
    ];

    public MachinistRotation()
    {
        Plugin.Framework.Update += OnFrameworkUpdate;
    }

    public void Dispose()
    {
        Plugin.Framework.Update -= OnFrameworkUpdate;
    }

    public void OnComboButtonPressed()
    {
        if (!IsEnabled)
        {
            IsEnabled = true;
            if (Settings.UseOpener)
            {
                StartOpener();
            }
            else
            {
                RotationStatus = "Running";
            }
            Plugin.Log.Information("Auto-rotation started via combo button press");
        }
    }

    public void StartOpener()
    {
        IsInOpener = true;
        OpenerStep = 0;
        RotationStatus = "Opener Active";
        Plugin.Log.Information("Machinist opener started");
    }

    public void StopRotation()
    {
        IsEnabled = false;
        IsInOpener = false;
        OpenerStep = 0;
        isInHypercharge = false;
        hyperchargeStacks = 0;
        RotationStatus = "Stopped";
        Plugin.Log.Information("Machinist rotation stopped");
    }

    public void ResetOpener()
    {
        IsInOpener = false;
        OpenerStep = 0;
        isInHypercharge = false;
        hyperchargeStacks = 0;
        RotationStatus = IsEnabled ? "Running" : "Idle";
    }

    private unsafe void OnFrameworkUpdate(IFramework framework)
    {
        if (!IsEnabled)
            return;

        // Read actual heat gauge from game
        ReadJobGauge();

        var target = Plugin.TargetManager.Target;
        if (target == null || target is not IBattleChara battleTarget)
        {
            RotationStatus = "No Target";
            NextAction = "Waiting for target...";
            return;
        }

        if (battleTarget.IsDead)
        {
            RotationStatus = "Target Dead";
            NextAction = "Waiting for target...";
            return;
        }

        // Small delay to prevent spamming
        var timeSinceLastAction = (float)(DateTime.Now - lastActionTime).TotalSeconds;
        if (timeSinceLastAction < MinActionDelay)
            return;

        if (IsInOpener)
        {
            ExecuteOpener(target.GameObjectId);
        }
        else
        {
            ExecuteRotation(target.GameObjectId);
        }
    }

    private unsafe void ReadJobGauge()
    {
        try
        {
            var jobGauge = JobGaugeManager.Instance();
            if (jobGauge != null)
            {
                // MCH job gauge is at offset for machinist
                // Heat is stored in the gauge
                var gaugeData = (byte*)jobGauge;
                // JobGaugeManager structure: first 8 bytes header, then gauge data
                // MCH gauge: Heat at offset 0, Battery at offset 2, etc.
                CurrentHeat = gaugeData[8];  // Heat gauge
                CurrentBattery = gaugeData[10]; // Battery gauge
            }
        }
        catch
        {
            // If we can't read the gauge, keep the last known values
        }
    }

    private unsafe void ExecuteOpener(ulong targetId)
    {
        if (OpenerStep >= openerSequence.Count)
        {
            IsInOpener = false;
            RotationStatus = "Opener Complete";
            Plugin.Log.Information("Opener completed");
            return;
        }

        var (actionId, isOGcd, actionName) = openerSequence[OpenerStep];

        if (!IsActionEnabledInSettings(actionId))
        {
            OpenerStep++;
            return;
        }

        NextAction = $"[Opener] {actionName}";

        var actionManager = ActionManager.Instance();
        if (actionManager == null)
            return;

        // Check if action is ready
        if (actionManager->GetActionStatus(ActionType.Action, actionId) != 0)
            return;

        // Try to use the action
        if (actionManager->UseAction(ActionType.Action, actionId, targetId))
        {
            LastAction = actionName;
            lastActionTime = DateTime.Now;
            if (!isOGcd)
                lastGcdTime = DateTime.Now;

            if (actionId == Hypercharge)
            {
                isInHypercharge = true;
                hyperchargeStacks = 5;
            }
            else if (actionId == HeatBlast || actionId == BlazingShot)
            {
                hyperchargeStacks--;
                if (hyperchargeStacks <= 0)
                    isInHypercharge = false;
            }

            OpenerStep++;
            RotationStatus = $"Opener {OpenerStep}/{openerSequence.Count}";
            Plugin.Log.Information($"Opener: {actionName}");
        }
    }

    private unsafe void ExecuteRotation(ulong targetId)
    {
        var actionManager = ActionManager.Instance();
        if (actionManager == null)
            return;

        RotationStatus = $"Heat: {CurrentHeat} | Battery: {CurrentBattery}";

        // Priority system for maximum DPS:
        // 1. Heat Blast during Hypercharge (must use all 5)
        // 2. Burst tools when ready (Air Anchor > Drill > Chain Saw > Excavator > FMF)
        // 3. oGCDs (weave Gauss/Ricochet, Barrel Stab, Hypercharge, Wildfire)
        // 4. Basic combo as filler

        // During Hypercharge, spam Heat Blast
        if (isInHypercharge && hyperchargeStacks > 0)
        {
            if (Settings.UseHeatBlast && TryUseGcd(actionManager, HeatBlast, targetId, "Heat Blast"))
            {
                hyperchargeStacks--;
                if (hyperchargeStacks <= 0)
                    isInHypercharge = false;
                return;
            }
            // Also try BlazingShot (upgraded Heat Blast)
            if (Settings.UseHeatBlast && TryUseGcd(actionManager, BlazingShot, targetId, "Blazing Shot"))
            {
                hyperchargeStacks--;
                if (hyperchargeStacks <= 0)
                    isInHypercharge = false;
                return;
            }
        }

        // Try burst GCDs first (highest DPS priority)
        if (TryUseBurstGcds(actionManager, targetId))
            return;

        // Weave oGCDs
        if (TryUseOGcds(actionManager, targetId))
            return;

        // Filler: Basic combo
        TryUseCombo(actionManager, targetId);
    }

    private unsafe bool TryUseBurstGcds(ActionManager* actionManager, ulong targetId)
    {
        // Priority: Air Anchor > Drill > Chain Saw > Excavator > Full Metal Field
        // Use Reassemble before high-damage tools

        // Air Anchor
        if (Settings.UseAirAnchor && IsActionReady(actionManager, AirAnchor))
        {
            // Use Reassemble if available
            if (Settings.UseReassemble && IsActionReady(actionManager, Reassemble))
                TryUseOGcd(actionManager, Reassemble, targetId, "Reassemble");

            if (TryUseGcd(actionManager, AirAnchor, targetId, "Air Anchor"))
                return true;
        }

        // Drill
        if (Settings.UseDrill && IsActionReady(actionManager, Drill))
        {
            if (Settings.UseReassemble && IsActionReady(actionManager, Reassemble))
                TryUseOGcd(actionManager, Reassemble, targetId, "Reassemble");

            if (TryUseGcd(actionManager, Drill, targetId, "Drill"))
                return true;
        }

        // Chain Saw
        if (Settings.UseChainSaw && IsActionReady(actionManager, ChainSaw))
        {
            if (Settings.UseReassemble && IsActionReady(actionManager, Reassemble))
                TryUseOGcd(actionManager, Reassemble, targetId, "Reassemble");

            if (TryUseGcd(actionManager, ChainSaw, targetId, "Chain Saw"))
                return true;
        }

        // Excavator (proc from Chain Saw)
        if (Settings.UseExcavator && IsActionReady(actionManager, Excavator))
        {
            if (TryUseGcd(actionManager, Excavator, targetId, "Excavator"))
                return true;
        }

        // Full Metal Field
        if (Settings.UseFullMetalField && IsActionReady(actionManager, FullMetalField))
        {
            if (TryUseGcd(actionManager, FullMetalField, targetId, "Full Metal Field"))
                return true;
        }

        return false;
    }

    private unsafe bool TryUseOGcds(ActionManager* actionManager, ulong targetId)
    {
        // Barrel Stabilizer - use on cooldown for heat
        if (Settings.UseBarrelStabilizer && IsActionReady(actionManager, BarrelStabilizer))
        {
            if (TryUseOGcd(actionManager, BarrelStabilizer, targetId, "Barrel Stabilizer"))
                return true;
        }

        // Hypercharge - use when heat >= 50, but MUST use at 100 to prevent overcap
        // Save 50 heat for Wildfire windows (every 2 min)
        if (Settings.UseHypercharge && !isInHypercharge && IsActionReady(actionManager, Hypercharge))
        {
            bool shouldHypercharge = false;

            // MUST use at 100 heat to prevent overcap
            if (CurrentHeat >= 100)
            {
                shouldHypercharge = true;
            }
            // Use at 95+ to prevent overcap from next combo action
            else if (CurrentHeat >= 95)
            {
                shouldHypercharge = true;
            }
            // Normal usage at 50+ heat
            else if (CurrentHeat >= 50)
            {
                // Check if Wildfire is ready - pair them together
                if (Settings.UseWildfire && IsActionReady(actionManager, Wildfire))
                {
                    shouldHypercharge = true;
                }
                // Otherwise use Hypercharge freely above 50 heat
                else
                {
                    shouldHypercharge = true;
                }
            }

            if (shouldHypercharge && TryUseOGcd(actionManager, Hypercharge, targetId, "Hypercharge"))
            {
                isInHypercharge = true;
                hyperchargeStacks = 5;
                return true;
            }
        }

        // Wildfire - use during Hypercharge for max damage
        if (Settings.UseWildfire && isInHypercharge && IsActionReady(actionManager, Wildfire))
        {
            if (TryUseOGcd(actionManager, Wildfire, targetId, "Wildfire"))
                return true;
        }

        // Gauss Round / Double Check - dump charges, don't overcap
        if (Settings.UseGaussRound)
        {
            if (IsActionReady(actionManager, GaussRound) && TryUseOGcd(actionManager, GaussRound, targetId, "Gauss Round"))
                return true;
            if (IsActionReady(actionManager, DoubleCheck) && TryUseOGcd(actionManager, DoubleCheck, targetId, "Double Check"))
                return true;
        }

        // Ricochet / Checkmate - dump charges, don't overcap
        if (Settings.UseRicochet)
        {
            if (IsActionReady(actionManager, Ricochet) && TryUseOGcd(actionManager, Ricochet, targetId, "Ricochet"))
                return true;
            if (IsActionReady(actionManager, Checkmate) && TryUseOGcd(actionManager, Checkmate, targetId, "Checkmate"))
                return true;
        }

        return false;
    }

    private unsafe bool TryUseCombo(ActionManager* actionManager, ulong targetId)
    {
        // The game handles combo state internally - just try actions in order
        // and the game will tell us which is ready via GetActionStatus

        // Try Clean Shot (combo finisher) - game will only allow if combo is ready
        if (IsActionReady(actionManager, HeatedCleanShot))
        {
            if (TryUseGcd(actionManager, HeatedCleanShot, targetId, "Heated Clean Shot"))
                return true;
        }

        // Try Slug Shot (combo 2)
        if (IsActionReady(actionManager, HeatedSlugShot))
        {
            if (TryUseGcd(actionManager, HeatedSlugShot, targetId, "Heated Slug Shot"))
                return true;
        }

        // Try Split Shot (combo starter) - always available
        if (IsActionReady(actionManager, HeatedSplitShot))
        {
            if (TryUseGcd(actionManager, HeatedSplitShot, targetId, "Heated Split Shot"))
                return true;
        }

        return false;
    }

    private unsafe bool TryUseGcd(ActionManager* actionManager, uint actionId, ulong targetId, string actionName)
    {
        if (actionManager->GetActionStatus(ActionType.Action, actionId) != 0)
            return false;

        if (actionManager->UseAction(ActionType.Action, actionId, targetId))
        {
            LastAction = actionName;
            NextAction = actionName;
            lastActionTime = DateTime.Now;
            lastGcdTime = DateTime.Now;
            Plugin.Log.Information($"GCD: {actionName}");
            return true;
        }
        return false;
    }

    private unsafe bool TryUseOGcd(ActionManager* actionManager, uint actionId, ulong targetId, string actionName)
    {
        if (actionManager->GetActionStatus(ActionType.Action, actionId) != 0)
            return false;

        if (actionManager->UseAction(ActionType.Action, actionId, targetId))
        {
            LastAction = actionName;
            lastActionTime = DateTime.Now;
            Plugin.Log.Information($"oGCD: {actionName}");
            return true;
        }
        return false;
    }

    private unsafe bool IsActionReady(ActionManager* actionManager, uint actionId)
    {
        return actionManager->GetActionStatus(ActionType.Action, actionId) == 0;
    }

    private bool IsActionEnabledInSettings(uint actionId)
    {
        return actionId switch
        {
            Drill => Settings.UseDrill,
            AirAnchor => Settings.UseAirAnchor,
            ChainSaw => Settings.UseChainSaw,
            Excavator => Settings.UseExcavator,
            FullMetalField => Settings.UseFullMetalField,
            Reassemble => Settings.UseReassemble,
            BarrelStabilizer => Settings.UseBarrelStabilizer,
            Hypercharge => Settings.UseHypercharge,
            Wildfire => Settings.UseWildfire,
            GaussRound or DoubleCheck => Settings.UseGaussRound,
            Ricochet or Checkmate => Settings.UseRicochet,
            HeatBlast or BlazingShot => Settings.UseHeatBlast,
            _ => true
        };
    }

    public string GetNextActionPreview()
    {
        if (!IsEnabled)
            return "Rotation disabled";

        if (IsInOpener && OpenerStep < openerSequence.Count)
            return $"[Opener] {openerSequence[OpenerStep].Name}";

        return string.IsNullOrEmpty(NextAction) ? "Ready" : NextAction;
    }

    public int GetEnabledAbilityCount()
    {
        var count = 0;
        if (Settings.UseDrill) count++;
        if (Settings.UseAirAnchor) count++;
        if (Settings.UseChainSaw) count++;
        if (Settings.UseExcavator) count++;
        if (Settings.UseFullMetalField) count++;
        if (Settings.UseReassemble) count++;
        if (Settings.UseBarrelStabilizer) count++;
        if (Settings.UseHypercharge) count++;
        if (Settings.UseHeatBlast) count++;
        if (Settings.UseWildfire) count++;
        if (Settings.UseGaussRound) count++;
        if (Settings.UseRicochet) count++;
        return count;
    }
}
