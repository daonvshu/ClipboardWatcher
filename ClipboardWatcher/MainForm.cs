using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Runtime.Serialization.Json;
using System.Runtime.Serialization;
using System.IO;
using System.Xml;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json;

namespace ClipboardWatcher {

    public delegate void ClipboardUpdate();

    public partial class MainForm : Form {

        public const int WM_CLIPBOARDUPDATE = 0x031D;

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool AddClipboardFormatListener(IntPtr hWnd);
        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool RemoveClipboardFormatListener(IntPtr hWnd);

        private IntPtr hWnd;
        private int lastTickCount;

        private ClipboardUpdate clipboardUpdateEvent;

        private string lastUdpIp = "";
        private volatile bool lastUdpIpChanged = false;

        private volatile bool serviceEnable = false;

        private string prepareSendText = "";
        private volatile bool clipFromRec = false;

        public MainForm() {
            InitializeComponent();

            MainContextRunner.AttachMainContext(SynchronizationContext.Current);

            hWnd = Handle;
            AddClipboardFormatListener(hWnd);

            clipboardUpdateEvent += () => cn_ClipboardUpdate();

            scanLocalIp();

            var thread = new Thread(new ThreadStart(connectUdpGroup));
            thread.Start();
        }

        ~MainForm() {
            RemoveClipboardFormatListener(hWnd);
        }

        private void scanLocalIp() {
            var networkInterfaces = NetworkInterface.GetAllNetworkInterfaces();
            foreach (var netInterface in networkInterfaces) {
                var properties = netInterface.GetIPProperties();
                var collection = properties.UnicastAddresses;
                foreach (var infor in collection) {
                    if (infor.Address.AddressFamily == AddressFamily.InterNetwork) {
                        var ip = infor.Address.ToString();
                        if (ip.StartsWith("192")) {
                            interfaceBox.Items.Add(ip);
                        }
                    }
                }
            }
            interfaceBox.SelectedIndex = 0;
        }

        protected override void WndProc(ref Message m) {
            if (m.Msg == WM_CLIPBOARDUPDATE) {
                //列表类解析，会阻止时间，用此法不行，再用lastText方法
                if (Environment.TickCount - lastTickCount >= 300) {
                    clipboardUpdateEvent?.Invoke();
                }
                lastTickCount = Environment.TickCount;
                m.Result = IntPtr.Zero;
            }
            base.WndProc(ref m);
        }

        private void startWatchBtn_Click(object sender, EventArgs e) {
            serviceEnable = !serviceEnable;
            if (serviceEnable) {
                startWatchBtn.Text = "关闭服务";
            }
            else {
                startWatchBtn.Text = "开启服务";
            }
        }

        private void cn_ClipboardUpdate() {
            Console.WriteLine("clip update!");
            if (clipFromRec) {
                return;
            }
            if (serviceEnable && Clipboard.ContainsText()) {
                Console.WriteLine("clip has text!!!!");
                prepareSendText = Clipboard.GetText();
                var thread = new Thread(new ThreadStart(sendText));
                thread.Start();
            }
        }

        private void sendText() {
            UdpClient client = new UdpClient(new IPEndPoint(IPAddress.Parse(lastUdpIp), 0));
            var ip = IPAddress.Parse("224.0.0.250");
            var multicast = new IPEndPoint(ip, 19472);
            client.JoinMulticastGroup(ip);
            var shareStr = new ShareData();
            shareStr.id = shareId.Text;
            shareStr.text = prepareSendText;
            prepareSendText = "";
            var json = JsonConvert.SerializeObject(shareStr);
            byte[] buf = Encoding.UTF8.GetBytes(json);
            client.Send(buf, buf.Length, multicast);
            client.Close();
            Console.WriteLine("send text -> " + shareStr.text);
        }

        private volatile bool udpRunning = true;

        private void connectUdpGroup() {
            UdpClient client = null;
            var ip = IPAddress.Parse("224.0.0.250");
            var multicast = new IPEndPoint(ip, 0);
            while (udpRunning) {
                try {
                    if (lastUdpIpChanged) {
                        client?.DropMulticastGroup(ip);
                        client?.Close();
                        client = new UdpClient(new IPEndPoint(IPAddress.Parse(lastUdpIp), 19472));
                        client.JoinMulticastGroup(ip);
                        client.Client.ReceiveTimeout = 5000;
                        client.AllowNatTraversal(false);
                        lastUdpIpChanged = false;
                    }
                    byte[] buf = client.Receive(ref multicast);
                    string msg = Encoding.UTF8.GetString(buf);
                    if (serviceEnable) {
                        findString(msg);
                    }
                } catch (SocketException) { }
            }
            client?.DropMulticastGroup(ip);
            client?.Close();
        }

        private void findString(string str) {
            try {
                var obj = JsonConvert.DeserializeObject<ShareData>(str);
                if (obj.id == shareId.Text) {
                    clipFromRec = true;
                    MainContextRunner.Post(() => {
                        Clipboard.SetDataObject(obj.text);
                    });
                    Thread.Sleep(200);
                    clipFromRec = false;
                }
            } catch (JsonReaderException) { }
        }

        private void MainForm_FormClosed(object sender, FormClosedEventArgs e) {
            udpRunning = false;
        }

        private void interfaceBox_SelectedIndexChanged(object sender, EventArgs e) {
            lastUdpIp = (string)interfaceBox.SelectedItem;
            lastUdpIpChanged = true;
        }

        private void ClickQuit(object sender, EventArgs e) {
            Application.Exit();
            Icon.Dispose();
        }

        private void MainForm_Deactivate(object sender, EventArgs e) {
            this.Hide();
        }

        private void MainForm_Load(object sender, EventArgs e) {
            BeginInvoke(new Action(() => {
                this.Hide();
                this.Opacity = 1;
            }));
        }

        private void MainForm_Paint(object sender, PaintEventArgs e) {

            ControlPaint.DrawBorder(e.Graphics, ClientRectangle,
                Color.Blue, 1, ButtonBorderStyle.Solid,
                Color.Blue, 1, ButtonBorderStyle.Solid,
                Color.Blue, 1, ButtonBorderStyle.Solid,
                Color.Blue, 1, ButtonBorderStyle.Solid);
        }

        private void notifyIcon1_MouseClick(object sender, MouseEventArgs e) {
            if (e.Button == MouseButtons.Left) {
                this.Size = new Size(474, 47);
                Size workArea = SystemInformation.WorkingArea.Size;
                Point mousePoint = MousePosition;
                Point leftTop = new Point();
                if (mousePoint.X + Size.Width > workArea.Width) {
                    leftTop.X = mousePoint.X - Size.Width;
                }
                else {
                    leftTop.X = mousePoint.X;
                }
                leftTop.Y = workArea.Height - Size.Height - 10;
                this.Location = leftTop;
                this.Show();
                this.Activate();
            }
        }
    }

    public class ShareData {
        public string id { get; set; }
        public string text { get; set; }
    }
}
