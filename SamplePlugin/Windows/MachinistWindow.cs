using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;

namespace SamplePlugin.Windows;

public class MachinistWindow : Window, IDisposable
{
    private readonly string machinistImagePath;
    private readonly Plugin plugin;

    // Machinist combo ability information
    private static readonly ComboAbility[] BasicCombo =
    [
        new("Heated Split Shot", "1", "Delivers an attack with a potency of 200. Additional Effect: Increases Heat Gauge by 5"),
        new("Heated Slug Shot", "2", "Delivers an attack with a potency of 120. Combo Potency: 320. Combo Bonus: Increases Heat Gauge by 5"),
        new("Heated Clean Shot", "3", "Delivers an attack with a potency of 120. Combo Potency: 400. Combo Bonus: Increases Heat Gauge by 5, Battery Gauge by 10")
    ];

    private static readonly ComboAbility[] BurstAbilities =
    [
        new("Reassemble", "oGCD", "Guarantees critical direct hit on next weaponskill. Recast: 55s"),
        new("Drill", "GCD", "Delivers an attack with a potency of 600. Recast: 20s"),
        new("Air Anchor", "GCD", "Delivers an attack with a potency of 600. Battery +20. Recast: 40s"),
        new("Chain Saw", "GCD", "Delivers an attack with a potency of 600. Battery +20. Recast: 60s")
    ];

    private static readonly ComboAbility[] HyperchargeAbilities =
    [
        new("Barrel Stabilizer", "oGCD", "Generates 50 Heat. Recast: 120s"),
        new("Hypercharge", "oGCD", "Consume 50 Heat. Enables Heat Blast for 8s"),
        new("Heat Blast", "GCD", "Potency 200. Recast: 1.5s. Reduces Gauss Round/Ricochet cooldown by 15s"),
        new("Wildfire", "oGCD", "Marks target. Explodes dealing 240 potency per weaponskill (max 6)")
    ];

    private static readonly ComboAbility[] OGCDAbilities =
    [
        new("Gauss Round", "oGCD", "Delivers an attack with potency of 130. 3 charges"),
        new("Ricochet", "oGCD", "Delivers an attack with potency of 130 to target and nearby enemies. 3 charges")
    ];

    public MachinistWindow(Plugin plugin, string machinistImagePath)
        : base("Machinist Job Interface##MachinistWindow", ImGuiWindowFlags.NoScrollbar)
    {
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(450, 550),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue)
        };

        this.machinistImagePath = machinistImagePath;
        this.plugin = plugin;
    }

    public void Dispose() { }

    public override void Draw()
    {
        // Header with job icon/image
        DrawHeader();

        ImGui.Separator();
        ImGuiHelpers.ScaledDummy(5.0f);

        using (var child = ImRaii.Child("MachinistContent", Vector2.Zero, false))
        {
            if (child.Success)
            {
                // Basic Combo Section
                DrawSection("Basic Combo (1-2-3)", BasicCombo, new Vector4(0.4f, 0.7f, 1.0f, 1.0f));

                ImGuiHelpers.ScaledDummy(10.0f);

                // Burst Abilities Section
                DrawSection("Burst Abilities", BurstAbilities, new Vector4(1.0f, 0.6f, 0.2f, 1.0f));

                ImGuiHelpers.ScaledDummy(10.0f);

                // Hypercharge Window Section
                DrawSection("Hypercharge Window", HyperchargeAbilities, new Vector4(1.0f, 0.3f, 0.3f, 1.0f));

                ImGuiHelpers.ScaledDummy(10.0f);

                // oGCD Weaving Section
                DrawSection("oGCD Weaving", OGCDAbilities, new Vector4(0.6f, 0.9f, 0.6f, 1.0f));

                ImGuiHelpers.ScaledDummy(15.0f);

                // Tips section
                DrawTipsSection();
            }
        }
    }

    private void DrawHeader()
    {
        // Center the header content
        var machinistImage = Plugin.TextureProvider.GetFromFile(machinistImagePath).GetWrapOrDefault();

        // Title
        ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1.0f, 0.75f, 0.0f, 1.0f));
        var title = "MACHINIST";
        var titleSize = ImGui.CalcTextSize(title);
        ImGui.SetCursorPosX((ImGui.GetWindowWidth() - titleSize.X) / 2);
        ImGui.Text(title);
        ImGui.PopStyleColor();

        ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.7f, 0.7f, 0.7f, 1.0f));
        var subtitle = "Ranged Physical DPS";
        var subtitleSize = ImGui.CalcTextSize(subtitle);
        ImGui.SetCursorPosX((ImGui.GetWindowWidth() - subtitleSize.X) / 2);
        ImGui.Text(subtitle);
        ImGui.PopStyleColor();

        ImGuiHelpers.ScaledDummy(5.0f);

        // Display the machinist image centered
        if (machinistImage != null)
        {
            var imageSize = new Vector2(100, 100);
            ImGui.SetCursorPosX((ImGui.GetWindowWidth() - imageSize.X) / 2);
            ImGui.Image(machinistImage.Handle, imageSize);
        }
        else
        {
            ImGui.TextColored(new Vector4(1.0f, 0.5f, 0.5f, 1.0f), "Machinist image not found.");
        }
    }

    private static void DrawSection(string sectionTitle, ComboAbility[] abilities, Vector4 titleColor)
    {
        ImGui.PushStyleColor(ImGuiCol.Text, titleColor);
        ImGui.Text(sectionTitle);
        ImGui.PopStyleColor();

        ImGui.Separator();

        using (ImRaii.PushIndent(10f))
        {
            foreach (var ability in abilities)
            {
                DrawAbility(ability);
            }
        }
    }

    private static void DrawAbility(ComboAbility ability)
    {
        // Ability name with type badge
        var badgeColor = ability.Type switch
        {
            "1" or "2" or "3" => new Vector4(0.3f, 0.6f, 1.0f, 1.0f),
            "GCD" => new Vector4(0.9f, 0.7f, 0.2f, 1.0f),
            "oGCD" => new Vector4(0.5f, 0.9f, 0.5f, 1.0f),
            _ => new Vector4(0.7f, 0.7f, 0.7f, 1.0f)
        };

        ImGui.TextColored(badgeColor, $"[{ability.Type}]");
        ImGui.SameLine();
        ImGui.TextColored(new Vector4(1.0f, 1.0f, 1.0f, 1.0f), ability.Name);

        using (ImRaii.PushIndent(25f))
        {
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.6f, 0.6f, 0.6f, 1.0f));
            ImGui.TextWrapped(ability.Description);
            ImGui.PopStyleColor();
        }

        ImGuiHelpers.ScaledDummy(3.0f);
    }

    private static void DrawTipsSection()
    {
        ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.9f, 0.9f, 0.3f, 1.0f));
        ImGui.Text("Quick Tips");
        ImGui.PopStyleColor();
        ImGui.Separator();

        using (ImRaii.PushIndent(10f))
        {
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.8f, 0.8f, 0.8f, 1.0f));

            ImGui.Bullet();
            ImGui.TextWrapped("Use Reassemble before Drill, Air Anchor, or Chain Saw for guaranteed crits");

            ImGui.Bullet();
            ImGui.TextWrapped("During Hypercharge, weave one oGCD between each Heat Blast");

            ImGui.Bullet();
            ImGui.TextWrapped("Align Wildfire with Hypercharge for maximum damage");

            ImGui.Bullet();
            ImGui.TextWrapped("Keep your GCD rolling - always be casting!");

            ImGui.PopStyleColor();
        }
    }

    private record ComboAbility(string Name, string Type, string Description);
}
