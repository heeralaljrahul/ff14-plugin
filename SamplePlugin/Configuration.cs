using Dalamud.Configuration;
using System;

namespace SamplePlugin;

[Serializable]
public class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 0;

    public bool IsConfigWindowMovable { get; set; } = true;
    public bool SomePropertyToBeSavedAndWithADefault { get; set; } = true;

    // Machinist Rotation Settings
    public MachinistSettings Machinist { get; set; } = new();

    public void Save()
    {
        Plugin.PluginInterface.SavePluginConfig(this);
    }
}

[Serializable]
public class MachinistSettings
{
    // Burst Mode
    public bool BurstModeEnabled { get; set; } = true;

    // GCD Abilities
    public bool UseDrill { get; set; } = true;
    public bool UseAirAnchor { get; set; } = true;
    public bool UseChainSaw { get; set; } = true;
    public bool UseExcavator { get; set; } = true;
    public bool UseFullMetalField { get; set; } = true;

    // oGCD Abilities
    public bool UseReassemble { get; set; } = true;
    public bool UseBarrelStabilizer { get; set; } = true;
    public bool UseHypercharge { get; set; } = true;
    public bool UseWildfire { get; set; } = true;
    public bool UseGaussRound { get; set; } = true;
    public bool UseRicochet { get; set; } = true;

    // Basic Combo (usually always on)
    public bool UseBasicCombo { get; set; } = true;
    public bool UseHeatBlast { get; set; } = true;
}
