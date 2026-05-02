using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace LngTool;

public class MainForm : Form
{
    private const string DefaultEncoding = "utf-8";

    private ComboBox _gameCombo = null!;
    private Button _extractBtn = null!;
    private Button _rebuildBtn = null!;
    private Label _statusLabel = null!;

    public MainForm()
    {
        Text = "Obscure 1 / 2 .lng tool — Extract / Rebuild";
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(460, 170);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        Font = new Font("Segoe UI", 9F);
        BuildUi();
    }

    private void BuildUi()
    {
        var lblGame = new Label { Text = "Game:", Left = 16, Top = 18, AutoSize = true };
        _gameCombo = new ComboBox
        {
            Left = 110,
            Top = 14,
            Width = 320,
            DropDownStyle = ComboBoxStyle.DropDownList
        };
        _gameCombo.Items.AddRange(new object[]
        {
            "Obscure 1 (PC / PS2 — big-endian)",
            "Obscure 2 (all platforms — little-endian)"
        });
        _gameCombo.SelectedIndex = 0;

        _extractBtn = new Button
        {
            Text = "Extract  .lng  →  .txt",
            Left = 16,
            Top = 50,
            Width = 414,
            Height = 36
        };
        _extractBtn.Click += OnExtractClick;

        _rebuildBtn = new Button
        {
            Text = "Rebuild  .txt  →  .lng",
            Left = 16,
            Top = 92,
            Width = 414,
            Height = 36
        };
        _rebuildBtn.Click += OnRebuildClick;

        _statusLabel = new Label
        {
            Left = 16,
            Top = 138,
            Width = 414,
            Height = 22,
            ForeColor = Color.DimGray,
            Text = "Ready."
        };

        Controls.AddRange(new Control[]
        {
            lblGame, _gameCombo,
            _extractBtn, _rebuildBtn,
            _statusLabel
        });
    }

    private GameType ResolveGame() => _gameCombo.SelectedIndex switch
    {
        0 => GameType.Obscure1,
        1 => GameType.Obscure2,
        _ => GameType.Obscure1
    };

    private void OnExtractClick(object? sender, EventArgs e)
    {
        using var ofd = new OpenFileDialog
        {
            Title = "Select .lng file to extract",
            Filter = "LNG files (*.lng)|*.lng|All files (*.*)|*.*"
        };
        if (ofd.ShowDialog(this) != DialogResult.OK) return;

        string lngPath = ofd.FileName;
        string txtPath = Path.Combine(
            Path.GetDirectoryName(lngPath) ?? "",
            Path.GetFileNameWithoutExtension(lngPath) + ".txt");

        try
        {
            GameType game = ResolveGame();
            int count = game switch
            {
                GameType.Obscure1 => ObscureLng.ExtractOb1ToTxt(lngPath, txtPath),
                GameType.Obscure2 => ObscureLng.ExtractOb2ToTxt(lngPath, txtPath, DefaultEncoding),
                _ => 0
            };

            string label = game == GameType.Obscure1 ? "OB1" : "OB2";
            SetStatus($"{label} — extracted {count} entries → {Path.GetFileName(txtPath)}");

            MessageBox.Show(this,
                $"Extracted to:\n{txtPath}",
                "Success",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            SetStatus("Failed: " + ex.Message);
            MessageBox.Show(this, ex.Message, "Extraction failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void OnRebuildClick(object? sender, EventArgs e)
    {
        using var ofd = new OpenFileDialog
        {
            Title = "Select .txt file to rebuild",
            Filter = "TXT files (*.txt)|*.txt|All files (*.*)|*.*"
        };
        if (ofd.ShowDialog(this) != DialogResult.OK) return;
        string txtPath = ofd.FileName;

        GameType game = ResolveGame();

        using var sfd = new SaveFileDialog
        {
            Title = "Save rebuilt .lng",
            Filter = "LNG files (*.lng)|*.lng",
            DefaultExt = ".lng",
            FileName = Path.GetFileNameWithoutExtension(txtPath) + ".lng"
        };
        if (sfd.ShowDialog(this) != DialogResult.OK) return;
        string outLng = sfd.FileName;

        try
        {
            switch (game)
            {
                case GameType.Obscure1:
                    ObscureLng.RebuildOb1FromTxt(txtPath, outLng);
                    SetStatus($"OB1 — rebuilt → {Path.GetFileName(outLng)}");
                    break;
                case GameType.Obscure2:
                    ObscureLng.RebuildOb2FromTxt(txtPath, outLng, DefaultEncoding, addNullTerminator: false);
                    SetStatus($"OB2 — rebuilt → {Path.GetFileName(outLng)}");
                    break;
            }

            MessageBox.Show(this,
                $"Rebuilt .lng saved to:\n{outLng}",
                "Success",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            SetStatus("Failed: " + ex.Message);
            MessageBox.Show(this, ex.Message, "Rebuild failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void SetStatus(string text)
    {
        _statusLabel.Text = text;
    }
}
