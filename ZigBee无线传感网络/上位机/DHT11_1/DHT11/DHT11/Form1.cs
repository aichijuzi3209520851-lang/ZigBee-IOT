using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.IO.Ports;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;
using MQTTnet;
using MQTTnet.Client;

namespace DHT11
{
    public partial class Form1 : Form
    {
        private SerialPort _serialPort;
        private List<double> _tempHistory = new List<double>();
        private List<double> _humHistory = new List<double>();
        private List<string> _timestampHistory = new List<string>();
        private List<string> _alarmTypeHistory = new List<string>();
        private const int MaxHistoryPoints = 100;
        private StringBuilder _receivedBuffer = new StringBuilder();
        private StringBuilder _logBuffer = new StringBuilder();

        // UI控件
        private ComboBox comPortComboBox;
        private ComboBox baudRateComboBox;
        private ComboBox dataFormatComboBox;
        private Button openButton;
        private Button closeButton;
        private Button refreshButton;
        private Button clearButton;
        private Button exportButton;
        private Button customFormatButton;
        private Label tempLabel;
        private Label humLabel;
        private Panel chartPanel;
        private Label portLabel;
        private Label baudRateLabel;
        private Label dataFormatLabel;
        private Label lblStatus;
        private TextBox logTextBox;
        private Label logLabel;
        private CheckBox autoScrollCheckBox;
        private Label tempUnitLabel;
        private Label humUnitLabel;

        // MQTT相关控件
        private TextBox mqttBrokerTextBox;
        private TextBox mqttTopicTextBox;
        private Button mqttConnectButton;
        private Button mqttDisconnectButton;
        private Label mqttBrokerLabel;
        private Label mqttTopicLabel;
        private Label mqttStatusLabel;
        private CheckBox mqttAutoPublishCheckBox;

        // 图标显示控件
        private PictureBox tempIconPictureBox;
        private PictureBox humIconPictureBox;
        private PictureBox avatarPictureBox;
        private Label avatarLabel;

        // 报警阈值设置控件
        private NumericUpDown tempMinThreshold;
        private NumericUpDown tempMaxThreshold;
        private NumericUpDown humMinThreshold;
        private NumericUpDown humMaxThreshold;
        private Label tempThresholdLabel;
        private Label humThresholdLabel;

        // 报警阈值
        private double _tempMin = 15.0;
        private double _tempMax = 30.0;
        private double _humMin = 30.0;
        private double _humMax = 70.0;

        // 报警状态
        private bool _tempAlarm = false;
        private bool _humAlarm = false;

        // 配色方案
        private Color primaryColor = Color.FromArgb(33, 150, 243);      // 蓝色
        private Color successColor = Color.FromArgb(76, 175, 80);       // 绿色
        private Color dangerColor = Color.FromArgb(244, 67, 54);        // 红色
        private Color warningColor = Color.FromArgb(255, 152, 0);       // 橙色
        private Color darkColor = Color.FromArgb(52, 73, 94);           // 深蓝灰
        private Color lightColor = Color.FromArgb(236, 240, 241);       // 浅灰
        private Color chartBgColor = Color.FromArgb(250, 250, 250);     // 图表背景

        // 数据格式解析配置
        private string _currentDataFormat = "格式1: Temperature:XX Humidity:XX";
        private string _customTempRegex = @"Temperature:\s*(\d+\.?\d*)";
        private string _customHumRegex = @"Humidity:\s*(\d+\.?\d*)";
        private Dictionary<string, DataFormatConfig> _dataFormats;

        // MQTT客户端和配置
        private IMqttClient _mqttClient;
        private string _mqttPublishTopic = "DHT11/data";   // 发布主题：温湿度数据发送到这里
        private bool _mqttAutoPublish = true;               // 是否自动发布数据到MQTT

        public Form1()
        {
            InitializeComponent();  // 必须首先调用，初始化设计器生成的组件
            InitializeDataFormats();
            InitializeSerialPort();
            InitializeChart();
            InitializeCustomComponents();
            InitializeMqttClient();
            LoadAvailablePorts();
        }

        private void InitializeDataFormats()
        {
            _dataFormats = new Dictionary<string, DataFormatConfig>
            {
                {
                    "格式1: Temperature:XX Humidity:XX",
                    new DataFormatConfig
                    {
                        Name = "格式1: Temperature:XX Humidity:XX",
                        TempRegex = @"Temperature:\s*(\d+\.?\d*)",
                        HumRegex = @"Humidity:\s*(\d+\.?\d*)",
                        Example = "Temperature:25 Humidity:60"
                    }
                },
                {
                    "格式2: T&H：XX XX",
                    new DataFormatConfig
                    {
                        Name = "格式2: T&H：XX XX",
                        TempRegex = @"T&H[：:]\s*(\d+\.?\d*)\s+(\d+\.?\d*)",
                        HumRegex = @"T&H[：:]\s*(\d+\.?\d*)\s+(\d+\.?\d*)",
                        Example = "T&H：24 84",
                        IsCombinedFormat = true
                    }
                },
                {
                    "格式3: Temp:XX Hum:XX",
                    new DataFormatConfig
                    {
                        Name = "格式3: Temp:XX Hum:XX",
                        TempRegex = @"Temp:\s*(\d+\.?\d*)",
                        HumRegex = @"Hum:\s*(\d+\.?\d*)",
                        Example = "Temp:25 Hum:60"
                    }
                },
                {
                    "格式4: T:XX H:XX",
                    new DataFormatConfig
                    {
                        Name = "格式4: T:XX H:XX",
                        TempRegex = @"T:\s*(\d+\.?\d*)",
                        HumRegex = @"H:\s*(\d+\.?\d*)",
                        Example = "T:25 H:60"
                    }
                },
                {
                    "格式5: XX,XX (温度,湿度)",
                    new DataFormatConfig
                    {
                        Name = "格式5: XX,XX (温度,湿度)",
                        TempRegex = @"(\d+\.?\d*)\s*,\s*(\d+\.?\d*)",
                        HumRegex = @"(\d+\.?\d*)\s*,\s*(\d+\.?\d*)",
                        Example = "25.5,60.2",
                        IsCombinedFormat = true
                    }
                },
                {
                    "自定义格式",
                    new DataFormatConfig
                    {
                        Name = "自定义格式",
                        TempRegex = _customTempRegex,
                        HumRegex = _customHumRegex,
                        Example = "使用自定义的正则表达式",
                        IsCustom = true
                    }
                }
            };
        }

        private void InitializeCustomComponents()
        {
            this.SuspendLayout();

            // 窗体设置
            this.Text = "DHT11 温湿度监控系统 v2.0";
            this.ClientSize = new Size(1350, 700);
            this.MinimumSize = new Size(1200, 600);  // 设置最小窗口尺寸
            this.FormBorderStyle = FormBorderStyle.Sizable;
            this.MaximizeBox = true;
            this.StartPosition = FormStartPosition.CenterScreen;
            this.BackColor = lightColor;
            this.FormClosing += Form1_FormClosing;
            this.Resize += Form1_Resize;

            // ========== 控制面板 ==========
            Panel controlPanel = new Panel
            {
                Location = new Point(0, 0),
                Size = new Size(1350, 110),
                BackColor = Color.White
            };
            controlPanel.Paint += ControlPanel_Paint;

            // 串口选择
            portLabel = new Label
            {
                Location = new Point(20, 15),
                AutoSize = true,
                Text = "串口选择:",
                Font = new Font("微软雅黑", 10F),
                ForeColor = darkColor
            };

            comPortComboBox = new ComboBox
            {
                Location = new Point(20, 40),
                Width = 120,
                DropDownStyle = ComboBoxStyle.DropDownList,
                Font = new Font("微软雅黑", 9F)
            };

            // 刷新按钮
            refreshButton = CreateStyledButton("刷新", new Point(145, 40), new Size(60, 27), primaryColor);
            refreshButton.Click += RefreshButton_Click;

            // 波特率选择
            baudRateLabel = new Label
            {
                Location = new Point(220, 15),
                AutoSize = true,
                Text = "波特率:",
                Font = new Font("微软雅黑", 10F),
                ForeColor = darkColor
            };

            baudRateComboBox = new ComboBox
            {
                Location = new Point(220, 40),
                Width = 100,
                DropDownStyle = ComboBoxStyle.DropDownList,
                Font = new Font("微软雅黑", 9F)
            };
            baudRateComboBox.Items.AddRange(new object[] { "9600", "19200", "38400", "57600", "115200" });
            baudRateComboBox.SelectedIndex = 0;
            baudRateComboBox.SelectedIndexChanged += BaudRateComboBox_SelectedIndexChanged;

            // 数据格式选择
            dataFormatLabel = new Label
            {
                Location = new Point(340, 15),
                AutoSize = true,
                Text = "数据格式:",
                Font = new Font("微软雅黑", 10F),
                ForeColor = darkColor
            };

            dataFormatComboBox = new ComboBox
            {
                Location = new Point(340, 40),
                Width = 250,
                DropDownStyle = ComboBoxStyle.DropDownList,
                Font = new Font("微软雅黑", 9F)
            };
            dataFormatComboBox.Items.AddRange(_dataFormats.Keys.ToArray());
            dataFormatComboBox.SelectedIndex = 0;
            dataFormatComboBox.SelectedIndexChanged += DataFormatComboBox_SelectedIndexChanged;

            // 自定义格式按钮
            customFormatButton = CreateStyledButton("自定义", new Point(595, 40), new Size(70, 27), primaryColor);
            customFormatButton.Click += CustomFormatButton_Click;
            customFormatButton.Enabled = false;

            // 连接按钮
            openButton = CreateStyledButton("连接串口", new Point(20, 75), new Size(90, 27), successColor);
            openButton.Click += OpenButton_Click;

            closeButton = CreateStyledButton("断开连接", new Point(120, 75), new Size(90, 27), dangerColor);
            closeButton.Click += CloseButton_Click;
            closeButton.Enabled = false;

            // 状态标签
            lblStatus = new Label
            {
                Location = new Point(230, 80),
                AutoSize = true,
                Text = "● 未连接",
                Font = new Font("微软雅黑", 10F, FontStyle.Bold),
                ForeColor = Color.Gray
            };

            // 清空数据按钮
            clearButton = CreateStyledButton("清空数据", new Point(360, 75), new Size(90, 27), warningColor);
            clearButton.Click += ClearButton_Click;

            // 导出数据按钮
            exportButton = CreateStyledButton("导出CSV", new Point(460, 75), new Size(90, 27), primaryColor);
            exportButton.Click += ExportButton_Click;

            // ========== MQTT配置区域 ==========
            // MQTT服务器标签
            mqttBrokerLabel = new Label
            {
                Location = new Point(700, 15),
                AutoSize = true,
                Text = "MQTT服务器:",
                Font = new Font("微软雅黑", 10F),
                ForeColor = darkColor
            };

            // MQTT服务器地址输入框
            mqttBrokerTextBox = new TextBox
            {
                Location = new Point(700, 40),
                Width = 160,
                Text = "192.168.184.221:1883",
                Font = new Font("微软雅黑", 9F)
            };

            // MQTT发布主题标签
            mqttTopicLabel = new Label
            {
                Location = new Point(870, 15),
                AutoSize = true,
                Text = "发布主题:",
                Font = new Font("微软雅黑", 10F),
                ForeColor = darkColor
            };

            // MQTT发布主题输入框
            mqttTopicTextBox = new TextBox
            {
                Location = new Point(870, 40),
                Width = 150,
                Text = "DHT11/data",
                Font = new Font("微软雅黑", 9F)
            };

            // 自动发布数据复选框（放在第二行左侧）
            mqttAutoPublishCheckBox = new CheckBox
            {
                Location = new Point(700, 80),
                AutoSize = true,
                Text = "自动发布数据到MQTT",
                Font = new Font("微软雅黑", 9F),
                Checked = true
            };
            mqttAutoPublishCheckBox.CheckedChanged += MqttAutoPublishCheckBox_CheckedChanged;

            // MQTT连接按钮（在发布主题输入框正下方）
            mqttConnectButton = CreateStyledButton("连接MQTT", new Point(870, 75), new Size(90, 27), successColor);
            mqttConnectButton.Click += MqttConnectButton_Click;

            // MQTT断开按钮（在连接按钮右边）
            mqttDisconnectButton = CreateStyledButton("断开MQTT", new Point(970, 75), new Size(90, 27), dangerColor);
            mqttDisconnectButton.Click += MqttDisconnectButton_Click;
            mqttDisconnectButton.Enabled = false;

            // MQTT状态标签（在断开按钮右边）
            mqttStatusLabel = new Label
            {
                Location = new Point(1070, 80),
                AutoSize = true,
                Text = "● 未连接",
                Font = new Font("微软雅黑", 9F, FontStyle.Bold),
                ForeColor = Color.Gray
            };

            // ========== 头像和昵称 ==========
            // 头像图片框
            avatarPictureBox = new PictureBox
            {
                Location = new Point(1150, 15),
                Size = new Size(60, 60),
                SizeMode = PictureBoxSizeMode.Zoom,
                BackColor = Color.White
            };
            // 加载并设置圆形头像
            LoadCircularAvatar();

            // 昵称标签
            avatarLabel = new Label
            {
                Location = new Point(1130, 78),
                AutoSize = false,
                Width = 100,
                Height = 20,
                Text = "爱吃橘子🍊",
                Font = new Font("微软雅黑", 9F),
                ForeColor = darkColor,
                TextAlign = ContentAlignment.MiddleCenter
            };

            // 添加到控制面板
            controlPanel.Controls.AddRange(new Control[] {
                portLabel, comPortComboBox, refreshButton,
                baudRateLabel, baudRateComboBox,
                dataFormatLabel, dataFormatComboBox, customFormatButton,
                openButton, closeButton, lblStatus,
                clearButton, exportButton,
                mqttBrokerLabel, mqttBrokerTextBox, mqttTopicLabel, mqttTopicTextBox,
                mqttConnectButton, mqttDisconnectButton,
                mqttStatusLabel, mqttAutoPublishCheckBox,
                avatarPictureBox, avatarLabel
            });

            // ========== 数据显示面板 ==========
            Panel dataPanel = new Panel
            {
                Location = new Point(20, 130),
                Size = new Size(460, 120),
                BackColor = Color.White,
                BorderStyle = BorderStyle.None
            };
            dataPanel.Paint += DataPanel_Paint;

            // 温度图标
            tempIconPictureBox = new PictureBox
            {
                Location = new Point(15, 35),
                Size = new Size(50, 50),
                SizeMode = PictureBoxSizeMode.Zoom,
                Image = LoadIconSafely("ICON\\温度.png")
            };

            // 温度显示
            Label tempTitleLabel = new Label
            {
                Location = new Point(75, 15),
                AutoSize = true,
                Text = "当前温度",
                Font = new Font("微软雅黑", 10F),
                ForeColor = darkColor
            };

            tempLabel = new Label
            {
                Location = new Point(75, 40),
                Size = new Size(120, 40),
                Text = "--.-",
                Font = new Font("Arial", 24F, FontStyle.Bold),
                ForeColor = dangerColor,
                TextAlign = ContentAlignment.MiddleLeft
            };

            tempUnitLabel = new Label
            {
                Location = new Point(195, 48),
                AutoSize = true,
                Text = "°C",
                Font = new Font("Arial", 14F),
                ForeColor = dangerColor
            };

            // 湿度图标
            humIconPictureBox = new PictureBox
            {
                Location = new Point(245, 35),
                Size = new Size(50, 50),
                SizeMode = PictureBoxSizeMode.Zoom,
                Image = LoadIconSafely("ICON\\湿度.png")
            };

            // 湿度显示
            Label humTitleLabel = new Label
            {
                Location = new Point(305, 15),
                AutoSize = true,
                Text = "当前湿度",
                Font = new Font("微软雅黑", 10F),
                ForeColor = darkColor
            };

            humLabel = new Label
            {
                Location = new Point(305, 40),
                Size = new Size(110, 40),
                Text = "--.-",
                Font = new Font("Arial", 24F, FontStyle.Bold),
                ForeColor = primaryColor,
                TextAlign = ContentAlignment.MiddleLeft
            };

            humUnitLabel = new Label
            {
                Location = new Point(415, 48),
                AutoSize = true,
                Text = "%",
                Font = new Font("Arial", 14F),
                ForeColor = primaryColor
            };

            dataPanel.Controls.AddRange(new Control[] {
                tempIconPictureBox, tempTitleLabel, tempLabel, tempUnitLabel,
                humIconPictureBox, humTitleLabel, humLabel, humUnitLabel
            });

            // ========== 报警阈值设置面板 ==========
            Panel thresholdPanel = new Panel
            {
                Location = new Point(500, 130),
                Size = new Size(270, 120),
                BackColor = Color.White,
                BorderStyle = BorderStyle.None
            };
            thresholdPanel.Paint += DataPanel_Paint;

            Label thresholdTitleLabel = new Label
            {
                Location = new Point(10, 10),
                AutoSize = true,
                Text = "报警阈值设置",
                Font = new Font("微软雅黑", 10F, FontStyle.Bold),
                ForeColor = darkColor
            };

            // 温度阈值设置
            tempThresholdLabel = new Label
            {
                Location = new Point(15, 40),
                AutoSize = true,
                Text = "温度范围:",
                Font = new Font("微软雅黑", 9F),
                ForeColor = darkColor
            };

            tempMinThreshold = new NumericUpDown
            {
                Location = new Point(85, 38),
                Width = 60,
                Minimum = -50,
                Maximum = 100,
                DecimalPlaces = 1,
                Increment = 0.5M,
                Value = (decimal)_tempMin,
                Font = new Font("微软雅黑", 9F)
            };
            tempMinThreshold.ValueChanged += TempThreshold_ValueChanged;

            Label tempRangeLabel = new Label
            {
                Location = new Point(150, 40),
                AutoSize = true,
                Text = "~",
                Font = new Font("微软雅黑", 9F),
                ForeColor = darkColor
            };

            tempMaxThreshold = new NumericUpDown
            {
                Location = new Point(165, 38),
                Width = 60,
                Minimum = -50,
                Maximum = 100,
                DecimalPlaces = 1,
                Increment = 0.5M,
                Value = (decimal)_tempMax,
                Font = new Font("微软雅黑", 9F)
            };
            tempMaxThreshold.ValueChanged += TempThreshold_ValueChanged;

            Label tempUnitLabel2 = new Label
            {
                Location = new Point(230, 40),
                AutoSize = true,
                Text = "°C",
                Font = new Font("微软雅黑", 9F),
                ForeColor = darkColor
            };

            // 湿度阈值设置
            humThresholdLabel = new Label
            {
                Location = new Point(15, 75),
                AutoSize = true,
                Text = "湿度范围:",
                Font = new Font("微软雅黑", 9F),
                ForeColor = darkColor
            };

            humMinThreshold = new NumericUpDown
            {
                Location = new Point(85, 73),
                Width = 60,
                Minimum = 0,
                Maximum = 100,
                DecimalPlaces = 1,
                Increment = 0.5M,
                Value = (decimal)_humMin,
                Font = new Font("微软雅黑", 9F)
            };
            humMinThreshold.ValueChanged += HumThreshold_ValueChanged;

            Label humRangeLabel = new Label
            {
                Location = new Point(150, 75),
                AutoSize = true,
                Text = "~",
                Font = new Font("微软雅黑", 9F),
                ForeColor = darkColor
            };

            humMaxThreshold = new NumericUpDown
            {
                Location = new Point(165, 73),
                Width = 60,
                Minimum = 0,
                Maximum = 100,
                DecimalPlaces = 1,
                Increment = 0.5M,
                Value = (decimal)_humMax,
                Font = new Font("微软雅黑", 9F)
            };
            humMaxThreshold.ValueChanged += HumThreshold_ValueChanged;

            Label humUnitLabel2 = new Label
            {
                Location = new Point(230, 75),
                AutoSize = true,
                Text = "%",
                Font = new Font("微软雅黑", 9F),
                ForeColor = darkColor
            };

            thresholdPanel.Controls.AddRange(new Control[] {
                thresholdTitleLabel,
                tempThresholdLabel, tempMinThreshold, tempRangeLabel, tempMaxThreshold, tempUnitLabel2,
                humThresholdLabel, humMinThreshold, humRangeLabel, humMaxThreshold, humUnitLabel2
            });

            // ========== 图表面板 ==========
            Label chartLabel = new Label
            {
                Location = new Point(20, 260),
                AutoSize = true,
                Text = "历史数据图表 (最近100组数据)",
                Font = new Font("微软雅黑", 10F, FontStyle.Bold),
                ForeColor = darkColor
            };

            chartPanel = new Panel
            {
                Location = new Point(20, 285),
                Size = new Size(900, 390),
                BackColor = chartBgColor,
                BorderStyle = BorderStyle.None
            };
            chartPanel.Paint += ChartPanel_Paint;

            // ========== 日志面板 ==========
            logLabel = new Label
            {
                Location = new Point(940, 130),
                AutoSize = true,
                Text = "接收日志",
                Font = new Font("微软雅黑", 10F, FontStyle.Bold),
                ForeColor = darkColor
            };

            autoScrollCheckBox = new CheckBox
            {
                Location = new Point(1240, 130),
                AutoSize = true,
                Text = "自动滚动",
                Font = new Font("微软雅黑", 9F),
                Checked = true
            };

            logTextBox = new TextBox
            {
                Location = new Point(940, 155),
                Size = new Size(390, 520),
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                BackColor = Color.White,
                Font = new Font("Consolas", 9F),
                BorderStyle = BorderStyle.FixedSingle
            };

            // 添加所有控件到窗体
            this.Controls.AddRange(new Control[] {
                controlPanel, dataPanel, thresholdPanel, chartLabel, chartPanel,
                logLabel, autoScrollCheckBox, logTextBox
            });

            this.ResumeLayout(false);
            this.PerformLayout();
        }

        // 创建样式化按钮
        private Button CreateStyledButton(string text, Point location, Size size, Color backColor)
        {
            Button btn = new Button
            {
                Text = text,
                Location = location,
                Size = size,
                BackColor = backColor,
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("微软雅黑", 9F, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            btn.FlatAppearance.BorderSize = 0;
            btn.FlatAppearance.MouseOverBackColor = ControlPaint.Light(backColor, 0.2f);
            btn.FlatAppearance.MouseDownBackColor = ControlPaint.Dark(backColor, 0.1f);
            return btn;
        }

        // 安全加载图标
        private Image LoadIconSafely(string relativePath)
        {
            try
            {
                string iconPath = Path.Combine(Application.StartupPath, relativePath);
                if (File.Exists(iconPath))
                {
                    return Image.FromFile(iconPath);
                }
                else
                {
                    LogMessage($"警告: 图标文件不存在: {iconPath}");
                    return null;
                }
            }
            catch (Exception ex)
            {
                LogMessage($"错误: 加载图标失败 - {ex.Message}");
                return null;
            }
        }

        // 加载圆形头像
        private void LoadCircularAvatar()
        {
            try
            {
                string avatarPath = Path.Combine(Application.StartupPath, "ICON\\爱吃橘子.png");
                if (File.Exists(avatarPath))
                {
                    Image originalImage = Image.FromFile(avatarPath);
                    Image circularImage = CreateCircularImage(originalImage, 60);
                    avatarPictureBox.Image = circularImage;
                }
                else
                {
                    LogMessage($"警告: 头像文件不存在: {avatarPath}");
                }
            }
            catch (Exception ex)
            {
                LogMessage($"错误: 加载头像失败 - {ex.Message}");
            }
        }

        // 创建圆形图片
        private Image CreateCircularImage(Image sourceImage, int size)
        {
            Bitmap bitmap = new Bitmap(size, size);
            using (Graphics g = Graphics.FromImage(bitmap))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;

                // 创建圆形路径
                using (GraphicsPath path = new GraphicsPath())
                {
                    path.AddEllipse(0, 0, size, size);
                    g.SetClip(path);

                    // 绘制图片
                    g.DrawImage(sourceImage, 0, 0, size, size);
                }

                // 绘制圆形边框
                using (Pen pen = new Pen(Color.FromArgb(220, 220, 220), 2))
                {
                    g.ResetClip();
                    g.DrawEllipse(pen, 1, 1, size - 2, size - 2);
                }
            }
            return bitmap;
        }

        // 温度阈值改变事件
        private void TempThreshold_ValueChanged(object sender, EventArgs e)
        {
            _tempMin = (double)tempMinThreshold.Value;
            _tempMax = (double)tempMaxThreshold.Value;

            if (_tempMin > _tempMax)
            {
                double temp = _tempMin;
                _tempMin = _tempMax;
                _tempMax = temp;
                tempMinThreshold.Value = (decimal)_tempMin;
                tempMaxThreshold.Value = (decimal)_tempMax;
            }

            LogMessage($"温度阈值已更新: {_tempMin:F1}°C ~ {_tempMax:F1}°C");

            // 重新检查当前温度
            if (tempLabel.Text != "--.-")
            {
                if (double.TryParse(tempLabel.Text, out double currentTemp))
                {
                    UpdateAlarmStatus(currentTemp, double.Parse(humLabel.Text == "--.-" ? "0" : humLabel.Text));
                }
            }
        }

        // 湿度阈值改变事件
        private void HumThreshold_ValueChanged(object sender, EventArgs e)
        {
            _humMin = (double)humMinThreshold.Value;
            _humMax = (double)humMaxThreshold.Value;

            if (_humMin > _humMax)
            {
                double temp = _humMin;
                _humMin = _humMax;
                _humMax = temp;
                humMinThreshold.Value = (decimal)_humMin;
                humMaxThreshold.Value = (decimal)_humMax;
            }

            LogMessage($"湿度阈值已更新: {_humMin:F1}% ~ {_humMax:F1}%");

            // 重新检查当前湿度
            if (humLabel.Text != "--.-")
            {
                if (double.TryParse(humLabel.Text, out double currentHum))
                {
                    UpdateAlarmStatus(double.Parse(tempLabel.Text == "--.-" ? "0" : tempLabel.Text), currentHum);
                }
            }
        }

        // 更新报警状态
        private void UpdateAlarmStatus(double temp, double hum)
        {
            // 检查温度报警
            bool tempAlarmNew = (temp < _tempMin || temp > _tempMax);
            if (tempAlarmNew != _tempAlarm)
            {
                _tempAlarm = tempAlarmNew;
                if (_tempAlarm)
                {
                    tempIconPictureBox.Image = LoadIconSafely("ICON\\温度_报警状态.png");
                    LogMessage($"⚠ 温度报警！当前: {temp:F1}°C, 阈值: {_tempMin:F1}~{_tempMax:F1}°C", warningColor);
                }
                else
                {
                    tempIconPictureBox.Image = LoadIconSafely("ICON\\温度.png");
                    LogMessage($"✓ 温度恢复正常: {temp:F1}°C", successColor);
                }
            }

            // 检查湿度报警
            bool humAlarmNew = (hum < _humMin || hum > _humMax);
            if (humAlarmNew != _humAlarm)
            {
                _humAlarm = humAlarmNew;
                if (_humAlarm)
                {
                    humIconPictureBox.Image = LoadIconSafely("ICON\\湿度_报警状态.png");
                    LogMessage($"⚠ 湿度报警！当前: {hum:F1}%, 阈值: {_humMin:F1}~{_humMax:F1}%", warningColor);
                }
                else
                {
                    humIconPictureBox.Image = LoadIconSafely("ICON\\湿度.png");
                    LogMessage($"✓ 湿度恢复正常: {hum:F1}%", successColor);
                }
            }
        }

        // 获取当前报警类型
        private string GetAlarmType(double temp, double hum)
        {
            bool tempAlarm = (temp < _tempMin || temp > _tempMax);
            bool humAlarm = (hum < _humMin || hum > _humMax);

            if (tempAlarm && humAlarm)
                return "温度湿度报警";
            else if (tempAlarm)
                return "温度报警";
            else if (humAlarm)
                return "湿度报警";
            else
                return "正常";
        }

        // 控制面板绘制边框
        private void ControlPanel_Paint(object sender, PaintEventArgs e)
        {
            Panel panel = sender as Panel;
            using (Pen pen = new Pen(Color.FromArgb(220, 220, 220), 1))
            {
                e.Graphics.DrawLine(pen, 0, panel.Height - 1, panel.Width, panel.Height - 1);
            }
        }

        // 数据面板绘制圆角边框
        private void DataPanel_Paint(object sender, PaintEventArgs e)
        {
            Panel panel = sender as Panel;
            using (Pen pen = new Pen(Color.FromArgb(220, 220, 220), 2))
            {
                Rectangle rect = new Rectangle(0, 0, panel.Width - 1, panel.Height - 1);
                DrawRoundedRectangle(e.Graphics, rect, 8, pen);
            }
        }

        // 绘制圆角矩形
        private void DrawRoundedRectangle(Graphics graphics, Rectangle bounds, int cornerRadius, Pen drawPen)
        {
            using (GraphicsPath path = new GraphicsPath())
            {
                path.AddArc(bounds.X, bounds.Y, cornerRadius, cornerRadius, 180, 90);
                path.AddArc(bounds.X + bounds.Width - cornerRadius, bounds.Y, cornerRadius, cornerRadius, 270, 90);
                path.AddArc(bounds.X + bounds.Width - cornerRadius, bounds.Y + bounds.Height - cornerRadius, cornerRadius, cornerRadius, 0, 90);
                path.AddArc(bounds.X, bounds.Y + bounds.Height - cornerRadius, cornerRadius, cornerRadius, 90, 90);
                path.CloseAllFigures();
                graphics.DrawPath(drawPen, path);
            }
        }

        private void InitializeSerialPort()
        {
            _serialPort = new SerialPort
            {
                Parity = Parity.None,
                StopBits = StopBits.One,
                DataBits = 8,
                Handshake = Handshake.None,
                RtsEnable = true,      // 启用RTS信号
                DtrEnable = true,      // 启用DTR信号
                ReadTimeout = 500,
                WriteTimeout = 500,
                Encoding = Encoding.UTF8  // 使用UTF8编码
            };
            _serialPort.DataReceived += SerialPort_DataReceived;
        }

        private void InitializeChart()
        {
            _tempHistory.Clear();
            _humHistory.Clear();
        }

        private void LoadAvailablePorts()
        {
            try
            {
                comPortComboBox.Items.Clear();
                var ports = SerialPort.GetPortNames();
                if (ports.Length > 0)
                {
                    comPortComboBox.Items.AddRange(ports);
                    comPortComboBox.SelectedIndex = 0;
                    LogMessage($"检测到 {ports.Length} 个串口: {string.Join(", ", ports)}");
                }
                else
                {
                    comPortComboBox.Items.Add("无可用串口");
                    comPortComboBox.SelectedIndex = 0;
                    LogMessage("警告: 未检测到可用串口！");
                }
            }
            catch (Exception ex)
            {
                comPortComboBox.Items.Add("无可用串口");
                comPortComboBox.SelectedIndex = 0;
                LogMessage($"错误: 枚举串口失败 - {ex.Message}");
            }
        }

        private void RefreshButton_Click(object sender, EventArgs e)
        {
            LoadAvailablePorts();
        }

        private void BaudRateComboBox_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (baudRateComboBox.SelectedItem != null && _serialPort != null && !_serialPort.IsOpen)
            {
                if (int.TryParse(baudRateComboBox.SelectedItem.ToString(), out int br))
                {
                    _serialPort.BaudRate = br;
                    LogMessage($"波特率设置为: {br}");
                }
            }
        }

        private void OpenButton_Click(object sender, EventArgs e)
        {
            if (_serialPort == null) InitializeSerialPort();

            if (_serialPort.IsOpen) return;

            if (comPortComboBox.SelectedIndex == -1 || comPortComboBox.SelectedItem.ToString() == "无可用串口")
            {
                MessageBox.Show("请选择有效的串口！", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            try
            {
                _serialPort.PortName = comPortComboBox.SelectedItem.ToString();
                if (int.TryParse(baudRateComboBox.SelectedItem.ToString(), out int br))
                    _serialPort.BaudRate = br;

                _serialPort.Open();
                _receivedBuffer.Clear();

                openButton.Enabled = false;
                closeButton.Enabled = true;
                comPortComboBox.Enabled = false;
                baudRateComboBox.Enabled = false;
                refreshButton.Enabled = false;

                lblStatus.Text = "● 已连接";
                lblStatus.ForeColor = successColor;

                LogMessage($"成功连接到 {_serialPort.PortName}，波特率: {_serialPort.BaudRate}");
                LogMessage("等待接收数据...");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"串口打开失败:\n{ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                LogMessage($"错误: 打开串口失败 - {ex.Message}");
            }
        }

        private void CloseButton_Click(object sender, EventArgs e)
        {
            CloseSerialPort();
        }

        private void CloseSerialPort()
        {
            try
            {
                if (_serialPort != null && _serialPort.IsOpen)
                {
                    _serialPort.Close();
                    LogMessage($"已断开串口 {_serialPort.PortName}");
                }
            }
            catch (Exception ex)
            {
                LogMessage($"错误: 关闭串口失败 - {ex.Message}");
            }

            openButton.Enabled = true;
            closeButton.Enabled = false;
            comPortComboBox.Enabled = true;
            baudRateComboBox.Enabled = true;
            refreshButton.Enabled = true;

            lblStatus.Text = "● 未连接";
            lblStatus.ForeColor = Color.Gray;
        }

        private void ClearButton_Click(object sender, EventArgs e)
        {
            _tempHistory.Clear();
            _humHistory.Clear();
            _timestampHistory.Clear();
            _alarmTypeHistory.Clear();
            tempLabel.Text = "--.-";
            humLabel.Text = "--.-";
            chartPanel.Invalidate();
            LogMessage("已清空历史数据");
        }

        private void ExportButton_Click(object sender, EventArgs e)
        {
            if (_tempHistory.Count == 0)
            {
                MessageBox.Show("没有数据可导出！", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            using (SaveFileDialog sfd = new SaveFileDialog())
            {
                sfd.Filter = "CSV文件|*.csv|所有文件|*.*";
                sfd.FileName = $"DHT11数据_{DateTime.Now:yyyyMMdd_HHmmss}.csv";

                if (sfd.ShowDialog() == DialogResult.OK)
                {
                    try
                    {
                        using (StreamWriter sw = new StreamWriter(sfd.FileName, false, Encoding.UTF8))
                        {
                            // 写入CSV标题行
                            sw.WriteLine("序号,温度(°C),湿度(%),报警类型,时间戳");

                            // 写入数据行
                            for (int i = 0; i < _tempHistory.Count; i++)
                            {
                                string timestamp = i < _timestampHistory.Count ? _timestampHistory[i] : "";
                                string alarmType = i < _alarmTypeHistory.Count ? _alarmTypeHistory[i] : "正常";
                                sw.WriteLine($"{i + 1},{_tempHistory[i]:F1},{_humHistory[i]:F1},{alarmType},{timestamp}");
                            }
                        }
                        LogMessage($"数据已导出到: {sfd.FileName}");
                        MessageBox.Show("数据导出成功！", "成功", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"导出失败:\n{ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        LogMessage($"错误: 导出数据失败 - {ex.Message}");
                    }
                }
            }
        }

        // 关键修复：改进的串口数据接收处理
        private void SerialPort_DataReceived(object sender, SerialDataReceivedEventArgs e)
        {
            try
            {
                // 读取所有可用数据到缓冲区
                int bytesToRead = _serialPort.BytesToRead;
                byte[] buffer = new byte[bytesToRead];
                _serialPort.Read(buffer, 0, bytesToRead);
                string data = Encoding.UTF8.GetString(buffer);

                _receivedBuffer.Append(data);

                // 处理缓冲区中的完整行（支持多种换行符）
                string bufferContent = _receivedBuffer.ToString();
                string[] lines = bufferContent.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);

                // 处理除最后一个元素外的所有行（最后一个可能不完整）
                for (int i = 0; i < lines.Length - 1; i++)
                {
                    string line = lines[i].Trim();
                    if (!string.IsNullOrEmpty(line))
                    {
                        ProcessReceivedLine(line);
                    }
                }

                // 保留最后一个不完整的部分
                _receivedBuffer.Clear();
                _receivedBuffer.Append(lines[lines.Length - 1]);

                // 防止缓冲区无限增长
                if (_receivedBuffer.Length > 1000)
                {
                    _receivedBuffer.Clear();
                }
            }
            catch (Exception ex)
            {
                LogMessage($"数据接收错误: {ex.Message}");
            }
        }

        private void ProcessReceivedLine(string line)
        {
            try
            {
                // 记录原始数据
                this.Invoke((MethodInvoker)delegate
                {
                    LogMessage($"接收: {line}");
                });

                // 解析数据
                var parsed = ParseData(line);
                if (parsed.HasValue)
                {
                    var p = parsed.Value;
                    string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                    this.Invoke((MethodInvoker)delegate
                    {
                        tempLabel.Text = $"{p.Temp:F1}";
                        humLabel.Text = $"{p.Hum:F1}";

                        // 获取报警类型
                        string alarmType = GetAlarmType(p.Temp, p.Hum);

                        // 添加到图表，包含时间戳和报警类型
                        AddToChart(p.Temp, p.Hum, timestamp, alarmType);
                        chartPanel.Invalidate();
                        LogMessage($"解析成功: 温度={p.Temp:F1}°C, 湿度={p.Hum:F1}%", Color.Green);

                        // 检查报警状态
                        UpdateAlarmStatus(p.Temp, p.Hum);

                        // 发布数据到MQTT
                        PublishDataToMqtt(p.Temp, p.Hum);
                    });
                }
                else
                {
                    this.Invoke((MethodInvoker)delegate
                    {
                        LogMessage($"解析失败: 数据格式不正确", Color.Red);
                    });
                }
            }
            catch (Exception ex)
            {
                this.Invoke((MethodInvoker)delegate
                {
                    LogMessage($"处理数据异常: {ex.Message}", Color.Red);
                });
            }
        }

        private (double Temp, double Hum)? ParseData(string raw)
        {
            try
            {
                string s = raw.Trim();
                if (string.IsNullOrEmpty(s)) return null;

                // 获取当前选择的数据格式配置
                if (!_dataFormats.TryGetValue(_currentDataFormat, out DataFormatConfig config))
                {
                    // 如果找不到配置,使用默认格式
                    config = _dataFormats["格式1: Temperature:XX Humidity:XX"];
                }

                double temp = 0, hum = 0;

                // 检查是否是组合格式(温湿度在同一个正则表达式中)
                if (config.IsCombinedFormat)
                {
                    // 使用温度正则(组合格式下,温度和湿度共用一个正则)
                    var match = Regex.Match(s, config.TempRegex);
                    if (!match.Success || match.Groups.Count < 3) return null;

                    // 第一个捕获组是温度,第二个捕获组是湿度
                    if (!double.TryParse(match.Groups[1].Value, out temp)) return null;
                    if (!double.TryParse(match.Groups[2].Value, out hum)) return null;
                }
                else
                {
                    // 分别匹配温度和湿度
                    var tempMatch = Regex.Match(s, config.TempRegex);
                    var humMatch = Regex.Match(s, config.HumRegex);

                    if (!tempMatch.Success || !humMatch.Success) return null;
                    if (tempMatch.Groups.Count < 2 || humMatch.Groups.Count < 2) return null;

                    // 提取第一个捕获组的值
                    if (!double.TryParse(tempMatch.Groups[1].Value, out temp)) return null;
                    if (!double.TryParse(humMatch.Groups[1].Value, out hum)) return null;
                }

                // 合理性检查
                if (temp < -50 || temp > 100) return null;  // 温度范围检查
                if (hum < 0 || hum > 100) return null;       // 湿度范围检查

                return (temp, hum);
            }
            catch (Exception ex)
            {
                // 记录解析异常(用于调试)
                System.Diagnostics.Debug.WriteLine($"ParseData异常: {ex.Message}");
                return null;
            }
        }

        private void AddToChart(double temp, double hum, string timestamp, string alarmType)
        {
            _tempHistory.Add(temp);
            _humHistory.Add(hum);
            _timestampHistory.Add(timestamp);
            _alarmTypeHistory.Add(alarmType);
            if (_tempHistory.Count > MaxHistoryPoints)
            {
                _tempHistory.RemoveAt(0);
                _humHistory.RemoveAt(0);
                _timestampHistory.RemoveAt(0);
                _alarmTypeHistory.RemoveAt(0);
            }
        }

        private void LogMessage(string message, Color? color = null)
        {
            try
            {
                string timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
                string logEntry = $"[{timestamp}] {message}\r\n";

                _logBuffer.Append(logEntry);

                // 限制日志缓冲区大小
                if (_logBuffer.Length > 50000)
                {
                    _logBuffer.Remove(0, 10000);
                }

                if (logTextBox.InvokeRequired)
                {
                    logTextBox.Invoke((MethodInvoker)delegate
                    {
                        UpdateLogTextBox(logEntry);
                    });
                }
                else
                {
                    UpdateLogTextBox(logEntry);
                }
            }
            catch { }
        }

        private void UpdateLogTextBox(string logEntry)
        {
            logTextBox.AppendText(logEntry);
            if (autoScrollCheckBox.Checked)
            {
                logTextBox.SelectionStart = logTextBox.Text.Length;
                logTextBox.ScrollToCaret();
            }
        }

        private void ChartPanel_Paint(object sender, PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(chartBgColor);

            var rect = chartPanel.ClientRectangle;
            int leftMargin = 60;
            int rightMargin = 60;
            int topMargin = 30;
            int bottomMargin = 50;

            int left = rect.Left + leftMargin;
            int top = rect.Top + topMargin;
            int right = rect.Right - rightMargin;
            int bottom = rect.Bottom - bottomMargin;

            if (right <= left || bottom <= top) return;

            // 绘制背景网格
            DrawGrid(g, left, top, right, bottom);

            // 绘制坐标轴
            using (var axisPen = new Pen(darkColor, 2))
            {
                g.DrawLine(axisPen, left, top, left, bottom);           // Y轴
                g.DrawLine(axisPen, left, bottom, right, bottom);       // X轴
            }

            // 绘制Y轴刻度 (左侧：温度 0-40°C)
            DrawTemperatureYAxis(g, left, top, bottom);

            // 绘制Y轴刻度 (右侧：湿度 0-100%)
            DrawHumidityYAxis(g, right, top, bottom);

            // 绘制X轴刻度
            DrawXAxis(g, left, right, bottom);

            // 绘制数据曲线
            if (_tempHistory.Count > 1)
                DrawTemperatureSeries(g, _tempHistory, left, top, right, bottom);
            if (_humHistory.Count > 1)
                DrawHumiditySeries(g, _humHistory, left, top, right, bottom);

            // 绘制图例
            DrawLegend(g, right - 150, top);

            // 绘制标题
            using (var titleFont = new Font("微软雅黑", 11F, FontStyle.Bold))
            {
                string title = $"数据点数: {_tempHistory.Count}/{MaxHistoryPoints}";
                g.DrawString(title, titleFont, new SolidBrush(darkColor), left, top - 25);
            }
        }

        private void DrawGrid(Graphics g, int left, int top, int right, int bottom)
        {
            using (var gridPen = new Pen(Color.FromArgb(230, 230, 230), 1))
            {
                gridPen.DashStyle = DashStyle.Dot;

                // 水平网格线
                int hSteps = 8;
                for (int i = 0; i <= hSteps; i++)
                {
                    int y = top + (bottom - top) * i / hSteps;
                    g.DrawLine(gridPen, left, y, right, y);
                }

                // 垂直网格线
                int vSteps = 10;
                for (int i = 0; i <= vSteps; i++)
                {
                    int x = left + (right - left) * i / vSteps;
                    g.DrawLine(gridPen, x, top, x, bottom);
                }
            }
        }

        private void DrawTemperatureYAxis(Graphics g, int x, int top, int bottom)
        {
            using (var font = new Font("Arial", 9F))
            using (var brush = new SolidBrush(dangerColor))
            {
                int steps = 8;
                for (int i = 0; i <= steps; i++)
                {
                    int y = bottom - (bottom - top) * i / steps;
                    double value = 40.0 * i / steps;  // 0-40度

                    // 刻度线
                    g.DrawLine(new Pen(darkColor, 1), x - 5, y, x, y);

                    // 刻度值
                    string label = value.ToString("F0");
                    SizeF size = g.MeasureString(label, font);
                    g.DrawString(label, font, brush, x - size.Width - 10, y - size.Height / 2);
                }

                // Y轴标签
                using (var labelFont = new Font("微软雅黑", 10F, FontStyle.Bold))
                {
                    string axisLabel = "温度(°C)";
                    SizeF labelSize = g.MeasureString(axisLabel, labelFont);
                    g.DrawString(axisLabel, labelFont, brush, x - labelSize.Width + 2, top - 25);
                }
            }
        }

        private void DrawHumidityYAxis(Graphics g, int x, int top, int bottom)
        {
            using (var font = new Font("Arial", 9F))
            using (var brush = new SolidBrush(primaryColor))
            {
                int steps = 10;
                for (int i = 0; i <= steps; i++)
                {
                    int y = bottom - (bottom - top) * i / steps;
                    double value = 100.0 * i / steps;  // 0-100%

                    // 刻度线
                    g.DrawLine(new Pen(darkColor, 1), x, y, x + 5, y);

                    // 刻度值
                    string label = value.ToString("F0");
                    g.DrawString(label, font, brush, x + 10, y - font.Height / 2);
                }

                // Y轴标签
                using (var labelFont = new Font("微软雅黑", 10F, FontStyle.Bold))
                {
                    string axisLabel = "湿度(%)";
                    g.DrawString(axisLabel, labelFont, brush, x + 10, top - 25);
                }
            }
        }

        private void DrawXAxis(Graphics g, int left, int right, int bottom)
        {
            using (var font = new Font("Arial", 9F))
            using (var brush = new SolidBrush(darkColor))
            {
                // X轴标签
                string label = "数据序号 →";
                SizeF size = g.MeasureString(label, font);
                g.DrawString(label, font, brush, (left + right) / 2 - size.Width / 2, bottom + 25);
            }
        }

        private void DrawTemperatureSeries(Graphics g, List<double> data, int left, int top, int right, int bottom)
        {
            int count = data.Count;
            if (count < 1) return;

            float width = right - left;
            float height = bottom - top;
            float xStep = count > 1 ? width / (count - 1) : 0;

            using (var pen = new Pen(dangerColor, 3))
            using (var shadowPen = new Pen(Color.FromArgb(50, dangerColor), 5))
            {
                pen.LineJoin = LineJoin.Round;

                PointF? prev = null;
                for (int i = 0; i < count; i++)
                {
                    float x = left + i * xStep;
                    double v = data[i];

                    // 映射到 0-40 度范围
                    float ratio = (float)(v / 40.0);
                    if (ratio < 0) ratio = 0;
                    if (ratio > 1) ratio = 1;

                    float y = bottom - ratio * height;
                    var cur = new PointF(x, y);

                    if (prev.HasValue)
                    {
                        g.DrawLine(shadowPen, prev.Value, cur);  // 阴影
                        g.DrawLine(pen, prev.Value, cur);         // 主线
                    }

                    // 绘制数据点
                    g.FillEllipse(new SolidBrush(dangerColor), x - 3, y - 3, 6, 6);

                    prev = cur;
                }
            }
        }

        private void DrawHumiditySeries(Graphics g, List<double> data, int left, int top, int right, int bottom)
        {
            int count = data.Count;
            if (count < 1) return;

            float width = right - left;
            float height = bottom - top;
            float xStep = count > 1 ? width / (count - 1) : 0;

            using (var pen = new Pen(primaryColor, 3))
            using (var shadowPen = new Pen(Color.FromArgb(50, primaryColor), 5))
            {
                pen.LineJoin = LineJoin.Round;

                PointF? prev = null;
                for (int i = 0; i < count; i++)
                {
                    float x = left + i * xStep;
                    double v = data[i];

                    // 映射到 0-100% 范围
                    float ratio = (float)(v / 100.0);
                    if (ratio < 0) ratio = 0;
                    if (ratio > 1) ratio = 1;

                    float y = bottom - ratio * height;
                    var cur = new PointF(x, y);

                    if (prev.HasValue)
                    {
                        g.DrawLine(shadowPen, prev.Value, cur);  // 阴影
                        g.DrawLine(pen, prev.Value, cur);         // 主线
                    }

                    // 绘制数据点
                    g.FillEllipse(new SolidBrush(primaryColor), x - 3, y - 3, 6, 6);

                    prev = cur;
                }
            }
        }

        private void DrawLegend(Graphics g, int x, int y)
        {
            using (var font = new Font("微软雅黑", 9F))
            {
                // 温度图例
                g.DrawLine(new Pen(dangerColor, 3), x, y + 5, x + 30, y + 5);
                g.FillEllipse(new SolidBrush(dangerColor), x + 12, y + 2, 6, 6);
                g.DrawString("温度", font, new SolidBrush(dangerColor), x + 35, y);

                // 湿度图例
                g.DrawLine(new Pen(primaryColor, 3), x, y + 25, x + 30, y + 25);
                g.FillEllipse(new SolidBrush(primaryColor), x + 12, y + 22, 6, 6);
                g.DrawString("湿度", font, new SolidBrush(primaryColor), x + 35, y + 20);
            }
        }

        private void Form1_FormClosing(object sender, FormClosingEventArgs e)
        {
            try
            {
                // 断开串口
                if (_serialPort != null && _serialPort.IsOpen)
                {
                    _serialPort.DataReceived -= SerialPort_DataReceived;
                    _serialPort.Close();
                    _serialPort.Dispose();
                }

                // 断开MQTT
                if (_mqttClient != null && _mqttClient.IsConnected)
                {
                    _mqttClient.DisconnectAsync().Wait(1000);
                }
            }
            catch { }
        }

        private void Form1_Resize(object sender, EventArgs e)
        {
            if (this.WindowState == FormWindowState.Minimized) return;

            try
            {
                // 获取当前窗体的客户区大小
                int clientWidth = this.ClientSize.Width;
                int clientHeight = this.ClientSize.Height;

                // 调整图表面板的大小和位置
                if (chartPanel != null)
                {
                    chartPanel.Width = clientWidth - 450;  // 右侧留出空间给日志
                    chartPanel.Height = clientHeight - 315;  // 上方留出空间给控件
                }

                // 调整日志文本框的位置和大小
                if (logTextBox != null)
                {
                    logTextBox.Location = new Point(clientWidth - 410, 155);
                    logTextBox.Width = 390;
                    logTextBox.Height = clientHeight - 165;
                }

                // 调整日志标签位置
                if (logLabel != null)
                {
                    logLabel.Location = new Point(clientWidth - 410, 130);
                }

                // 调整自动滚动复选框位置
                if (autoScrollCheckBox != null)
                {
                    autoScrollCheckBox.Location = new Point(clientWidth - 110, 130);
                }

                // 重绘图表
                if (chartPanel != null)
                {
                    chartPanel.Invalidate();
                }
            }
            catch { }
        }

        // 数据格式选择改变事件
        private void DataFormatComboBox_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (dataFormatComboBox.SelectedItem == null) return;

            _currentDataFormat = dataFormatComboBox.SelectedItem.ToString();

            if (_dataFormats.TryGetValue(_currentDataFormat, out DataFormatConfig config))
            {
                // 如果是自定义格式,启用自定义按钮
                customFormatButton.Enabled = config.IsCustom;

                // 如果不是自定义格式,更新当前使用的正则表达式
                if (!config.IsCustom)
                {
                    _customTempRegex = config.TempRegex;
                    _customHumRegex = config.HumRegex;
                }

                LogMessage($"数据格式已切换为: {_currentDataFormat}");
                LogMessage($"示例格式: {config.Example}");
            }
        }

        // 自定义格式按钮点击事件
        private void CustomFormatButton_Click(object sender, EventArgs e)
        {
            using (var customDialog = new CustomFormatDialog(_customTempRegex, _customHumRegex))
            {
                if (customDialog.ShowDialog() == DialogResult.OK)
                {
                    _customTempRegex = customDialog.TempRegex;
                    _customHumRegex = customDialog.HumRegex;

                    // 更新自定义格式配置
                    if (_dataFormats.ContainsKey("自定义格式"))
                    {
                        _dataFormats["自定义格式"].TempRegex = _customTempRegex;
                        _dataFormats["自定义格式"].HumRegex = _customHumRegex;
                    }

                    LogMessage("自定义数据格式已更新");
                    LogMessage($"温度正则: {_customTempRegex}");
                    LogMessage($"湿度正则: {_customHumRegex}");
                }
            }
        }

        #region MQTT相关方法

        // 初始化MQTT客户端
        private void InitializeMqttClient()
        {
            try
            {
                var factory = new MqttFactory();
                _mqttClient = factory.CreateMqttClient();
                LogMessage("MQTT客户端已初始化");
            }
            catch (Exception ex)
            {
                LogMessage($"MQTT客户端初始化失败: {ex.Message}", Color.Red);
            }
        }

        // MQTT连接按钮点击事件
        private async void MqttConnectButton_Click(object sender, EventArgs e)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(mqttBrokerTextBox.Text))
                {
                    MessageBox.Show("请输入MQTT服务器地址!", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                if (string.IsNullOrWhiteSpace(mqttTopicTextBox.Text))
                {
                    MessageBox.Show("请输入MQTT发布主题!", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                // 解析服务器地址和端口
                string[] parts = mqttBrokerTextBox.Text.Split(':');
                string server = parts[0];
                int port = parts.Length > 1 ? int.Parse(parts[1]) : 1883;

                // 读取发布主题
                _mqttPublishTopic = mqttTopicTextBox.Text.Trim();

                UpdateMqttStatus("连接中...", Color.Orange);
                LogMessage($"正在连接MQTT服务器: {mqttBrokerTextBox.Text}");

                // 创建连接选项
                var options = new MqttClientOptionsBuilder()
                    .WithTcpServer(server, port)
                    .WithClientId("DHT11_Client_" + Guid.NewGuid().ToString())
                    .WithCleanSession()
                    .Build();

                // 连接到MQTT服务器
                var result = await _mqttClient.ConnectAsync(options);

                if (result.ResultCode == MqttClientConnectResultCode.Success)
                {
                    UpdateMqttStatus("已连接", Color.Green);
                    mqttConnectButton.Enabled = false;
                    mqttDisconnectButton.Enabled = true;
                    mqttBrokerTextBox.Enabled = false;
                    mqttTopicTextBox.Enabled = false;

                    LogMessage($"MQTT连接成功: {mqttBrokerTextBox.Text}", Color.Green);
                    LogMessage($"发布主题: {_mqttPublishTopic}");
                }
                else
                {
                    UpdateMqttStatus("连接失败", Color.Red);
                    LogMessage($"MQTT连接失败: {result.ResultCode}", Color.Red);
                    MessageBox.Show($"MQTT连接失败: {result.ResultCode}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
            catch (Exception ex)
            {
                UpdateMqttStatus("连接失败", Color.Red);
                LogMessage($"MQTT连接异常: {ex.Message}", Color.Red);
                MessageBox.Show($"连接MQTT服务器失败: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        // MQTT断开按钮点击事件
        private async void MqttDisconnectButton_Click(object sender, EventArgs e)
        {
            try
            {
                if (_mqttClient != null && _mqttClient.IsConnected)
                {
                    await _mqttClient.DisconnectAsync();
                    UpdateMqttStatus("未连接", Color.Gray);
                    mqttConnectButton.Enabled = true;
                    mqttDisconnectButton.Enabled = false;
                    mqttBrokerTextBox.Enabled = true;
                    mqttTopicTextBox.Enabled = true;

                    LogMessage("MQTT已断开连接");
                }
            }
            catch (Exception ex)
            {
                LogMessage($"MQTT断开连接失败: {ex.Message}", Color.Red);
                MessageBox.Show($"断开MQTT连接失败: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        // 自动发布复选框状态改变事件
        private void MqttAutoPublishCheckBox_CheckedChanged(object sender, EventArgs e)
        {
            _mqttAutoPublish = mqttAutoPublishCheckBox.Checked;
            LogMessage($"自动发布MQTT数据: {(_mqttAutoPublish ? "已开启" : "已关闭")}");
        }

        // 发布温湿度数据到MQTT
        private async void PublishDataToMqtt(double temperature, double humidity)
        {
            try
            {
                if (_mqttClient == null || !_mqttClient.IsConnected)
                {
                    return; // 如果未连接，不发送
                }

                if (!_mqttAutoPublish)
                {
                    return; // 如果未开启自动发布，不发送
                }

                // 构建JSON格式的数据
                string jsonData = $@"{{
    ""timestamp"": ""{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}"",
    ""temperature"": {temperature},
    ""humidity"": {humidity},
    ""deviceId"": ""DHT11_Sensor_01"",
    ""dataFormat"": ""{_currentDataFormat}""
}}";

                var message = new MqttApplicationMessageBuilder()
                    .WithTopic(_mqttPublishTopic)
                    .WithPayload(jsonData)
                    .WithQualityOfServiceLevel(MQTTnet.Protocol.MqttQualityOfServiceLevel.AtLeastOnce)
                    .WithRetainFlag(false)
                    .Build();

                await _mqttClient.PublishAsync(message);
                LogMessage($"[MQTT发送] 温度:{temperature}°C 湿度:{humidity}%", Color.Blue);
            }
            catch (Exception ex)
            {
                LogMessage($"MQTT发布数据失败: {ex.Message}", Color.Red);
            }
        }

        // 更新MQTT连接状态标签
        private void UpdateMqttStatus(string status, Color color)
        {
            if (mqttStatusLabel.InvokeRequired)
            {
                mqttStatusLabel.Invoke(new Action(() => UpdateMqttStatus(status, color)));
                return;
            }

            mqttStatusLabel.Text = "● " + status;
            mqttStatusLabel.ForeColor = color;
        }

        #endregion
    }

    // 数据格式配置类
    public class DataFormatConfig
    {
        public string Name { get; set; }
        public string TempRegex { get; set; }
        public string HumRegex { get; set; }
        public string Example { get; set; }
        public bool IsCombinedFormat { get; set; } = false;  // 是否是组合格式(温湿度在同一个匹配中)
        public bool IsCustom { get; set; } = false;  // 是否是自定义格式
    }

    // 自定义格式对话框
    public class CustomFormatDialog : Form
    {
        private TextBox tempRegexTextBox;
        private TextBox humRegexTextBox;
        private Button okButton;
        private Button cancelButton;
        private Label tempLabel;
        private Label humLabel;
        private Label infoLabel;

        public string TempRegex { get; private set; }
        public string HumRegex { get; private set; }

        public CustomFormatDialog(string currentTempRegex, string currentHumRegex)
        {
            TempRegex = currentTempRegex;
            HumRegex = currentHumRegex;

            InitializeDialog();
        }

        private void InitializeDialog()
        {
            this.Text = "自定义数据格式";
            this.ClientSize = new Size(600, 385);
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.StartPosition = FormStartPosition.CenterParent;
            this.MaximizeBox = false;
            this.MinimizeBox = false;

            // 说明标签
            infoLabel = new Label
            {
                Location = new Point(20, 15),
                Size = new Size(560, 100),
                Text = "请输入用于匹配温度和湿度的正则表达式。\n" +
                       "正则表达式应该包含至少一个捕获组 ()，用于提取数值。\n\n" +
                       "示例：\n" +
                       "  Temperature:\\s*(\\d+\\.?\\d*)  匹配 \"Temperature:25.5\"\n" +
                       "  T&H[：:]\\s*(\\d+\\.?\\d*)\\s+(\\d+\\.?\\d*)  匹配 \"T&H：24 84\"",
                Font = new Font("微软雅黑", 9F),
                ForeColor = Color.FromArgb(52, 73, 94)
            };

            // 温度正则表达式
            tempLabel = new Label
            {
                Location = new Point(20, 145),
                Size = new Size(560, 20),
                Text = "温度正则表达式:",
                Font = new Font("微软雅黑", 10F, FontStyle.Bold),
                ForeColor = Color.FromArgb(52, 73, 94)
            };

            tempRegexTextBox = new TextBox
            {
                Location = new Point(20, 170),
                Size = new Size(560, 30),
                Text = TempRegex,
                Font = new Font("Consolas", 10F),
                Multiline = false
            };

            // 湿度正则表达式
            humLabel = new Label
            {
                Location = new Point(20, 215),
                Size = new Size(560, 20),
                Text = "湿度正则表达式:",
                Font = new Font("微软雅黑", 10F, FontStyle.Bold),
                ForeColor = Color.FromArgb(52, 73, 94)
            };

            humRegexTextBox = new TextBox
            {
                Location = new Point(20, 240),
                Size = new Size(560, 30),
                Text = HumRegex,
                Font = new Font("Consolas", 10F),
                Multiline = false
            };

            // 确定按钮
            okButton = new Button
            {
                Text = "确定",
                Location = new Point(380, 305),
                Size = new Size(100, 35),
                BackColor = Color.FromArgb(76, 175, 80),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("微软雅黑", 10F, FontStyle.Bold),
                DialogResult = DialogResult.OK
            };
            okButton.FlatAppearance.BorderSize = 0;
            okButton.Click += OkButton_Click;

            // 取消按钮
            cancelButton = new Button
            {
                Text = "取消",
                Location = new Point(490, 305),
                Size = new Size(100, 35),
                BackColor = Color.FromArgb(244, 67, 54),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("微软雅黑", 10F, FontStyle.Bold),
                DialogResult = DialogResult.Cancel
            };
            cancelButton.FlatAppearance.BorderSize = 0;

            // 添加控件
            this.Controls.AddRange(new Control[] {
                infoLabel, tempLabel, tempRegexTextBox,
                humLabel, humRegexTextBox, okButton, cancelButton
            });

            this.AcceptButton = okButton;
            this.CancelButton = cancelButton;
        }

        private void OkButton_Click(object sender, EventArgs e)
        {
            string tempRegex = tempRegexTextBox.Text.Trim();
            string humRegex = humRegexTextBox.Text.Trim();

            if (string.IsNullOrWhiteSpace(tempRegex) || string.IsNullOrWhiteSpace(humRegex))
            {
                MessageBox.Show("正则表达式不能为空！", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                this.DialogResult = DialogResult.None;
                return;
            }

            // 验证正则表达式是否有效
            try
            {
                Regex.Match("test", tempRegex);
                Regex.Match("test", humRegex);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"正则表达式格式错误:\n{ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                this.DialogResult = DialogResult.None;
                return;
            }

            TempRegex = tempRegex;
            HumRegex = humRegex;
        }
    }
}
