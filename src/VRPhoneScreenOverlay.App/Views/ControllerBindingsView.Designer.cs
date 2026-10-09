#nullable disable

namespace VRPhoneScreenOverlay.App.Views;

partial class ControllerBindingsView
{
    private System.ComponentModel.IContainer components = null;
    private Label status;
    private BindingGuideSelectorControl controllerSelector;
    private ModernButton openBindingsButton;
    private ModernButton restoreBindingsButton;
    private Label handTitle;
    private ControllerHandSelectorControl handSelector;
    private ModernButton refreshButton;
    private ControllerBindingInstructions instructions;
    private TableLayoutPanel guideLayout;
    private SteamVrBindingNotice bindingNotice;

    private ContextHelpIcon controllerSelectorHelp;

    protected override void Dispose(bool disposing)
    {
        if (disposing) { components?.Dispose(); }
        base.Dispose(disposing);
    }

    private void InitializeComponent()
    {
        controllerSelectorHelp = new ContextHelpIcon();

        status = new Label();
        controllerSelector = new BindingGuideSelectorControl();
        openBindingsButton = new ModernButton();
        restoreBindingsButton = new ModernButton();
        handTitle = new Label();
        handSelector = new ControllerHandSelectorControl();
        refreshButton = new ModernButton();
        instructions = new ControllerBindingInstructions();
        guideLayout = new TableLayoutPanel();
        bindingNotice = new SteamVrBindingNotice();
        guideLayout.SuspendLayout();
        SuspendLayout();
        // controllerSelectorHelp
        controllerSelectorHelp.Name = "controllerSelectorHelp";
        controllerSelectorHelp.Location = new Point(470, 7);
        controllerSelectorHelp.Size = new Size(22, 22);
        controllerSelectorHelp.HelpText = UiHelpContent.Bindings;
        controllerSelectorHelp.TargetLabel = handTitle;
        controllerSelectorHelp.AccessibleName = "手柄与按键说明说明";

        // openBindingsButton
        openBindingsButton.Location = new Point(470, 374);
        openBindingsButton.Size = new Size(112, 38);
        openBindingsButton.Text = "手柄绑定";
        openBindingsButton.Icon = UiIcons.Steam;
        openBindingsButton.Name = "openBindingsButton";
        // restoreBindingsButton
        restoreBindingsButton.Location = new Point(324, 374);
        restoreBindingsButton.Size = new Size(134, 38);
        restoreBindingsButton.Text = "恢复默认绑定";
        restoreBindingsButton.Name = "restoreBindingsButton";
        // handTitle
        handTitle.Location = new Point(12, 380);
        handTitle.Size = new Size(70, 28);
        handTitle.Text = "惯用手";
        handTitle.ForeColor = UiPalette.TextPrimary;
        handTitle.Name = "handTitle";
        // handSelector
        handSelector.Location = new Point(84, 374);
        handSelector.Size = new Size(136, 38);
        handSelector.Name = "handSelector";
        // refreshButton
        refreshButton.Location = new Point(504, 3);
        refreshButton.Size = new Size(78, 30);
        refreshButton.Text = "刷新";
        refreshButton.Name = "refreshButton";
        // controllerSelector
        controllerSelector.Location = new Point(12, 0);
        controllerSelector.Size = new Size(480, 36);
        controllerSelector.Name = "controllerSelector";
        // instructions
        instructions.Dock = DockStyle.Fill;
        instructions.Margin = Padding.Empty;
        instructions.Size = new Size(596, 310);
        instructions.Name = "instructions";
        // bindingNotice
        bindingNotice.Dock = DockStyle.Top;
        bindingNotice.Margin = Padding.Empty;
        bindingNotice.Name = "bindingNotice";
        bindingNotice.Visible = false;
        // guideLayout
        guideLayout.Location = new Point(0, 44);
        guideLayout.Size = new Size(596, 306);
        guideLayout.ColumnCount = 1;
        guideLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        guideLayout.RowCount = 2;
        guideLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        guideLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        guideLayout.Controls.Add(bindingNotice, 0, 0);
        guideLayout.Controls.Add(instructions, 0, 1);
        guideLayout.Name = "guideLayout";
        // status
        status.Location = new Point(12, 352);
        status.Size = new Size(570, 21);
        status.ForeColor = UiPalette.TextSecondary;
        status.Name = "status";
        // ControllerBindingsView
        AutoScaleMode = AutoScaleMode.None;
        BackColor = UiPalette.Window;
        Size = new Size(596, 424);
        Name = "ControllerBindingsView";
        Controls.Add(controllerSelectorHelp);
        Controls.Add(openBindingsButton);
        Controls.Add(restoreBindingsButton);
        Controls.Add(handTitle);
        Controls.Add(handSelector);
        Controls.Add(refreshButton);
        Controls.Add(controllerSelector);
        Controls.Add(guideLayout);
        Controls.Add(status);
        guideLayout.ResumeLayout(false);
        ResumeLayout(false);
        PerformLayout();
    }
}
