using System;
using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Collections.Generic;
using System.Linq;

namespace AionPingLite
{
    public partial class MainWindow : Window
    {
        private long _dotnetEpochOffsetMs = 62135596800000L;
        private CancellationTokenSource _cts = new CancellationTokenSource();
        private Brush _colorGreen = new SolidColorBrush(Color.FromRgb(65, 217, 138));
        private Brush _colorYellow = new SolidColorBrush(Color.FromRgb(242, 193, 90));
        private Brush _colorOrange = new SolidColorBrush(Color.FromRgb(255, 154, 61));
        private Brush _colorRed = new SolidColorBrush(Color.FromRgb(255, 70, 70));
        private Brush _colorGray = new SolidColorBrush(Colors.Gray);

        private DateTime _lastPingTime = DateTime.MinValue;
        
        private List<Tuple<DateTime, int>> _pingHistory = new List<Tuple<DateTime, int>>();
        private ChartWindow _chartWindow;
        private bool _isDragging = false;
        private Point _mouseDownPos;

        public MainWindow()
        {
            InitializeComponent();
            WifiIcon.ToolTip = "Offline (Double click to exit)";
            StartCapture();
            
            var timer = new System.Windows.Threading.DispatcherTimer();
            timer.Interval = TimeSpan.FromSeconds(2);
            timer.Tick += (s, e) => {
                if ((DateTime.Now - _lastPingTime).TotalSeconds > 15)
                {
                    WifiIcon.Fill = _colorGray;
                    PingText.Foreground = _colorGray;
                    PingText.Text = "--- ms";
                }
            };
            timer.Start();
        }

        private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _mouseDownPos = e.GetPosition(this);
            _isDragging = false;
            
            if (e.ClickCount == 2) // Double click to completely exit
            {
                Application.Current.Shutdown();
            }
        }
        
        private void Window_MouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                var pos = e.GetPosition(this);
                if (Math.Abs(pos.X - _mouseDownPos.X) > SystemParameters.MinimumHorizontalDragDistance ||
                    Math.Abs(pos.Y - _mouseDownPos.Y) > SystemParameters.MinimumVerticalDragDistance)
                {
                    _isDragging = true;
                    this.DragMove();
                }
            }
        }

        private void Window_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (!_isDragging)
            {
                if (_chartWindow == null || !_chartWindow.IsLoaded)
                {
                    _chartWindow = new ChartWindow();
                    _chartWindow.UpdateData(_pingHistory);
                    _chartWindow.Show();
                }
                else
                {
                    _chartWindow.UpdateData(_pingHistory);
                    _chartWindow.Activate();
                }
            }
            _isDragging = false;
        }

        private Color GetPingColor(int ping)
        {
            if (ping <= 40) return Color.FromRgb(65, 217, 138); // Green
            if (ping <= 80) return Color.FromRgb(242, 193, 90); // Yellow
            if (ping <= 120) return Color.FromRgb(255, 154, 61); // Orange
            return Color.FromRgb(255, 70, 70); // Red
        }

        private void UpdatePingDisplay(int pingMs)
        {
            Dispatcher.Invoke(() =>
            {
                _lastPingTime = DateTime.Now;
                _pingHistory.Add(new Tuple<DateTime, int>(DateTime.Now, pingMs));
                _pingHistory.RemoveAll(x => (DateTime.Now - x.Item1).TotalMinutes > 10);
                
                var colorBrush = new SolidColorBrush(GetPingColor(pingMs));
                WifiIcon.Fill = colorBrush;
                PingText.Foreground = colorBrush;
                PingText.Text = $"{pingMs} ms";
                WifiIcon.ToolTip = $"{pingMs} ms (Double click to exit)";
                
                if (_chartWindow != null && _chartWindow.IsLoaded)
                {
                    _chartWindow.UpdateData(_pingHistory);
                }
            });
        }

        private void StartCapture()
        {
            var host = Dns.GetHostEntry(Dns.GetHostName());
            foreach (var ip in host.AddressList)
            {
                if (ip.AddressFamily == AddressFamily.InterNetwork)
                {
                    string localIp = ip.ToString();
                    Task.Run(() => CaptureLoop(localIp, _cts.Token));
                }
            }
        }

        private void CaptureLoop(string localIp, CancellationToken token)
        {
            try
            {
                Socket socket = new Socket(AddressFamily.InterNetwork, SocketType.Raw, ProtocolType.IP);
                socket.Bind(new IPEndPoint(IPAddress.Parse(localIp), 0));
                socket.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.HeaderIncluded, true);

                byte[] byTrue = new byte[4] { 1, 0, 0, 0 };
                byte[] byOut = new byte[4];
                socket.IOControl(IOControlCode.ReceiveAll, byTrue, byOut);

                byte[] buffer = new byte[65536];

                while (!token.IsCancellationRequested)
                {
                    if (socket.Available > 0)
                    {
                        int received = socket.Receive(buffer, 0, buffer.Length, SocketFlags.None);
                        if (received > 20) // Min IP header is 20 bytes
                        {
                            int ipHeaderLength = (buffer[0] & 0x0F) * 4;
                            if (buffer[9] == 6) // Protocol 6 is TCP
                            {
                                int tcpHeaderOffset = ipHeaderLength;
                                if (received > tcpHeaderOffset + 20)
                                {
                                    int tcpHeaderLength = (buffer[tcpHeaderOffset + 12] >> 4) * 4;
                                    int payloadOffset = tcpHeaderOffset + tcpHeaderLength;
                                    int payloadLength = received - payloadOffset;

                                    if (payloadLength >= 12)
                                    {
                                        TryPingRs(buffer, payloadOffset, payloadLength);
                                    }
                                }
                            }
                        }
                    }
                    else
                    {
                        Thread.Sleep(1);
                    }
                }
            }
            catch (Exception)
            {
                // Handled gracefully, likely interface not supporting raw sockets
            }
        }

        private void TryPingRs(byte[] data, int offset, int length)
        {
            int i = offset;
            int maxI = offset + length - 12;
            while (i <= maxI)
            {
                if (data[i] == 0x03 && data[i + 1] == 0x36 && data[i + 2] == 0x00 && data[i + 3] == 0x00)
                {
                    long clientSentRaw = BitConverter.ToInt64(data, i + 4);
                    long clientSentUnixMs = clientSentRaw - _dotnetEpochOffsetMs;
                    long arrivalMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

                    int rttMs = (int)(arrivalMs - clientSentUnixMs);
                    
                    if (rttMs >= 0 && rttMs < 9999)
                    {
                        UpdatePingDisplay(rttMs);
                    }
                    i += 12;
                }
                else
                {
                    i++;
                }
            }
        }
    }
}