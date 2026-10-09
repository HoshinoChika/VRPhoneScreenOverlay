#nullable disable

namespace VRPhoneScreenOverlay.App.Views;

partial class PlayspaceMotionView
{
    private System.ComponentModel.IContainer components = null;
    private Label title;
    private ModernButton backButton;
    private SurfacePanel generalCard;
    private SurfacePanel motionCard;
    private Label multiplierTitle;
    private MotionValueControl multiplier;
    private ModernButton multiplierOne;
    private ModernButton multiplierFive;
    private ModernButton multiplierTen;
    private Label resetTitle;
    private PlayspaceResetSelectorControl resetMode;
    private Label strengthTitle;
    private MotionValueControl strength;
    private ModernButton strengthOne;
    private ModernButton strengthFive;
    private ModernButton strengthTen;
    private Label gravityTitle;
    private MotionValueControl gravity;
    private ModernButton gravityZero;
    private ModernButton gravityNormal;
    private ModernButton gravityHigh;
    private Label frictionTitle;
    private MotionValueControl friction;
    private ModernButton frictionZero;
    private ModernButton frictionTen;
    private ModernButton frictionFifty;
    private Label status;

    private ContextHelpIcon multiplierTitleHelp;
    private ContextHelpIcon resetTitleHelp;
    private ContextHelpIcon strengthTitleHelp;
    private ContextHelpIcon gravityTitleHelp;
    private ContextHelpIcon frictionTitleHelp;

    protected override void Dispose(bool disposing)
    {
        if (disposing) { components?.Dispose(); }
        base.Dispose(disposing);
    }
    private void InitializeComponent()
    {
        multiplierTitleHelp = new ContextHelpIcon();
        resetTitleHelp = new ContextHelpIcon();
        strengthTitleHelp = new ContextHelpIcon();
        gravityTitleHelp = new ContextHelpIcon();
        frictionTitleHelp = new ContextHelpIcon();

        title = new Label();
        backButton = new ModernButton();
        generalCard = new SurfacePanel();
        motionCard = new SurfacePanel();
        multiplierTitle = new Label();
        multiplier = new MotionValueControl();
        multiplierOne = new ModernButton();
        multiplierFive = new ModernButton();
        multiplierTen = new ModernButton();
        resetTitle = new Label();
        resetMode = new PlayspaceResetSelectorControl();
        strengthTitle = new Label();
        strength = new MotionValueControl();
        strengthOne = new ModernButton();
        strengthFive = new ModernButton();
        strengthTen = new ModernButton();
        gravityTitle = new Label();
        gravity = new MotionValueControl();
        gravityZero = new ModernButton();
        gravityNormal = new ModernButton();
        gravityHigh = new ModernButton();
        frictionTitle = new Label();
        friction = new MotionValueControl();
        frictionZero = new ModernButton();
        frictionTen = new ModernButton();
        frictionFifty = new ModernButton();
        status = new Label();
        SuspendLayout();
        // multiplierTitleHelp
        multiplierTitleHelp.Name = "multiplierTitleHelp";
        multiplierTitleHelp.Location = new Point(104, 22);
        multiplierTitleHelp.Size = new Size(22, 22);
        multiplierTitleHelp.HelpText = UiHelpContent.Multiplier;
        multiplierTitleHelp.TargetLabel = multiplierTitle;
        multiplierTitleHelp.AccessibleName = "拖拽倍率说明";
        // resetTitleHelp
        resetTitleHelp.Name = "resetTitleHelp";
        resetTitleHelp.Location = new Point(104, 76);
        resetTitleHelp.Size = new Size(22, 22);
        resetTitleHelp.HelpText = UiHelpContent.Reset;
        resetTitleHelp.TargetLabel = resetTitle;
        resetTitleHelp.AccessibleName = "重置方式说明";
        // strengthTitleHelp
        strengthTitleHelp.Name = "strengthTitleHelp";
        strengthTitleHelp.Location = new Point(104, 22);
        strengthTitleHelp.Size = new Size(22, 22);
        strengthTitleHelp.HelpText = UiHelpContent.Strength;
        strengthTitleHelp.TargetLabel = strengthTitle;
        strengthTitleHelp.AccessibleName = "惯性强度说明";
        // gravityTitleHelp
        gravityTitleHelp.Name = "gravityTitleHelp";
        gravityTitleHelp.Location = new Point(104, 76);
        gravityTitleHelp.Size = new Size(22, 22);
        gravityTitleHelp.HelpText = UiHelpContent.Gravity;
        gravityTitleHelp.TargetLabel = gravityTitle;
        gravityTitleHelp.AccessibleName = "重力说明";
        // frictionTitleHelp
        frictionTitleHelp.Name = "frictionTitleHelp";
        frictionTitleHelp.Location = new Point(104, 130);
        frictionTitleHelp.Size = new Size(22, 22);
        frictionTitleHelp.HelpText = UiHelpContent.Friction;
        frictionTitleHelp.TargetLabel = frictionTitle;
        frictionTitleHelp.AccessibleName = "阻力说明";

        // title
        title.Name = "title";
        title.Location = new Point(18, 14);
        title.Size = new Size(380, 32);
        title.Text = "空间拖拽设置";
        title.ForeColor = UiPalette.TextPrimary;
        title.BackColor = Color.Transparent;
        title.Font = new Font("Microsoft YaHei UI", 9.5F);
        title.Font = new Font("Microsoft YaHei UI", 11F, FontStyle.Bold);
        // backButton
        backButton.Name = "backButton";
        backButton.Location = new Point(480, 12);
        backButton.Size = new Size(96, 32);
        backButton.Text = "返回主页";
        // generalCard
        generalCard.Name = "generalCard";
        generalCard.Location = new Point(18, 62);
        generalCard.Size = new Size(558, 110);
        // motionCard
        motionCard.Name = "motionCard";
        motionCard.Location = new Point(18, 186);
        motionCard.Size = new Size(558, 178);
        generalCard.SurfaceColor = UiPalette.SurfaceRaised;
        generalCard.BorderColor = UiPalette.BorderSoft;
        generalCard.CornerRadius = 12;
        motionCard.SurfaceColor = UiPalette.SurfaceRaised;
        motionCard.BorderColor = UiPalette.BorderSoft;
        motionCard.CornerRadius = 12;
        // multiplierTitle
        multiplierTitle.Name = "multiplierTitle";
        multiplierTitle.Location = new Point(16, 20);
        multiplierTitle.Size = new Size(82, 26);
        multiplierTitle.Text = "拖拽倍率";
        multiplierTitle.ForeColor = UiPalette.TextPrimary;
        multiplierTitle.BackColor = Color.Transparent;
        multiplierTitle.Font = new Font("Microsoft YaHei UI", 9.5F);
        // multiplier
        multiplier.Name = "multiplier";
        multiplier.Location = new Point(132, 14);
        multiplier.Size = new Size(180, 36);
        multiplier.Minimum = 1;
        multiplier.Maximum = 40;
        multiplier.DecimalPlaces = 0;
        multiplier.Increment = 1;
        multiplier.Value = 1;
        // multiplierOne
        multiplierOne.Name = "multiplierOne";
        multiplierOne.Location = new Point(326, 14);
        multiplierOne.Size = new Size(64, 36);
        multiplierOne.Text = "1×";
        // multiplierFive
        multiplierFive.Name = "multiplierFive";
        multiplierFive.Location = new Point(398, 14);
        multiplierFive.Size = new Size(64, 36);
        multiplierFive.Text = "5×";
        // multiplierTen
        multiplierTen.Name = "multiplierTen";
        multiplierTen.Location = new Point(470, 14);
        multiplierTen.Size = new Size(64, 36);
        multiplierTen.Text = "10×";
        // resetTitle
        resetTitle.Name = "resetTitle";
        resetTitle.Location = new Point(16, 74);
        resetTitle.Size = new Size(82, 26);
        resetTitle.Text = "重置方式";
        resetTitle.ForeColor = UiPalette.TextPrimary;
        resetTitle.BackColor = Color.Transparent;
        resetTitle.Font = new Font("Microsoft YaHei UI", 9.5F);
        // resetMode
        resetMode.Name = "resetMode";
        resetMode.BackColor = UiPalette.SurfaceRaised;
        resetMode.Location = new Point(132, 66);
        resetMode.Size = new Size(402, 34);
        // strengthTitle
        strengthTitle.Name = "strengthTitle";
        strengthTitle.Location = new Point(16, 20);
        strengthTitle.Size = new Size(82, 26);
        strengthTitle.Text = "惯性强度";
        strengthTitle.ForeColor = UiPalette.TextPrimary;
        strengthTitle.BackColor = Color.Transparent;
        strengthTitle.Font = new Font("Microsoft YaHei UI", 9.5F);
        // strength
        strength.Name = "strength";
        strength.Location = new Point(132, 14);
        strength.Size = new Size(180, 36);
        strength.Maximum = 20;
        strength.Increment = 0.1m;
        strength.Value = 1m;
        // strengthOne
        strengthOne.Name = "strengthOne";
        strengthOne.Location = new Point(326, 14);
        strengthOne.Size = new Size(64, 36);
        strengthOne.Text = "1×";
        // strengthFive
        strengthFive.Name = "strengthFive";
        strengthFive.Location = new Point(398, 14);
        strengthFive.Size = new Size(64, 36);
        strengthFive.Text = "5×";
        // strengthTen
        strengthTen.Name = "strengthTen";
        strengthTen.Location = new Point(470, 14);
        strengthTen.Size = new Size(64, 36);
        strengthTen.Text = "10×";
        // gravityTitle
        gravityTitle.Name = "gravityTitle";
        gravityTitle.Location = new Point(16, 74);
        gravityTitle.Size = new Size(82, 26);
        gravityTitle.Text = "重力";
        gravityTitle.ForeColor = UiPalette.TextPrimary;
        gravityTitle.BackColor = Color.Transparent;
        gravityTitle.Font = new Font("Microsoft YaHei UI", 9.5F);
        // gravity
        gravity.Name = "gravity";
        gravity.Location = new Point(132, 68);
        gravity.Size = new Size(180, 36);
        gravity.Maximum = 30;
        gravity.Increment = 0.1m;
        gravity.Value = 9.8m;
        // gravityZero
        gravityZero.Name = "gravityZero";
        gravityZero.Location = new Point(326, 68);
        gravityZero.Size = new Size(64, 36);
        gravityZero.Text = "0";
        // gravityNormal
        gravityNormal.Name = "gravityNormal";
        gravityNormal.Location = new Point(398, 68);
        gravityNormal.Size = new Size(64, 36);
        gravityNormal.Text = "9.8";
        // gravityHigh
        gravityHigh.Name = "gravityHigh";
        gravityHigh.Location = new Point(470, 68);
        gravityHigh.Size = new Size(64, 36);
        gravityHigh.Text = "20";
        // frictionTitle
        frictionTitle.Name = "frictionTitle";
        frictionTitle.Location = new Point(16, 128);
        frictionTitle.Size = new Size(82, 26);
        frictionTitle.Text = "阻力";
        frictionTitle.ForeColor = UiPalette.TextPrimary;
        frictionTitle.BackColor = Color.Transparent;
        frictionTitle.Font = new Font("Microsoft YaHei UI", 9.5F);
        // friction
        friction.Name = "friction";
        friction.Location = new Point(132, 122);
        friction.Size = new Size(180, 36);
        friction.Maximum = 999;
        friction.Increment = 1m;
        friction.Value = 0m;
        // frictionZero
        frictionZero.Name = "frictionZero";
        frictionZero.Location = new Point(326, 122);
        frictionZero.Size = new Size(64, 36);
        frictionZero.Text = "0%";
        // frictionTen
        frictionTen.Name = "frictionTen";
        frictionTen.Location = new Point(398, 122);
        frictionTen.Size = new Size(64, 36);
        frictionTen.Text = "10%";
        // frictionFifty
        frictionFifty.Name = "frictionFifty";
        frictionFifty.Location = new Point(470, 122);
        frictionFifty.Size = new Size(64, 36);
        frictionFifty.Text = "50%";
        // status
        status.Name = "status";
        status.Location = new Point(18, 378);
        status.Size = new Size(558, 30);
        status.Text = "";
        status.ForeColor = UiPalette.TextPrimary;
        status.BackColor = Color.Transparent;
        status.Font = new Font("Microsoft YaHei UI", 9.5F);
        status.ForeColor = UiPalette.TextSecondary;
        multiplierTitle.TabIndex = 0;
        generalCard.Controls.Add(multiplierTitleHelp);
        generalCard.Controls.Add(resetTitleHelp);
        generalCard.Controls.Add(multiplierTitle);
        multiplier.TabIndex = 1;
        generalCard.Controls.Add(multiplier);
        multiplierOne.TabIndex = 2;
        generalCard.Controls.Add(multiplierOne);
        multiplierFive.TabIndex = 3;
        generalCard.Controls.Add(multiplierFive);
        multiplierTen.TabIndex = 4;
        generalCard.Controls.Add(multiplierTen);
        resetTitle.TabIndex = 5;
        generalCard.Controls.Add(resetTitle);
        resetMode.TabIndex = 6;
        generalCard.Controls.Add(resetMode);
        strengthTitle.TabIndex = 0;
        motionCard.Controls.Add(strengthTitleHelp);
        motionCard.Controls.Add(gravityTitleHelp);
        motionCard.Controls.Add(frictionTitleHelp);
        motionCard.Controls.Add(strengthTitle);
        strength.TabIndex = 1;
        motionCard.Controls.Add(strength);
        strengthOne.TabIndex = 2;
        motionCard.Controls.Add(strengthOne);
        strengthFive.TabIndex = 3;
        motionCard.Controls.Add(strengthFive);
        strengthTen.TabIndex = 4;
        motionCard.Controls.Add(strengthTen);
        gravityTitle.TabIndex = 5;
        motionCard.Controls.Add(gravityTitle);
        gravity.TabIndex = 6;
        motionCard.Controls.Add(gravity);
        gravityZero.TabIndex = 7;
        motionCard.Controls.Add(gravityZero);
        gravityNormal.TabIndex = 8;
        motionCard.Controls.Add(gravityNormal);
        gravityHigh.TabIndex = 9;
        motionCard.Controls.Add(gravityHigh);
        frictionTitle.TabIndex = 10;
        motionCard.Controls.Add(frictionTitle);
        friction.TabIndex = 11;
        motionCard.Controls.Add(friction);
        frictionZero.TabIndex = 12;
        motionCard.Controls.Add(frictionZero);
        frictionTen.TabIndex = 13;
        motionCard.Controls.Add(frictionTen);
        frictionFifty.TabIndex = 14;
        motionCard.Controls.Add(frictionFifty);
        Controls.Add(title);
        Controls.Add(backButton);
        Controls.Add(generalCard);
        Controls.Add(motionCard);
        Controls.Add(status);
        AutoScaleMode = AutoScaleMode.None;
        BackColor = UiPalette.Surface;
        Font = new Font("Microsoft YaHei UI", 9.5F);
        Name = "PlayspaceMotionView";
        Size = new Size(596, 424);
        ResumeLayout(false);
    }
}
