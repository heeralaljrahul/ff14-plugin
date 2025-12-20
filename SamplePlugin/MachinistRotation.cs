using System;
using System.Collections.Generic;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;

namespace SamplePlugin;

public class MachinistRotation : IDisposable
{
    // Machinist Action IDs
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
    public const uint Automaton = 2864;
    public const uint QueenOverdrive = 16502;

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
    private const float GcdLockout = 0.6f; // Minimum time between GCDs
    private const float OGcdLockout = 0.6f; // Animation lock for oGCDs
    private bool isInHypercharge;
    private int hyperchargeStacks;

    // Opener sequence (standard level 100 opener)
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

        // Check if we have a valid target
        var target = Plugin.TargetManager.Target;
        if (target == null || target is not IBattleChara battleTarget)
        {
            RotationStatus = "No Target";
            NextAction = "Waiting for target...";
            return;
        }

        // Check if target is dead
        if (battleTarget.IsDead)
        {
            RotationStatus = "Target Dead";
            NextAction = "Waiting for target...";
            return;
        }

        // Check timing
        var timeSinceLastAction = (float)(DateTime.Now - lastActionTime).TotalSeconds;
        if (timeSinceLastAction < 0.1f)
            return; // Don't spam too fast

        // Execute rotation
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
            // Opener complete, switch to normal rotation
            IsInOpener = false;
            RotationStatus = "Opener Complete - Running";
            Plugin.Log.Information("Machinist opener completed, switching to rotation");
            return;
        }

        var (actionId, isOGcd, actionName) = openerSequence[OpenerStep];
        NextAction = actionName;

        var actionManager = ActionManager.Instance();
        if (actionManager == null)
            return;

        // Check if we can use this action
        var actionStatus = actionManager->GetActionStatus(ActionType.Action, actionId);
        if (actionStatus != 0)
        {
            // Action not ready, might need to wait or skip
            // For opener, we wait for the action to be ready
            return;
        }

        // Check GCD/oGCD timing
        var timeSinceLastGcd = (float)(DateTime.Now - lastGcdTime).TotalSeconds;
        if (!isOGcd && timeSinceLastGcd < GcdLockout)
            return;

        var timeSinceLastAction = (float)(DateTime.Now - lastActionTime).TotalSeconds;
        if (isOGcd && timeSinceLastAction < OGcdLockout)
            return;

        // Execute the action
        var result = actionManager->UseAction(ActionType.Action, actionId, targetId);
        if (result)
        {
            LastAction = actionName;
            lastActionTime = DateTime.Now;
            if (!isOGcd)
                lastGcdTime = DateTime.Now;

            OpenerStep++;
            RotationStatus = $"Opener {OpenerStep}/{openerSequence.Count}";

            // Update combo state for the basic combo
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

        // Check GCD timing
        var timeSinceLastGcd = (float)(DateTime.Now - lastGcdTime).TotalSeconds;
        var timeSinceLastAction = (float)(DateTime.Now - lastActionTime).TotalSeconds;

        // Determine what to use next
        // Priority system:
        // 1. Burst tools (Drill, Air Anchor, Chain Saw) when off cooldown and Reassemble available
        // 2. Hypercharge when at 50+ heat and burst tools on cooldown
        // 3. Heat Blast during Hypercharge
        // 4. oGCDs (Gauss Round, Ricochet) between GCDs
        // 5. Basic combo filler

        // Try to weave oGCDs first if we're in the GCD window
        if (timeSinceLastGcd >= 0.6f && timeSinceLastGcd < 2.0f && timeSinceLastAction >= OGcdLockout)
        {
            if (TryUseOGcd(actionManager, targetId))
                return;
        }

        // Check if GCD is ready
        if (timeSinceLastGcd < GcdLockout)
            return;

        // Priority: Burst GCDs > Hypercharge GCDs > Combo GCDs

        // Check for burst tools
        if (TryUseBurstGcd(actionManager, targetId))
            return;

        // Check for Heat Blast during Hypercharge
        if (isInHypercharge && hyperchargeStacks > 0)
        {
            if (TryUseAction(actionManager, HeatBlast, targetId, "Heat Blast", false))
            {
                hyperchargeStacks--;
                if (hyperchargeStacks <= 0)
                    isInHypercharge = false;
                return;
            }
        }

        // Basic combo
        TryUseComboGcd(actionManager, targetId);
    }

    private unsafe bool TryUseOGcd(ActionManager* actionManager, ulong targetId)
    {
        // Priority: Reassemble (before burst) > Barrel Stabilizer > Hypercharge > Wildfire > Gauss Round > Ricochet

        // Barrel Stabilizer
        if (IsActionReady(actionManager, BarrelStabilizer))
        {
            if (TryUseAction(actionManager, BarrelStabilizer, targetId, "Barrel Stabilizer", true))
                return true;
        }

        // Hypercharge when we have enough heat (check via action readiness)
        if (!isInHypercharge && IsActionReady(actionManager, Hypercharge))
        {
            if (TryUseAction(actionManager, Hypercharge, targetId, "Hypercharge", true))
            {
                isInHypercharge = true;
                hyperchargeStacks = 5;
                return true;
            }
        }

        // Wildfire during Hypercharge
        if (isInHypercharge && IsActionReady(actionManager, Wildfire))
        {
            if (TryUseAction(actionManager, Wildfire, targetId, "Wildfire", true))
                return true;
        }

        // Gauss Round / Double Check
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

        // Ricochet / Checkmate
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

        return false;
    }

    private unsafe bool TryUseBurstGcd(ActionManager* actionManager, ulong targetId)
    {
        // Check Reassemble first, use before burst tool
        var hasReassemble = IsActionReady(actionManager, Reassemble);

        // Priority: Drill > Air Anchor > Chain Saw > Excavator
        if (IsActionReady(actionManager, Drill))
        {
            // Use Reassemble if available
            if (hasReassemble)
                TryUseAction(actionManager, Reassemble, targetId, "Reassemble", true);

            if (TryUseAction(actionManager, Drill, targetId, "Drill", false))
                return true;
        }

        if (IsActionReady(actionManager, AirAnchor))
        {
            if (hasReassemble && !IsActionReady(actionManager, Drill))
                TryUseAction(actionManager, Reassemble, targetId, "Reassemble", true);

            if (TryUseAction(actionManager, AirAnchor, targetId, "Air Anchor", false))
                return true;
        }

        if (IsActionReady(actionManager, ChainSaw))
        {
            if (hasReassemble && !IsActionReady(actionManager, Drill) && !IsActionReady(actionManager, AirAnchor))
                TryUseAction(actionManager, Reassemble, targetId, "Reassemble", true);

            if (TryUseAction(actionManager, ChainSaw, targetId, "Chain Saw", false))
                return true;
        }

        if (IsActionReady(actionManager, Excavator))
        {
            if (TryUseAction(actionManager, Excavator, targetId, "Excavator", false))
                return true;
        }

        if (IsActionReady(actionManager, FullMetalField))
        {
            if (TryUseAction(actionManager, FullMetalField, targetId, "Full Metal Field", false))
                return true;
        }

        return false;
    }

    private unsafe bool TryUseComboGcd(ActionManager* actionManager, ulong targetId)
    {
        // Basic 1-2-3 combo based on combo state
        var nextComboAction = ComboStep switch
        {
            0 => (HeatedSplitShot, "Heated Split Shot"),
            1 => (HeatedSlugShot, "Heated Slug Shot"),
            2 => (HeatedCleanShot, "Heated Clean Shot"),
            _ => (HeatedSplitShot, "Heated Split Shot")
        };

        // Check if the combo action is ready (will fail if wrong combo step)
        if (TryUseAction(actionManager, nextComboAction.Item1, targetId, nextComboAction.Item2, false))
        {
            UpdateComboState(nextComboAction.Item1);
            return true;
        }

        // If combo broke, restart from step 1
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

        return NextAction;
    }
}
