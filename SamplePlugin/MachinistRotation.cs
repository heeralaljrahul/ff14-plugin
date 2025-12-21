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
    public int ComboStep { get; private set; }
    public string LastAction { get; private set; } = "";
    public string NextAction { get; private set; } = "";
    public string RotationStatus { get; private set; } = "Idle";

    // Timing
    private DateTime lastActionTime = DateTime.MinValue;
    private DateTime lastGcdTime = DateTime.MinValue;
    private const float GcdLockout = 0.6f;
    private const float OGcdLockout = 0.6f;
    private bool isInHypercharge;
    private int hyperchargeStacks;

    // Opener sequence (standard level 100 opener) - filtered by settings at runtime
    private readonly List<(uint ActionId, bool IsOGcd, string Name)> openerSequence =
    [
        (Reassemble, true, "Reassemble"),
        (AirAnchor, false, "Air Anchor"),
        (GaussRound, true, "Gauss Round"),
        (Ricochet, true, "Ricochet"),
        (Drill, false, "Drill"),
        (BarrelStabilizer, true, "Barrel Stabilizer"),
        (GaussRound, true, "Gauss Round"),
        (HeatedSplitShot, false, "Heated Split Shot"),
        (Ricochet, true, "Ricochet"),
        (HeatedSlugShot, false, "Heated Slug Shot"),
        (GaussRound, true, "Gauss Round"),
        (HeatedCleanShot, false, "Heated Clean Shot"),
        (Ricochet, true, "Ricochet"),
        (Reassemble, true, "Reassemble"),
        (ChainSaw, false, "Chain Saw"),
        (GaussRound, true, "Gauss Round"),
        (Ricochet, true, "Ricochet"),
        (Hypercharge, true, "Hypercharge"),
        (HeatBlast, false, "Heat Blast 1"),
        (Wildfire, true, "Wildfire"),
        (HeatBlast, false, "Heat Blast 2"),
        (GaussRound, true, "Gauss Round"),
        (HeatBlast, false, "Heat Blast 3"),
        (Ricochet, true, "Ricochet"),
        (HeatBlast, false, "Heat Blast 4"),
        (GaussRound, true, "Gauss Round"),
        (HeatBlast, false, "Heat Blast 5"),
        (Ricochet, true, "Ricochet"),
        (Drill, false, "Drill"),
    ];

    public MachinistRotation()
    {
        Plugin.Framework.Update += OnFrameworkUpdate;
    }

    public void Dispose()
    {
        Plugin.Framework.Update -= OnFrameworkUpdate;
    }

    /// <summary>
    /// Called when user presses any basic combo button - auto-starts the rotation
    /// </summary>
    public void OnComboButtonPressed()
    {
        if (!IsEnabled)
        {
            IsEnabled = true;

            // Start opener if enabled in settings
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
        ComboStep = 0;
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
        ComboStep = 0;
        isInHypercharge = false;
        hyperchargeStacks = 0;
        RotationStatus = IsEnabled ? "Running" : "Idle";
    }

    private void OnFrameworkUpdate(IFramework framework)
    {
        if (!IsEnabled)
            return;

        // Use the player's selected target
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

        var timeSinceLastAction = (float)(DateTime.Now - lastActionTime).TotalSeconds;
        if (timeSinceLastAction < 0.1f)
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

    private unsafe void ExecuteOpener(ulong targetId)
    {
        if (OpenerStep >= openerSequence.Count)
        {
            IsInOpener = false;
            RotationStatus = "Opener Complete - Running";
            Plugin.Log.Information("Machinist opener completed, switching to rotation");
            return;
        }

        var (actionId, isOGcd, actionName) = openerSequence[OpenerStep];

        // Check if this action is enabled in settings - skip if disabled
        if (!IsActionEnabledInSettings(actionId))
        {
            OpenerStep++;
            return;
        }

        NextAction = actionName;

        var actionManager = ActionManager.Instance();
        if (actionManager == null)
            return;

        var actionStatus = actionManager->GetActionStatus(ActionType.Action, actionId);
        if (actionStatus != 0)
            return;

        var timeSinceLastGcd = (float)(DateTime.Now - lastGcdTime).TotalSeconds;
        if (!isOGcd && timeSinceLastGcd < GcdLockout)
            return;

        var timeSinceLastAction = (float)(DateTime.Now - lastActionTime).TotalSeconds;
        if (isOGcd && timeSinceLastAction < OGcdLockout)
            return;

        var result = actionManager->UseAction(ActionType.Action, actionId, targetId);
        if (result)
        {
            LastAction = actionName;
            lastActionTime = DateTime.Now;
            if (!isOGcd)
                lastGcdTime = DateTime.Now;

            OpenerStep++;
            RotationStatus = $"Opener {OpenerStep}/{openerSequence.Count}";
            UpdateComboState(actionId);
            Plugin.Log.Information($"Opener step {OpenerStep}: {actionName}");
        }
    }

    private unsafe void ExecuteRotation(ulong targetId)
    {
        var actionManager = ActionManager.Instance();
        if (actionManager == null)
            return;

        RotationStatus = "Running";

        var timeSinceLastGcd = (float)(DateTime.Now - lastGcdTime).TotalSeconds;
        var timeSinceLastAction = (float)(DateTime.Now - lastActionTime).TotalSeconds;

        // Try to weave oGCDs if we're in the GCD window
        if (timeSinceLastGcd >= 0.6f && timeSinceLastGcd < 2.0f && timeSinceLastAction >= OGcdLockout)
        {
            if (TryUseOGcd(actionManager, targetId))
                return;
        }

        if (timeSinceLastGcd < GcdLockout)
            return;

        // Check for Heat Blast during Hypercharge
        if (isInHypercharge && hyperchargeStacks > 0 && Settings.UseHeatBlast)
        {
            if (TryUseAction(actionManager, HeatBlast, targetId, "Heat Blast", false))
            {
                hyperchargeStacks--;
                if (hyperchargeStacks <= 0)
                    isInHypercharge = false;
                return;
            }
        }

        // Priority: Burst GCDs > Combo GCDs
        if (TryUseBurstGcd(actionManager, targetId))
            return;

        // Basic combo
        TryUseComboGcd(actionManager, targetId);
    }

    private unsafe bool TryUseOGcd(ActionManager* actionManager, ulong targetId)
    {
        // Barrel Stabilizer
        if (Settings.UseBarrelStabilizer && IsActionReady(actionManager, BarrelStabilizer))
        {
            if (TryUseAction(actionManager, BarrelStabilizer, targetId, "Barrel Stabilizer", true))
                return true;
        }

        // Hypercharge
        if (Settings.UseHypercharge && !isInHypercharge && IsActionReady(actionManager, Hypercharge))
        {
            if (TryUseAction(actionManager, Hypercharge, targetId, "Hypercharge", true))
            {
                isInHypercharge = true;
                hyperchargeStacks = 5;
                return true;
            }
        }

        // Wildfire during Hypercharge
        if (Settings.UseWildfire && isInHypercharge && IsActionReady(actionManager, Wildfire))
        {
            if (TryUseAction(actionManager, Wildfire, targetId, "Wildfire", true))
                return true;
        }

        // Gauss Round / Double Check
        if (Settings.UseGaussRound)
        {
            if (IsActionReady(actionManager, GaussRound))
            {
                if (TryUseAction(actionManager, GaussRound, targetId, "Gauss Round", true))
                    return true;
            }
            if (IsActionReady(actionManager, DoubleCheck))
            {
                if (TryUseAction(actionManager, DoubleCheck, targetId, "Double Check", true))
                    return true;
            }
        }

        // Ricochet / Checkmate
        if (Settings.UseRicochet)
        {
            if (IsActionReady(actionManager, Ricochet))
            {
                if (TryUseAction(actionManager, Ricochet, targetId, "Ricochet", true))
                    return true;
            }
            if (IsActionReady(actionManager, Checkmate))
            {
                if (TryUseAction(actionManager, Checkmate, targetId, "Checkmate", true))
                    return true;
            }
        }

        return false;
    }

    private unsafe bool TryUseBurstGcd(ActionManager* actionManager, ulong targetId)
    {
        var hasReassemble = Settings.UseReassemble && IsActionReady(actionManager, Reassemble);

        // Drill
        if (Settings.UseDrill && IsActionReady(actionManager, Drill))
        {
            if (hasReassemble)
                TryUseAction(actionManager, Reassemble, targetId, "Reassemble", true);

            if (TryUseAction(actionManager, Drill, targetId, "Drill", false))
                return true;
        }

        // Air Anchor
        if (Settings.UseAirAnchor && IsActionReady(actionManager, AirAnchor))
        {
            if (hasReassemble && !(Settings.UseDrill && IsActionReady(actionManager, Drill)))
                TryUseAction(actionManager, Reassemble, targetId, "Reassemble", true);

            if (TryUseAction(actionManager, AirAnchor, targetId, "Air Anchor", false))
                return true;
        }

        // Chain Saw
        if (Settings.UseChainSaw && IsActionReady(actionManager, ChainSaw))
        {
            if (hasReassemble && !(Settings.UseDrill && IsActionReady(actionManager, Drill)) && !(Settings.UseAirAnchor && IsActionReady(actionManager, AirAnchor)))
                TryUseAction(actionManager, Reassemble, targetId, "Reassemble", true);

            if (TryUseAction(actionManager, ChainSaw, targetId, "Chain Saw", false))
                return true;
        }

        // Excavator
        if (Settings.UseExcavator && IsActionReady(actionManager, Excavator))
        {
            if (TryUseAction(actionManager, Excavator, targetId, "Excavator", false))
                return true;
        }

        // Full Metal Field
        if (Settings.UseFullMetalField && IsActionReady(actionManager, FullMetalField))
        {
            if (TryUseAction(actionManager, FullMetalField, targetId, "Full Metal Field", false))
                return true;
        }

        return false;
    }

    private unsafe bool TryUseComboGcd(ActionManager* actionManager, ulong targetId)
    {
        var nextComboAction = ComboStep switch
        {
            0 => (HeatedSplitShot, "Heated Split Shot"),
            1 => (HeatedSlugShot, "Heated Slug Shot"),
            2 => (HeatedCleanShot, "Heated Clean Shot"),
            _ => (HeatedSplitShot, "Heated Split Shot")
        };

        if (TryUseAction(actionManager, nextComboAction.Item1, targetId, nextComboAction.Item2, false))
        {
            UpdateComboState(nextComboAction.Item1);
            return true;
        }

        if (ComboStep != 0)
        {
            ComboStep = 0;
            return TryUseAction(actionManager, HeatedSplitShot, targetId, "Heated Split Shot", false);
        }

        return false;
    }

    private unsafe bool TryUseAction(ActionManager* actionManager, uint actionId, ulong targetId, string actionName, bool isOGcd)
    {
        var actionStatus = actionManager->GetActionStatus(ActionType.Action, actionId);
        if (actionStatus != 0)
            return false;

        var result = actionManager->UseAction(ActionType.Action, actionId, targetId);
        if (result)
        {
            LastAction = actionName;
            lastActionTime = DateTime.Now;
            if (!isOGcd)
                lastGcdTime = DateTime.Now;

            Plugin.Log.Information($"Rotation used: {actionName}");
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
            HeatedSplitShot or HeatedSlugShot or HeatedCleanShot => true, // Basic combo always enabled
            _ => true
        };
    }

    private void UpdateComboState(uint actionId)
    {
        ComboStep = actionId switch
        {
            HeatedSplitShot => 1,
            HeatedSlugShot => 2,
            HeatedCleanShot => 0,
            _ => ComboStep
        };
    }

    public string GetNextActionPreview()
    {
        if (!IsEnabled)
            return "Rotation disabled";

        if (IsInOpener && OpenerStep < openerSequence.Count)
            return $"[Opener] {openerSequence[OpenerStep].Name}";

        return string.IsNullOrEmpty(NextAction) ? "Basic combo" : NextAction;
    }

    /// <summary>
    /// Gets the count of abilities currently enabled in the combo
    /// </summary>
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
