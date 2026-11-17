namespace VRPhoneScreenOverlay.App;

partial class StartupFailureForm
{
    private System.ComponentModel.IContainer components = null;

    protected override void Dispose(bool disposing)
    {
        if (disposing) { components?.Dispose(); }
        base.Dispose(disposing);
    }

    private void InitializeComponent()
    {
        details = new Label();
        closeButton = new Button();
        SuspendLayout();
        details.Location = new Point(24, 24);
        details.Name = "details";
        details.Size = new Size(472, 160);
        details.TabIndex = 0;
        closeButton.DialogResult = DialogResult.Cancel;
        closeButton.Location = new Point(396, 200);
        closeButton.Name = "closeButton";
        closeButton.Size = new Size(100, 32);
        closeButton.TabIndex = 1;
        closeButton.Text = "关闭";
        closeButton.UseVisualStyleBackColor = true;
        closeButton.Click += CloseButton_Click;
        AutoScaleDimensions = new SizeF(7F, 17F);
        AutoScaleMode = AutoScaleMode.Font;
        CancelButton = closeButton;
        ClientSize = new Size(520, 256);
        Controls.Add(closeButton);
        Controls.Add(details);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        Name = "StartupFailureForm";
        StartPosition = FormStartPosition.CenterScreen;
        Text = "VRPhoneScreen Overlay — 无法继续运行";
        ResumeLayout(false);
    }

    private Label details;
    private Button closeButton;
}

