namespace EmojiForge.WinForms;

partial class MainForm
{
    private System.ComponentModel.IContainer components = null!;
    private ToolStrip topToolStrip = null!;
    private ToolStripButton settingsButton = null!;
    private ToolStripButton openOutputButton = null!;
    private ToolStripButton aboutButton = null!;
    private SplitContainer splitContainer = null!;
    private GroupBox generationGroup = null!;
    private TextBox promptTextBox = null!;
    private Label promptHintsLabel = null!;
    private RadioButton allEmojisRadio = null!;
    private RadioButton singleEmojiRadio = null!;
    private TextBox emojiInputTextBox = null!;
    private TrackBar strengthTrackBar = null!;
    private Label strengthValueLabel = null!;
    private NumericUpDown stepsUpDown = null!;
    private NumericUpDown cfgScaleUpDown = null!;
    private NumericUpDown seedUpDown = null!;
    private CheckBox randomSeedCheckBox = null!;
    private CheckBox sameSeedCheckBox = null!;
    private NumericUpDown batchSizeUpDown = null!;
    private ComboBox outputSizeComboBox = null!;
    private NumericUpDown removeBgStrengthUpDown = null!;
    private Button generateButton = null!;
    private Button cancelButton = null!;
    private Label statusLabel = null!;
    private ProgressBar progressBar = null!;
    private FlowLayoutPanel previewPanel = null!;
    private RichTextBox logTextBox = null!;
    private StatusStrip statusStrip = null!;
    private ToolStripStatusLabel gpuStatusLabel = null!;
    private ToolStripStatusLabel outputStatusLabel = null!;
    private ToolStripStatusLabel etaStatusLabel = null!;
    private ToolTip generationToolTip = null!;
    private ContextMenuStrip emojiInputContextMenu = null!;
    private ToolStripMenuItem openEmojiPickerMenuItem = null!;
    private ToolStripMenuItem pasteEmojiMenuItem = null!;

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
        topToolStrip = new ToolStrip();
        settingsButton = new ToolStripButton();
        openOutputButton = new ToolStripButton();
        aboutButton = new ToolStripButton();
        splitContainer = new SplitContainer();
        generationGroup = new GroupBox();
        promptTextBox = new TextBox();
        promptHintsLabel = new Label();
        allEmojisRadio = new RadioButton();
        singleEmojiRadio = new RadioButton();
        emojiInputTextBox = new TextBox();
        strengthTrackBar = new TrackBar();
        strengthValueLabel = new Label();
        stepsUpDown = new NumericUpDown();
        cfgScaleUpDown = new NumericUpDown();
        seedUpDown = new NumericUpDown();
        randomSeedCheckBox = new CheckBox();
        sameSeedCheckBox = new CheckBox();
        batchSizeUpDown = new NumericUpDown();
        outputSizeComboBox = new ComboBox();
        removeBgStrengthUpDown = new NumericUpDown();
        generateButton = new Button();
        cancelButton = new Button();
        statusLabel = new Label();
        progressBar = new ProgressBar();
        previewPanel = new FlowLayoutPanel();
        logTextBox = new RichTextBox();
        statusStrip = new StatusStrip();
        gpuStatusLabel = new ToolStripStatusLabel();
        outputStatusLabel = new ToolStripStatusLabel();
        etaStatusLabel = new ToolStripStatusLabel();
        generationToolTip = new ToolTip(components);
        emojiInputContextMenu = new ContextMenuStrip(components);
        openEmojiPickerMenuItem = new ToolStripMenuItem();
        pasteEmojiMenuItem = new ToolStripMenuItem();

        SuspendLayout();

        topToolStrip.Items.AddRange([settingsButton, openOutputButton, new ToolStripSeparator(), aboutButton]);
        topToolStrip.Dock = DockStyle.Top;

        settingsButton.Text = "Settings";
        settingsButton.Click += SettingsButton_Click;

        openOutputButton.Text = "Open Output Folder";
        openOutputButton.Click += OpenOutputButton_Click;

        aboutButton.Text = "About";
        aboutButton.Click += AboutButton_Click;

        splitContainer.Dock = DockStyle.Fill;
        splitContainer.SplitterDistance = 320;
        splitContainer.Panel1MinSize = 300;

        generationGroup.Text = "Generation";
        generationGroup.Dock = DockStyle.Fill;

        var leftLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 23,
            Padding = new Padding(10),
            AutoScroll = true
        };
        leftLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        leftLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        var promptLabel = new Label { Text = "Prompt", Dock = DockStyle.Top };
        leftLayout.Controls.Add(promptLabel, 0, 0);
        leftLayout.SetColumnSpan(promptLabel, 2);
        promptTextBox.Multiline = true;
        promptTextBox.Height = 90;
        promptTextBox.Dock = DockStyle.Top;
        generationToolTip.SetToolTip(promptTextBox, "Describe the style transformation to apply to each emoji.");
        leftLayout.Controls.Add(promptTextBox, 0, 1);
        leftLayout.SetColumnSpan(promptTextBox, 2);

        promptHintsLabel.Text = "Try: pixel art style, watercolor painting, neon glow";
        promptHintsLabel.Dock = DockStyle.Top;
        promptHintsLabel.AutoSize = true;
        leftLayout.Controls.Add(promptHintsLabel, 0, 2);
        leftLayout.SetColumnSpan(promptHintsLabel, 2);

        allEmojisRadio.Text = "All Emojis";
        allEmojisRadio.Checked = true;
        allEmojisRadio.CheckedChanged += ModeRadio_CheckedChanged;
        generationToolTip.SetToolTip(allEmojisRadio, "Generate a full set for all supported emojis.");
        leftLayout.Controls.Add(allEmojisRadio, 0, 3);
        leftLayout.SetColumnSpan(allEmojisRadio, 2);

        singleEmojiRadio.Text = "Selected Emojis";
        singleEmojiRadio.CheckedChanged += ModeRadio_CheckedChanged;
        generationToolTip.SetToolTip(singleEmojiRadio, "Generate only the checked emojis.");
        leftLayout.Controls.Add(singleEmojiRadio, 0, 4);
        leftLayout.SetColumnSpan(singleEmojiRadio, 2);

        emojiInputTextBox.Enabled = false;
        emojiInputTextBox.Dock = DockStyle.Top;
        emojiInputTextBox.PlaceholderText = "Type, paste, or open emoji picker here...";
        emojiInputTextBox.TextChanged += EmojiInputTextBox_TextChanged;
        emojiInputTextBox.KeyDown += EmojiInputTextBox_KeyDown;
        emojiInputTextBox.Enter += EmojiInputTextBox_Enter;
        emojiInputTextBox.Leave += EmojiInputTextBox_Leave;
        emojiInputContextMenu.Items.AddRange([openEmojiPickerMenuItem, pasteEmojiMenuItem]);
        openEmojiPickerMenuItem.Text = "Open Emoji Picker";
        openEmojiPickerMenuItem.Click += OpenEmojiPickerMenuItem_Click;
        pasteEmojiMenuItem.Text = "Paste Emoji from Clipboard";
        pasteEmojiMenuItem.Click += PasteEmojiMenuItem_Click;
        emojiInputTextBox.ContextMenuStrip = emojiInputContextMenu;
        generationToolTip.SetToolTip(emojiInputTextBox, "Input emojis here; matching items will be checked in the list.");
        leftLayout.Controls.Add(emojiInputTextBox, 0, 5);
        leftLayout.SetColumnSpan(emojiInputTextBox, 2);

        var strengthLabel = new Label { Text = "Strength", Dock = DockStyle.Top };
        leftLayout.Controls.Add(strengthLabel, 0, 6);
        leftLayout.SetColumnSpan(strengthLabel, 2);
        strengthTrackBar.Minimum = 1;
        strengthTrackBar.Maximum = 10;
        strengthTrackBar.Value = 1;
        strengthTrackBar.TickStyle = TickStyle.None;
        strengthTrackBar.Dock = DockStyle.Top;
        strengthTrackBar.ValueChanged += StrengthTrackBar_ValueChanged;
        generationToolTip.SetToolTip(strengthTrackBar, "How strongly the output departs from the source emoji (0.1 to 1.0).");
        leftLayout.Controls.Add(strengthTrackBar, 0, 7);
        leftLayout.SetColumnSpan(strengthTrackBar, 2);

        strengthValueLabel.Text = "0.1";
        strengthValueLabel.AutoSize = true;
        leftLayout.Controls.Add(strengthValueLabel, 0, 8);

        logTextBox.Dock = DockStyle.Fill;
        logTextBox.ReadOnly = true;
        leftLayout.Controls.Add(logTextBox, 1, 8);
        leftLayout.SetRowSpan(logTextBox, 7);

        leftLayout.Controls.Add(new Label { Text = "Steps", AutoSize = true }, 0, 9);
        stepsUpDown.Minimum = 1;
        stepsUpDown.Maximum = 50;
        stepsUpDown.Value = 30;
        generationToolTip.SetToolTip(stepsUpDown, "Number of denoising steps. Higher values are slower but can improve detail.");
        leftLayout.Controls.Add(stepsUpDown, 0, 10);

        leftLayout.Controls.Add(new Label { Text = "CFG Scale", AutoSize = true }, 0, 11);
        cfgScaleUpDown.DecimalPlaces = 1;
        cfgScaleUpDown.Increment = 0.5m;
        cfgScaleUpDown.Minimum = 1;
        cfgScaleUpDown.Maximum = 30;
        cfgScaleUpDown.Value = 1.0m;
        generationToolTip.SetToolTip(cfgScaleUpDown, "Classifier-free guidance scale. Higher values follow the prompt more strongly.");
        leftLayout.Controls.Add(cfgScaleUpDown, 0, 12);

        leftLayout.Controls.Add(new Label { Text = "Seed", AutoSize = true }, 0, 13);
        var seedPanel = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true };
        seedUpDown.Minimum = 0;
        seedUpDown.Maximum = (decimal)long.MaxValue;
        seedUpDown.Value = 42;
        generationToolTip.SetToolTip(seedUpDown, "Seed for reproducibility. Same seed + settings usually gives similar results.");
        randomSeedCheckBox.Text = "Random";
        randomSeedCheckBox.Checked = true;
        generationToolTip.SetToolTip(randomSeedCheckBox, "Use a new random 64-bit seed each generation.");
        sameSeedCheckBox.Text = "Same seed for all";
        sameSeedCheckBox.AutoSize = true;
        generationToolTip.SetToolTip(sameSeedCheckBox, "When checked, every image in a batch uses the same seed instead of incrementing per image.");
        seedPanel.Controls.Add(seedUpDown);
        seedPanel.Controls.Add(randomSeedCheckBox);
        seedPanel.Controls.Add(sameSeedCheckBox);
        leftLayout.Controls.Add(seedPanel, 0, 14);

        leftLayout.Controls.Add(new Label { Text = "Batch Size", AutoSize = true }, 0, 15);
        batchSizeUpDown.Minimum = 1;
        batchSizeUpDown.Maximum = 100;
        batchSizeUpDown.Value = 1;
        generationToolTip.SetToolTip(batchSizeUpDown, "How many full passes to run for the current job.");
        leftLayout.Controls.Add(batchSizeUpDown, 0, 16);

        outputSizeComboBox.DropDownStyle = ComboBoxStyle.DropDownList;
        outputSizeComboBox.Items.AddRange(["256", "512", "1024"]);
        outputSizeComboBox.SelectedItem = "512";
        generationToolTip.SetToolTip(outputSizeComboBox, "Output image size in pixels.");
        leftLayout.Controls.Add(new Label { Text = "Output Size", AutoSize = true }, 0, 17);
        leftLayout.Controls.Add(outputSizeComboBox, 0, 18);

        leftLayout.Controls.Add(new Label { Text = "Background Removal", AutoSize = true }, 0, 19);
        removeBgStrengthUpDown.DecimalPlaces = 2;
        removeBgStrengthUpDown.Increment = 0.05m;
        removeBgStrengthUpDown.Minimum = 0;
        removeBgStrengthUpDown.Maximum = 1;
        removeBgStrengthUpDown.Value = 1.0m;
        generationToolTip.SetToolTip(removeBgStrengthUpDown, "How strongly background removal is applied (0.00 keeps more fill, 1.00 is strongest).");
        leftLayout.Controls.Add(removeBgStrengthUpDown, 0, 20);

        generateButton.Text = "Generate";
        generateButton.Dock = DockStyle.Top;
        generateButton.Height = 38;
        generateButton.Enabled = false;
        generateButton.Click += GenerateButton_Click;
        generationToolTip.SetToolTip(generateButton, "Start generation with current prompt and settings.");

        cancelButton.Text = "Cancel";
        cancelButton.Dock = DockStyle.Top;
        cancelButton.Height = 38;
        cancelButton.Enabled = false;
        cancelButton.Click += CancelButton_Click;

        leftLayout.Controls.Add(generateButton, 0, 21);
        leftLayout.SetColumnSpan(generateButton, 2);
        leftLayout.Controls.Add(cancelButton, 0, 22);
        leftLayout.SetColumnSpan(cancelButton, 2);

        generationGroup.Controls.Add(leftLayout);
        splitContainer.Panel1.Controls.Add(generationGroup);

        var rightLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3 };
        rightLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        rightLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        rightLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        progressBar.Dock = DockStyle.Fill;
        rightLayout.Controls.Add(progressBar, 0, 0);

        statusLabel.Text = "Ready";
        statusLabel.Dock = DockStyle.Fill;
        rightLayout.Controls.Add(statusLabel, 0, 1);

        previewPanel.Dock = DockStyle.Fill;
        previewPanel.AutoScroll = true;
        rightLayout.Controls.Add(previewPanel, 0, 2);

        splitContainer.Panel2.Controls.Add(rightLayout);

        statusStrip.Items.AddRange([gpuStatusLabel, outputStatusLabel, etaStatusLabel]);
        statusStrip.Dock = DockStyle.Bottom;

        gpuStatusLabel.Text = "GPU: Unknown";
        outputStatusLabel.Text = "Output: -";
        etaStatusLabel.Text = "Idle";

        AutoScaleMode = AutoScaleMode.Font;
        ClientSize = new Size(1200, 760);
        MinimumSize = new Size(900, 650);
        Text = "EmojiForge - AI Emoji Generator";
        Controls.Add(splitContainer);
        Controls.Add(statusStrip);
        Controls.Add(topToolStrip);

        ResumeLayout(false);
        PerformLayout();
    }
}
