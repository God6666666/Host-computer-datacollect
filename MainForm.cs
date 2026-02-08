using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.IO.Ports;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace _2._6test
{
    public partial class MainForm : Form
    {
        private readonly SerialPort _sp = new SerialPort();
        private readonly Timer _sampleTimer = new Timer();

        // 接收缓冲（拼包用）
        private readonly StringBuilder _rxBuffer = new StringBuilder();

        // 解析出来的“最新一条有效数据”（供 Timer 采样入库）
        private SamplePoint _latestPoint = null;

        // 数据存储
        private readonly List<SamplePoint> _data = new List<SamplePoint>();

        // 采集开关
        private bool _collecting = false;

        public MainForm()
        {
            InitializeComponent();
            InitUi();
            InitSerial();
            InitTimer();
        }

        private void InitUi()
        {
            cmbPort.Items.AddRange(SerialPort.GetPortNames());
            if (cmbPort.Items.Count > 0) cmbPort.SelectedIndex = 0;

            cmbBaud.Items.AddRange(new object[] { "9600", "19200", "38400", "57600", "115200" });
            cmbBaud.SelectedItem = "115200";

            // DataGridView 绑定
            dgv.AutoGenerateColumns = true;
            dgv.DataSource = new BindingSource { DataSource = _data };

            UpdateStatus("未连接");
        }

        private void InitSerial()
        {
            _sp.DataReceived += Sp_DataReceived;
            _sp.Encoding = Encoding.ASCII;          // 视设备而定，常见 ASCII/UTF8
            _sp.NewLine = "\n";                     // 行结束符（按你设备调整）
            _sp.ReadTimeout = 500;
        }

        private void InitTimer()
        {
            _sampleTimer.Interval = 1000;           // 采样周期：1秒一个数据点（可改）
            _sampleTimer.Tick += SampleTimer_Tick;
        }

        // ---------- 串口打开/关闭 ----------
        private void btnOpen_Click(object sender, EventArgs e)
        {
            try
            {
                if (!_sp.IsOpen)
                {
                    _sp.PortName = cmbPort.Text;
                    _sp.BaudRate = int.Parse(cmbBaud.Text);
                    _sp.Parity = Parity.None;
                    _sp.DataBits = 8;
                    _sp.StopBits = StopBits.One;

                    _sp.Open();
                    UpdateStatus($"已连接：{_sp.PortName} @{_sp.BaudRate}");
                    btnOpen.Text = "关闭串口";
                }
                else
                {
                    StopCollectInternal();
                    _sp.Close();
                    UpdateStatus("已断开");
                    btnOpen.Text = "打开串口";
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("串口操作失败：" + ex.Message);
            }
        }

        // ---------- 开始/停止采集 ----------
        private void btnStart_Click(object sender, EventArgs e)
        {
            if (!_sp.IsOpen)
            {
                MessageBox.Show("请先打开串口");
                return;
            }

            _collecting = true;
            _sampleTimer.Start();
            UpdateStatus("采集中...");
        }

        private void btnStop_Click(object sender, EventArgs e)
        {
            StopCollectInternal();
            UpdateStatus("已停止采集");
        }

        private void StopCollectInternal()
        {
            _collecting = false;
            _sampleTimer.Stop();
        }

        // ---------- 串口接收：只做“接收+拼包+解析最新值” ----------
        private void Sp_DataReceived(object sender, SerialDataReceivedEventArgs e)
        {
            try
            {
                string chunk = _sp.ReadExisting(); // 读到的是“片段”，可能半包/多包
                if (string.IsNullOrEmpty(chunk)) return;

                lock (_rxBuffer)
                {
                    _rxBuffer.Append(chunk);

                    // 按行切（最常见：设备一行一帧）
                    while (true)
                    {
                        string all = _rxBuffer.ToString();
                        int idx = all.IndexOf('\n');
                        if (idx < 0) break;

                        string line = all.Substring(0, idx).Trim('\r', '\n', ' ');
                        _rxBuffer.Remove(0, idx + 1);

                        if (!string.IsNullOrWhiteSpace(line))
                        {
                            // 可选：显示原始数据（跨线程要用 BeginInvoke）
                            BeginInvoke(new Action(() =>
                            {
                                txtRaw.AppendText(line + Environment.NewLine);
                            }));

                            // 解析（把 line -> 温度/湿度）
                            if (TryParseLine(line, out double t, out double h))
                            {
                                // 更新“最新值”（Timer Tick 时入库）
                                _latestPoint = new SamplePoint
                                {
                                    Time = DateTime.Now,
                                    Temperature = t,
                                    Humidity = h
                                };
                            }
                        }
                    }
                }
            }
            catch
            {
                // DataReceived 里尽量别弹窗；实际项目可记录日志
            }
        }

        // 示例协议：T=25.3,H=60.2
        // 你换协议就改这里（这是“解析层”）
        private bool TryParseLine(string line, out double temperature, out double humidity)
        {
            temperature = 0;
            humidity = 0;

            // 极简解析：用逗号分割
            // line: "T=25.3,H=60.2"
            try
            {
                string[] parts = line.Split(',');
                if (parts.Length < 2) return false;

                string tPart = parts[0].Trim(); // "T=25.3"
                string hPart = parts[1].Trim(); // "H=60.2"

                if (!tPart.StartsWith("T=") || !hPart.StartsWith("H=")) return false;

                string tStr = tPart.Substring(2);
                string hStr = hPart.Substring(2);

                if (!double.TryParse(tStr, out temperature)) return false;
                if (!double.TryParse(hStr, out humidity)) return false;

                return true;
            }
            catch
            {
                return false;
            }
        }

        // ---------- Timer：按周期“形成数据点、入库、刷新UI” ----------
        private void SampleTimer_Tick(object sender, EventArgs e)
        {
            if (!_collecting) return;

            // Timer Tick：取“最新一条有效数据”，作为当前采样点
            var point = _latestPoint;
            if (point == null) return; // 还没收到有效数据

            // 入库
            _data.Add(point);

            // 刷新 DataGridView
            // 这里用 BindingSource 的 ResetBindings 更平滑
            if (dgv.DataSource is BindingSource bs)
            {
                bs.ResetBindings(false);
            }
            else
            {
                dgv.Refresh();
            }

            lblStatus.Text = $"采集中... 数据点数：{_data.Count}";
        }

        private void UpdateStatus(string text)
        {
            lblStatus.Text = text;
        }

        private void dgv_CellContentClick(object sender, DataGridViewCellEventArgs e)
        {

        }
    }
}
