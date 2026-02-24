namespace EmojiForge.WinForms;

partial class SettingsForm
{
    private System.ComponentModel.IContainer components = null!;
    private TabControl tabControl = null!;
    private TextBox pythonPathBox = null!;
    private TextBox backendPathBox = null!;
    private TextBox outputDirBox = null!;
    private TextBox fontPathBox = null!;
    private TextBox modelPathBox = null!;
    private Button browseModelPathButton = null!;
    private Button downloadModelButton = null!;
    private Label modelDownloadStatusLabel = null!;
    private ProgressBar modelDownloadProgressBar = null!;
    private FlowLayoutPanel downloadStagePanel = null!;
    private Label stagePrepareLabel = null!;
    private Label stageBootstrapLabel = null!;
    private Label stageDownloadLabel = null!;
    private Label stageVerifyLabel = null!;
    private Label stageCompleteLabel = null!;
    private RichTextBox modelDownloadLogBox = null!;
    private TextBox hfTokenBox = null!;
    private ComboBox deviceCombo = null!;
    private CheckBox cpuOffloadCheck = null!;
    private NumericUpDown strengthUpDown = null!;
    private NumericUpDown stepsUpDown = null!;
    private NumericUpDown guidanceUpDown = null!;
    private NumericUpDown seedUpDown = null!;
    private ComboBox outputSizeCombo = null!;
    private CheckBox randomSeedCheck = null!;
    private CheckBox removeBackgroundCheck = null!;
    private NumericUpDown removeBgStrengthUpDown = null!;
    private Button saveButton = null!;
    private Button cancelButton = null!;
    private Button resetButton = null!;

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            components?.Dispose();
        }

        base.Dispose(disposing);
    }

    private void InitializeComponent()
    {
        components = new System.ComponentModel.Container();
        tabControl = new TabControl { Dock = DockStyle.Top, Height = 420 };

        var pathsTab = new TabPage("Paths");
        var modelTab = new TabPage("Model");
        var defaultsTab = new TabPage("Generation Defaults");

        pythonPathBox = new TextBox();
        backendPathBox = new TextBox();
        outputDirBox = new TextBox();
        fontPathBox = new TextBox();

        modelPathBox = new TextBox();
        browseModelPathButton = new Button();
        downloadModelButton = new Button();
        modelDownloadStatusLabel = new Label();
        modelDownloadProgressBar = new ProgressBar();
        downloadStagePanel = new FlowLayoutPanel();
        stagePrepareLabel = new Label();
        stageBootstrapLabel = new Label();
        stageDownloadLabel = new Label();
        stageVerifyLabel = new Label();
        stageCompleteLabel = new Label();
        modelDownloadLogBox = new RichTextBox();
        hfTokenBox = new TextBox();
        deviceCombo = new ComboBox();
        cpuOffloadCheck = new CheckBox();

        strengthUpDown = new NumericUpDown();
        stepsUpDown = new NumericUpDown();
        guidanceUpDown = new NumericUpDown();
        seedUpDown = new NumericUpDown();
        outputSizeCombo = new ComboBox();
        randomSeedCheck = new CheckBox();
        removeBackgroundCheck = new CheckBox();
        removeBgStrengthUpDown = new NumericUpDown();

        var pathsLayout = CreateTwoColumnLayout();
        AddLabeled(pathsLayout, "Python Executable", pythonPathBox, 0);
        AddLabeled(pathsLayout, "Backend Script", backendPathBox, 1);
        AddLabeled(pathsLayout, "Default Output Dir", outputDirBox, 2);
        AddLabeled(pathsLayout, "Emoji Font Path", fontPathBox, 3);
        pathsTab.Controls.Add(pathsLayout);

        var modelLayout = CreateTwoColumnLayout();
        var modelPathPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            WrapContents = false
        };
        modelPathBox.Width = 360;
        browseModelPathButton.Text = "Browse...";
        browseModelPathButton.AutoSize = true;
        browseModelPathButton.Click += BrowseModelPathButton_Click;
        modelPathPanel.Controls.Add(modelPathBox);
        modelPathPanel.Controls.Add(browseModelPathButton);
        AddLabeled(modelLayout, "Model ID / Path", modelPathPanel, 0);

        downloadModelButton.Text = "Download Model";
        downloadModelButton.AutoSize = true;
        downloadModelButton.Click += DownloadModelButton_Click;
        modelLayout.Controls.Add(downloadModelButton, 1, 1);

        modelDownloadStatusLabel.Text = "Model status: idle";
        modelDownloadStatusLabel.AutoSize = true;
        modelLayout.Controls.Add(modelDownloadStatusLabel, 1, 2);

        modelDownloadProgressBar.Minimum = 0;
        modelDownloadProgressBar.Maximum = 100;
        modelDownloadProgressBar.Value = 0;
        modelDownloadProgressBar.Width = 520;
        modelLayout.Controls.Add(modelDownloadProgressBar, 1, 3);

        downloadStagePanel.Dock = DockStyle.Top;
        downloadStagePanel.AutoSize = true;
        downloadStagePanel.WrapContents = false;
        downloadStagePanel.Controls.Add(stagePrepareLabel);
        downloadStagePanel.Controls.Add(stageBootstrapLabel);
        downloadStagePanel.Controls.Add(stageDownloadLabel);
        downloadStagePanel.Controls.Add(stageVerifyLabel);
        downloadStagePanel.Controls.Add(stageCompleteLabel);
        ConfigureStageLabel(stagePrepareLabel, "Prepare");
        ConfigureStageLabel(stageBootstrapLabel, "Bootstrap");
        ConfigureStageLabel(stageDownloadLabel, "Download");
        ConfigureStageLabel(stageVerifyLabel, "Verify");
        ConfigureStageLabel(stageCompleteLabel, "Complete");
        modelLayout.Controls.Add(downloadStagePanel, 1, 4);

        modelDownloadLogBox.ReadOnly = true;
        modelDownloadLogBox.Height = 110;
        modelDownloadLogBox.Width = 520;
        modelDownloadLogBox.BackColor = Color.WhiteSmoke;
        modelLayout.Controls.Add(modelDownloadLogBox, 1, 5);

        hfTokenBox.UseSystemPasswordChar = true;
        AddLabeled(modelLayout, "HuggingFace Token", hfTokenBox, 6);
        deviceCombo.DropDownStyle = ComboBoxStyle.DropDownList;
        deviceCombo.Items.AddRange(["cuda", "cpu"]);
        AddLabeled(modelLayout, "Device", deviceCombo, 7);
        cpuOffloadCheck.Text = "Enable CPU Offload";
        modelLayout.Controls.Add(cpuOffloadCheck, 1, 8);
        modelTab.Controls.Add(modelLayout);

        var defaultsLayout = CreateTwoColumnLayout();
        strengthUpDown.DecimalPlaces = 2;
        strengthUpDown.Increment = 0.05m;
        strengthUpDown.Minimum = 0.1m;
        strengthUpDown.Maximum = 1m;
        strengthUpDown.Value = 0.1m;
        AddLabeled(defaultsLayout, "Strength", strengthUpDown, 0);

        stepsUpDown.Minimum = 1;
        stepsUpDown.Maximum = 50;
        stepsUpDown.Value = 30;
        AddLabeled(defaultsLayout, "Steps", stepsUpDown, 1);

        guidanceUpDown.DecimalPlaces = 1;
        guidanceUpDown.Increment = 0.5m;
        guidanceUpDown.Minimum = 1;
        guidanceUpDown.Maximum = 30;
        guidanceUpDown.Value = 1.0m;
        AddLabeled(defaultsLayout, "CFG Scale", guidanceUpDown, 2);

        seedUpDown.Minimum = 0;
        seedUpDown.Maximum = (decimal)long.MaxValue;
        AddLabeled(defaultsLayout, "Seed", seedUpDown, 3);

        outputSizeCombo.DropDownStyle = ComboBoxStyle.DropDownList;
        outputSizeCombo.Items.AddRange(["256", "512", "1024"]);
        AddLabeled(defaultsLayout, "Output Size", outputSizeCombo, 4);

        randomSeedCheck.Text = "Random Seed";
        randomSeedCheck.Checked = true;
        defaultsLayout.Controls.Add(randomSeedCheck, 1, 5);

        removeBackgroundCheck.Text = "Remove Background";
        removeBackgroundCheck.Checked = true;
        defaultsLayout.Controls.Add(removeBackgroundCheck, 1, 6);

        removeBgStrengthUpDown.DecimalPlaces = 2;
        removeBgStrengthUpDown.Increment = 0.05m;
        removeBgStrengthUpDown.Minimum = 0m;
        removeBgStrengthUpDown.Maximum = 1m;
        removeBgStrengthUpDown.Value = 1.0m;
        AddLabeled(defaultsLayout, "Background Removal Strength", removeBgStrengthUpDown, 7);
        defaultsTab.Controls.Add(defaultsLayout);

        tabControl.TabPages.Add(pathsTab);
        tabControl.TabPages.Add(modelTab);
        tabControl.TabPages.Add(defaultsTab);

        saveButton = new Button { Text = "Save", Width = 100 };
        cancelButton = new Button { Text = "Cancel", Width = 100 };
        resetButton = new Button { Text = "Reset to Defaults", Width = 130 };
        saveButton.Click += SaveButton_Click;
        cancelButton.Click += CancelButton_Click;
        resetButton.Click += ResetButton_Click;

        var buttonPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 50,
            FlowDirection = FlowDirection.RightToLeft
        };
        buttonPanel.Controls.Add(saveButton);
        buttonPanel.Controls.Add(cancelButton);
        buttonPanel.Controls.Add(resetButton);

        AutoScaleMode = AutoScaleMode.Font;
        ClientSize = new Size(760, 500);
        Text = "EmojiForge Settings";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        Controls.Add(buttonPanel);
        Controls.Add(tabControl);
    }

    private static TableLayoutPanel CreateTwoColumnLayout()
    {
        return new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            Padding = new Padding(10),
            RowCount = 10,
            AutoSize = true
        };
    }

    private static void AddLabeled(TableLayoutPanel layout, string labelText, Control input, int row)
    {
        var label = new Label { Text = labelText, AutoSize = true, Padding = new Padding(0, 8, 0, 0) };
        input.Dock = DockStyle.Top;
        layout.Controls.Add(label, 0, row);
        layout.Controls.Add(input, 1, row);
    }

    private static void ConfigureStageLabel(Label label, string text)
    {
        label.Text = text;
        label.AutoSize = true;
        label.BorderStyle = BorderStyle.FixedSingle;
        label.Padding = new Padding(8, 4, 8, 4);
        label.Margin = new Padding(0, 0, 6, 0);
        label.BackColor = Color.Gainsboro;
    }
}
