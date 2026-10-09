using System.ComponentModel;

namespace VRPhoneScreenOverlay.App.Views;

public partial class PlayspaceMotionView : UserControl
{
    public PlayspaceMotionView()
    {
        InitializeComponent();
        multiplierOne.Click += (_, _) => multiplier.Value = 1m;
        multiplierFive.Click += (_, _) => multiplier.Value = 5m;
        multiplierTen.Click += (_, _) => multiplier.Value = 10m;
        strengthOne.Click += (_, _) => strength.Value = 1m;
        strengthFive.Click += (_, _) => strength.Value = 5m;
        strengthTen.Click += (_, _) => strength.Value = 10m;
        gravityZero.Click += (_, _) => gravity.Value = 0m;
        gravityNormal.Click += (_, _) => gravity.Value = 9.8m;
        gravityHigh.Click += (_, _) => gravity.Value = 20m;
        frictionZero.Click += (_, _) => friction.Value = 0m;
        frictionTen.Click += (_, _) => friction.Value = 10m;
        frictionFifty.Click += (_, _) => friction.Value = 50m;
        foreach (MotionValueControl input in new[] { multiplier, strength, gravity, friction })
        {
            input.ValueChanged += (_, _) => ParametersChanged?.Invoke(this, EventArgs.Empty);
        }
        resetMode.SelectedValueChanged += (_, _) => ParametersChanged?.Invoke(this, EventArgs.Empty);
    }
    internal event EventHandler? ParametersChanged;
    internal ModernButton BackButton => backButton;
    internal MotionValueControl Multiplier => multiplier;
    internal MotionValueControl Strength => strength;
    internal MotionValueControl Gravity => gravity;
    internal MotionValueControl Friction => friction;
    internal PlayspaceResetSelectorControl ResetMode => resetMode;
    internal Label Status => status;
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal bool ResetAllOffsets
    {
        get => resetMode.TryGetSelectedValue(out bool value) && value;
        set => resetMode.SelectValue(value);
    }
}
