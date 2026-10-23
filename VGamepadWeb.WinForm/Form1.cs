using System.ComponentModel;
using System.Diagnostics;
using System.Drawing.Drawing2D;
using VGamepadWeb.Core;

namespace VGamepadWeb.WinForm
{
    public partial class Form1 : Form
    {
        ServerRunner server = new ServerRunner();
        ServerState _CurrentState = ServerState.Stopped;

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public ServerState CurrentState
        {
            get => _CurrentState;
            set
            {
                if (value != _CurrentState)
                {
                    _CurrentState = value;
                    onChangeButtonState();
                }
            }
        }

        public Form1()
        {
            InitializeComponent();

            // 🔥 الربط المركزي الآمن هنا مباشرة بمجرد بناء الـ Form.
            // الكائن server مبني وثابت، والحدث لن يعود بـ NullReferenceException أبداً.
            server.OnControllerIdAssigned += HandlePlayerConnected;
            server.OnPlayerDisconnected += HandlePlayerDisconnected;

            label_ips.Text = "Local IPs: " + string.Join(", ", NetworkHelper.GetAllLocalIPv4Addresses());
            labelVersion.Text = $"Version: {Application.ProductVersion.Split("+")[0]}";

            // Auto-start the server on launch for convenience.
            this.Shown += (s, e) =>
            {
                if (CurrentState == ServerState.Stopped)
                {
                    try { button_Server_Click(this, EventArgs.Empty); } catch { }
                }
            };
        }

        public enum ServerState
        {
            Stopped,
            Starting,
            Running,
            Stopping
        }

        // ==========================================
        // 🔥 دالة إضافة كرت اللاعب عند الاتصال بنجاح
        // ==========================================
        private void HandlePlayerConnected(string connectionId, int controllerId)
        {
            if (flowLayoutPanelPlayers.InvokeRequired)
            {
                flowLayoutPanelPlayers.Invoke(new Action(() => HandlePlayerConnected(connectionId, controllerId)));
                return;
            }

            // سحب نوع الكنترولر بأمان من الـ GamepadManager الذي أصبح متاحاً الآن لأن السيرفر يعمل
            string controllerType = "Unknown";
            if (server.GamepadManager != null)
            {
                controllerType = server.GamepadManager.GetControllerTypeString(connectionId);
            }

            PlayerCardUserControl playerCard = new PlayerCardUserControl();
            playerCard.SetPlayerDetails("Player", controllerId, controllerType);
            playerCard.Name = connectionId; // لحفظ المعرف واستخدامه عند الحذف

            flowLayoutPanelPlayers.Controls.Add(playerCard);
        }

        // ==========================================
        // 🔥 دالة حذف كرت اللاعب فور انقطاع الاتصال
        // ==========================================
        private void HandlePlayerDisconnected(string connectionId)
        {
            if (flowLayoutPanelPlayers.InvokeRequired)
            {
                flowLayoutPanelPlayers.Invoke(new Action(() => HandlePlayerDisconnected(connectionId)));
                return;
            }

            Control cardToRemove = flowLayoutPanelPlayers.Controls[connectionId];

            if (cardToRemove != null)
            {
                flowLayoutPanelPlayers.Controls.Remove(cardToRemove);
                cardToRemove.Dispose();
            }
        }

        // ==========================================
        // ⚙️ زر تشغيل وإيقاف السيرفر (أصبح نظيفاً ومستقراً)
        // ==========================================
        private async void button_Server_Click(object sender, EventArgs e)
        {
            if (CurrentState == ServerState.Stopped)
            {
                try
                {
                    CurrentState = ServerState.Starting;
                    int port = int.TryParse(textBoxPort.Text, out int parsedPort) ? parsedPort : -1;

                    if (port <= 0 || port > 65535)
                    {
                        CurrentState = ServerState.Stopped;
                        MessageBox.Show("Please enter a valid port number between 1 and 65535.");
                        return;
                    }

                    labelIP.Text = $"[ {NetworkHelper.GetPreferredLocalIPv4Address()}:{port} ]";

                    // تشغيل السيرفر وبناء الـ WebApplication داخلياً
                    await server.StartServerAsync(port);

                    CurrentState = ServerState.Running;
                }
                catch (Exception ex)
                {
                    MessageBox.Show(ex.Message);
                    button_Server.Enabled = true;
                    CurrentState = ServerState.Stopped;
                }
            }
            else
            {
                CurrentState = ServerState.Stopping;

                await server.StopServerAsync();

                labelIP.Text = "[ x.x.x.x:x ]";

                // تنظيف الشاشة من أي كروت متبقية فور إيقاف السيرفر
                flowLayoutPanelPlayers.Controls.Clear();

                CurrentState = ServerState.Stopped;
            }
        }

        void onChangeButtonState()
        {
            switch (CurrentState)
            {
                case ServerState.Starting:
                    button_Server.Enabled = false;
                    button_Server.Text = "Starting...";
                    button_Server.ForeColor = Color.Orange;

                    labelState.Text = "Starting ...";
                    labelState.ForeColor = Color.Orange;
                    break;
                case ServerState.Running:
                    button_Server.Enabled = true;
                    button_Server.Text = "⏹️  Stop Server";
                    button_Server.ForeColor = Color.Red;

                    labelState.Text = "RUNNING";
                    labelState.ForeColor = Color.Green;
                    break;
                case ServerState.Stopping:
                    button_Server.Enabled = false;
                    button_Server.Text = "Stopping...";
                    button_Server.ForeColor = Color.Orange;

                    labelState.Text = "Stopping...";
                    labelState.ForeColor = Color.Orange;
                    break;
                case ServerState.Stopped:
                    button_Server.Enabled = true;
                    button_Server.Text = "🔴  Start Server";
                    button_Server.ForeColor = Color.Green;

                    labelState.Text = "STOPPED";
                    labelState.ForeColor = Color.Red;
                    break;
            }

            panelStatus.Invalidate();
        }

        private void panel1_Paint(object sender, PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.HighQuality;

            Rectangle rect = new Rectangle(2, 2, panelStatus.Width - 5, panelStatus.Height - 5);

            Color centerColor = Color.Gray;
            Color surroundColor = Color.DimGray;

            switch (CurrentState)
            {
                case ServerState.Running:
                    centerColor = Color.FromArgb(150, 255, 150);
                    surroundColor = Color.FromArgb(0, 200, 0);
                    break;

                case ServerState.Stopped:
                    centerColor = Color.FromArgb(255, 150, 150);
                    surroundColor = Color.FromArgb(200, 0, 0);
                    break;

                case ServerState.Starting:
                case ServerState.Stopping:
                    centerColor = Color.Orange;
                    surroundColor = Color.FromArgb(220, 180, 0);
                    break;
            }

            using (GraphicsPath path = new GraphicsPath())
            {
                path.AddEllipse(rect);
                using (PathGradientBrush pgb = new PathGradientBrush(path))
                {
                    pgb.CenterColor = centerColor;
                    pgb.SurroundColors = new Color[] { surroundColor };

                    g.FillEllipse(pgb, rect);
                }
            }
        }

        private void textBox1_TextChanged(object sender, EventArgs e)
        {
            server.UpdateServerPassword(textBoxPassword.Text);
        }

        private void linkLabel1_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            try
            {
                // تجهيز إعدادات تشغيل الرابط عبر متصفح النظام الافتراضي
                ProcessStartInfo psi = new ProcessStartInfo
                {
                    FileName = "https://github.com/aboodedc/VGamepadWeb",
                    UseShellExecute = true // ضرورية جداً في دوت نت الحديث لتجنب خطأ الـ Win32Exception
                };

                Process.Start(psi);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"تعذر فتح الرابط تلقائياً: {ex.Message}", "خطأ", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
}