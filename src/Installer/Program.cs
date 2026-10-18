using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace HFFVRoidInstaller
{
    internal static class Program
    {
        [STAThread]
        private static int Main(string[] args)
        {
            // Command line (also used when re-launching elevated):
            //   HFF-VRoid-Installer.exe --install "<game folder>"   |   --uninstall "<game folder>"
            if (args.Length >= 1 && (args[0] == "--install" || args[0] == "--uninstall"))
            {
                string dir = args.Length >= 2 ? args[1] : GameInstall.FindGameFolder();
                bool quiet = Array.IndexOf(args, "--quiet") >= 0;
                try
                {
                    if (!GameInstall.IsGameFolder(dir)) throw new Exception("Human: Fall Flat was not found in: " + dir);
                    string log = args[0] == "--install" ? GameInstall.Install(dir) : GameInstall.Uninstall(dir, true);
                    if (!quiet) MessageBox.Show(log, "HFF VRoid", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return 0;
                }
                catch (Exception e)
                {
                    if (!quiet) MessageBox.Show(e.Message, "HFF VRoid", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return 1;
                }
            }
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
            return 0;
        }
    }

    internal sealed class MainForm : Form
    {
        private readonly TextBox pathBox = new TextBox();
        private readonly Label status = new Label();
        private readonly Button installButton = new Button();
        private readonly Button uninstallButton = new Button();
        private readonly Button openFolderButton = new Button();

        public MainForm()
        {
            Text = "HFF VRoid - VRM avatars for Human: Fall Flat";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(560, 330);
            Font = new Font("Segoe UI", 9.5f);

            var title = new Label
            {
                Text = "HFF VRoid installer",
                Font = new Font("Segoe UI", 15f, FontStyle.Bold),
                AutoSize = true,
                Location = new Point(16, 12)
            };
            var intro = new Label
            {
                Text = "Adds an IMPORT VRM button to Customize Character so you can play as any .vrm avatar " +
                       "(VRoid Studio, VRoid Hub, Booth...). Imported avatars appear in LOAD PRESET.\r\n" +
                       "This installs BepInEx (the mod loader) if needed, then the mod.",
                Location = new Point(18, 50),
                Size = new Size(524, 60)
            };
            var pathLabel = new Label { Text = "Human: Fall Flat folder:", AutoSize = true, Location = new Point(18, 118) };
            pathBox.Location = new Point(18, 140);
            pathBox.Size = new Size(430, 26);
            var browse = new Button { Text = "Browse...", Location = new Point(456, 138), Size = new Size(86, 28) };
            browse.Click += OnBrowse;

            installButton.Text = "Install";
            installButton.Font = new Font("Segoe UI", 10.5f, FontStyle.Bold);
            installButton.Location = new Point(18, 182);
            installButton.Size = new Size(170, 40);
            installButton.Click += (s, e) => Run(true);

            uninstallButton.Text = "Uninstall";
            uninstallButton.Location = new Point(196, 182);
            uninstallButton.Size = new Size(110, 40);
            uninstallButton.Click += (s, e) => Run(false);

            openFolderButton.Text = "Open VRM drop folder";
            openFolderButton.Location = new Point(314, 182);
            openFolderButton.Size = new Size(228, 40);
            openFolderButton.Click += OnOpenFolder;

            status.Location = new Point(18, 234);
            status.Size = new Size(524, 86);

            Controls.AddRange(new Control[] { title, intro, pathLabel, pathBox, browse, installButton, uninstallButton, openFolderButton, status });

            string found = GameInstall.FindGameFolder();
            pathBox.Text = found ?? "";
            pathBox.TextChanged += (s, e) => RefreshState();
            RefreshState();
            if (found == null) status.Text = "Could not find Human: Fall Flat automatically. Click Browse and pick the game folder " +
                                             "(Steam > right-click the game > Manage > Browse local files).";
        }

        private void RefreshState()
        {
            string dir = pathBox.Text.Trim();
            bool ok = GameInstall.IsGameFolder(dir);
            installButton.Enabled = ok;
            uninstallButton.Enabled = ok && GameInstall.IsInstalled(dir);
            openFolderButton.Enabled = ok;
            if (!ok) status.Text = dir.Length == 0 ? "" : "That folder doesn't contain Human.exe.";
            else status.Text = GameInstall.IsInstalled(dir) ? "HFF VRoid is installed. Click Install again to update it." : "Ready to install.";
            installButton.Text = ok && GameInstall.IsInstalled(dir) ? "Update / Reinstall" : "Install";
        }

        private void OnBrowse(object sender, EventArgs e)
        {
            using (var dlg = new FolderBrowserDialog())
            {
                dlg.Description = "Select the Human Fall Flat folder (the one that contains Human.exe)";
                if (Directory.Exists(pathBox.Text)) dlg.SelectedPath = pathBox.Text;
                if (dlg.ShowDialog(this) == DialogResult.OK) pathBox.Text = dlg.SelectedPath;
            }
        }

        private void OnOpenFolder(object sender, EventArgs e)
        {
            string dir = Path.Combine(pathBox.Text.Trim(), "BepInEx", "VRoid", "Import");
            try
            {
                Directory.CreateDirectory(dir);
                Process.Start("explorer.exe", "\"" + dir + "\"");
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void Run(bool install)
        {
            string dir = pathBox.Text.Trim();
            if (GameInstall.IsGameRunning())
            {
                MessageBox.Show(this, "Please close Human: Fall Flat first.", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            try
            {
                Cursor = Cursors.WaitCursor;
                string log = install ? GameInstall.Install(dir) : GameInstall.Uninstall(dir, true);
                status.Text = log + (install ? "Done! Start the game from Steam and open Customize Character." : "");
            }
            catch (UnauthorizedAccessException)
            {
                // Game folder is protected (e.g. inside Program Files): retry as administrator.
                try
                {
                    var psi = new ProcessStartInfo(Application.ExecutablePath,
                        (install ? "--install" : "--uninstall") + " \"" + dir + "\"") { Verb = "runas", UseShellExecute = true };
                    var p = Process.Start(psi);
                    if (p != null) p.WaitForExit();
                    status.Text = p != null && p.ExitCode == 0 ? "Done (with administrator rights)." : "Administrator step failed or was cancelled.";
                }
                catch (Exception ex)
                {
                    status.Text = "Could not write to the game folder: " + ex.Message;
                }
            }
            catch (Exception ex)
            {
                status.Text = "Error: " + ex.Message;
            }
            finally
            {
                Cursor = Cursors.Default;
                bool ok = GameInstall.IsGameFolder(dir);
                uninstallButton.Enabled = ok && GameInstall.IsInstalled(dir);
                installButton.Text = ok && GameInstall.IsInstalled(dir) ? "Update / Reinstall" : "Install";
            }
        }
    }
}
