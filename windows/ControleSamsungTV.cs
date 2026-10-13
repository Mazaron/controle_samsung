using System;
using System.Collections.Generic;
using DrawingIcon = System.Drawing.Icon;
using DrawingSystemIcons = System.Drawing.SystemIcons;
using System.IO;
using System.Net;
using System.Net.Security;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Ellipse = System.Windows.Shapes.Ellipse;
using System.Windows.Threading;
using Microsoft.Win32;
using Forms = System.Windows.Forms;
using MediaColor = System.Windows.Media.Color;

namespace ControleSamsungTV
{
    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            ServicePointManager.ServerCertificateValidationCallback = delegate { return true; };
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
            try
            {
                var app = new Application();
                app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
                var window = new RemoteWindow(Array.IndexOf(args, "--tray") >= 0);
                app.MainWindow = window;
                if (Array.IndexOf(args, "--tray") < 0) window.Show();
                app.Run();
            }
            catch (Exception ex) { MessageBox.Show(ex.ToString(), "Controle Samsung TV", MessageBoxButton.OK, MessageBoxImage.Error); }
        }
    }

    internal sealed class RemoteWindow : Window
    {
        private static readonly MediaColor Bg = Color("#080d16");
        private static readonly MediaColor CardColor = Color("#111c2a");
        private static readonly MediaColor Card2 = Color("#192638");
        private static readonly MediaColor White = Color("#f0f6ff");
        private static readonly MediaColor Muted = Color("#93a2b8");
        private static readonly MediaColor Cyan = Color("#65d9e8");
        private static readonly MediaColor Blue = Color("#5595ff");
        private static readonly MediaColor Red = Color("#ff627d");

        private readonly string ip;
        private readonly string token;
        private readonly string smartThingsToken;
        private readonly string smartThingsDeviceId;
        private readonly Forms.NotifyIcon tray;
        private TextBlock stateLabel;
        private TextBlock modelLabel;
        private TextBlock contentLabel;
        private TextBlock contentHint;
        private TextBlock messageLabel;
        private readonly DispatcherTimer pollTimer;
        private readonly DispatcherTimer volumePollTimer;
        private readonly StackPanel pages;
        private readonly Dictionary<string, UIElement> pageMap = new Dictionary<string, UIElement>();
        private readonly Dictionary<string, Button> tabs = new Dictionary<string, Button>();
        private bool allowClose;
        private bool busy;
        private bool statusReadPending;
        private int volumeLevel;
        private Border volumeFill;
        private Ellipse volumeThumb;
        private Canvas volumeTrack;
        private TextBlock volumeLabel;
        private bool volumeDragging;
        private bool volumeReadPending;

        public RemoteWindow(bool startHidden)
        {
            // Este é o IP confirmado para a TV deste projeto; o .env continua podendo sobrescrevê-lo.
            ip = ReadSetting("IP", "");
            token = ReadSetting("TOKEN_TV", "");
            smartThingsToken = ReadSetting("SMARTTHINGS_TOKEN", "");
            smartThingsDeviceId = ReadSetting("SMARTTHINGS_DEVICE_ID", "");
            Title = "Controle Samsung TV";
            Width = 382;
            Height = 604;
            MinWidth = 382;
            MaxWidth = 382;
            MinHeight = 604;
            MaxHeight = 604;
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ShowInTaskbar = false;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            var menu = new Forms.ContextMenuStrip();
            menu.Items.Add("Abrir controle", null, delegate { Dispatcher.BeginInvoke((Action)ShowWindow); });
            menu.Items.Add("Silenciar / ativar som", null, delegate { RunAction("Mudo", delegate { SendKey("KEY_MUTE"); }); });
            menu.Items.Add("Desligar TV", null, delegate { RunAction("Desligar", PowerOff); });
            menu.Items.Add(new Forms.ToolStripSeparator());
            menu.Items.Add("Sair", null, delegate { allowClose = true; tray.Visible = false; Close(); });
            tray = new Forms.NotifyIcon();
            try { tray.Icon = DrawingIcon.ExtractAssociatedIcon(System.Reflection.Assembly.GetExecutingAssembly().Location); }
            catch { tray.Icon = DrawingSystemIcons.Application; }
            tray.Text = "Controle Samsung TV";
            tray.ContextMenuStrip = menu;
            tray.Visible = true;
            tray.DoubleClick += delegate { ShowWindow(); };

            var shell = new Border { CornerRadius = new CornerRadius(21), Background = Brush("#0b111b"),
                BorderBrush = Brush("#243146"), BorderThickness = new Thickness(1), Padding = new Thickness(16) };
            var layout = new Grid();
            layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            shell.Child = layout;

            var header = new Grid { Margin = new Thickness(2, 0, 0, 12), Cursor = Cursors.SizeAll };
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(38) });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var logo = new Border { Width = 34, Height = 34, CornerRadius = new CornerRadius(12), Background = Brush("#254b72"),
                Child = Text("⌂", 20, White, true, HorizontalAlignment.Center) };
            header.Children.Add(logo);
            var brand = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
            brand.Children.Add(Text("Smart Tray", 15, White, true));
            brand.Children.Add(Text("SAMSUNG TV  ·  SALA", 8, Cyan, true));
            Grid.SetColumn(brand, 1); header.Children.Add(brand);
            var windowButtons = new StackPanel { Orientation = Orientation.Horizontal };
            windowButtons.Children.Add(IconButton("⚙", delegate { SelectPage("controle"); }, 32));
            windowButtons.Children.Add(IconButton("—", HideWindow, 32));
            windowButtons.Children.Add(IconButton("×", HideWindow, 32));
            Grid.SetColumn(windowButtons, 2); header.Children.Add(windowButtons);
            header.MouseLeftButtonDown += delegate { try { DragMove(); } catch { } };
            Grid.SetRow(header, 0); layout.Children.Add(header);

            var nav = new UniformGrid { Columns = 3, Margin = new Thickness(0, 0, 0, 11) };
            tabs["tv"] = TabButton("TV", "tv", nav);
            tabs["apps"] = TabButton("Apps", "apps", nav);
            tabs["controle"] = TabButton("Controle", "controle", nav);
            Grid.SetRow(nav, 1); layout.Children.Add(nav);

            pages = new StackPanel { VerticalAlignment = VerticalAlignment.Stretch };
            BuildHome(); BuildApps(); BuildRemote(); SelectPage("tv");
            var scroller = new ScrollViewer { Content = pages, VerticalScrollBarVisibility = ScrollBarVisibility.Hidden,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Padding = new Thickness(0, 0, 2, 0) };
            Grid.SetRow(scroller, 2); layout.Children.Add(scroller);

            var footer = new StackPanel { Margin = new Thickness(3, 8, 3, 0) };
            var startup = new CheckBox { Content = "Iniciar com o Windows", IsChecked = StartupEnabled(),
                Foreground = Brush("#a6b1c2"), FontSize = 9, VerticalContentAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 4) };
            startup.Checked += delegate { SetStartup(true); }; startup.Unchecked += delegate { SetStartup(false); };
            footer.Children.Add(startup);
            messageLabel = Text("Pronto  ·  Fechar minimiza na bandeja", 9, Muted);
            footer.Children.Add(messageLabel);
            Grid.SetRow(footer, 3); layout.Children.Add(footer);
            Content = shell;

            StateChanged += delegate { tray.Visible = true; };
            Closing += delegate(object sender, System.ComponentModel.CancelEventArgs e) {
                if (!allowClose) { e.Cancel = true; HideWindow(); }
                else tray.Visible = false;
            };
            pollTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
            pollTimer.Tick += delegate { RefreshStatus(); };
            volumePollTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            volumePollTimer.Tick += delegate { RefreshVolume(); };
            Loaded += delegate { if (startHidden) HideWindow(); else RefreshStatus(); pollTimer.Start(); volumePollTimer.Start(); RefreshVolume(); };
        }

        private void BuildHome()
        {
            var page = new StackPanel { Tag = "tv" };
            var card = new Border { Background = new LinearGradientBrush(Brush("#1b3045").Color, CardColor, 90),
                BorderBrush = Brush("#2b4157"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(18), Padding = new Thickness(14) };
            var body = new StackPanel(); card.Child = body;
            var top = new Grid();
            top.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var status = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            status.Children.Add(Text("●", 12, Cyan, true));
            stateLabel = Text("Conectando…", 13, White, true); status.Children.Add(stateLabel);
            Grid.SetColumn(status, 0); top.Children.Add(status);
            var power = Button("⏻", delegate { RunAction("Ligar / desligar", TogglePower); }, "#55c9dc", "#09202a", 42, 14);
            Grid.SetColumn(power, 1); top.Children.Add(power); body.Children.Add(top);
            modelLabel = Text("Samsung Smart TV", 10, Muted); modelLabel.Margin = new Thickness(0, 1, 0, 8); body.Children.Add(modelLabel);

            var tvArt = new Border { Height = 132, Background = new LinearGradientBrush(Brush("#142b3e").Color, Brush("#0e1b2a").Color, 90),
                CornerRadius = new CornerRadius(14), Margin = new Thickness(0, 0, 0, 8) };
            var art = new Grid(); tvArt.Child = art;
            var halo = new System.Windows.Shapes.Ellipse { Width = 205, Height = 150, Fill = Brush("#18364d"), Opacity = .65,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            art.Children.Add(halo);
            var tv = new StackPanel { Width = 218, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            var screen = new Border { Height = 84, CornerRadius = new CornerRadius(8), Background = Brush("#07111d"),
                BorderBrush = Brush("#72dce8"), BorderThickness = new Thickness(2),
                Child = Text("SAMSUNG", 15, Brush("#d6eaf4").Color, true, HorizontalAlignment.Center) };
            tv.Children.Add(screen);
            var stand = new Grid { Height = 12 };
            stand.Children.Add(new System.Windows.Shapes.Rectangle { Width = 47, Height = 4, Fill = Brush("#72dce8"), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top });
            stand.Children.Add(new System.Windows.Shapes.Rectangle { Width = 75, Height = 4, Fill = Brush("#72dce8"), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom });
            tv.Children.Add(stand);
            // Build visual artwork in its host grid.
            art.Children.Clear(); art.Children.Add(halo); art.Children.Add(tv);
            body.Children.Add(tvArt);

            var now = new StackPanel { Margin = new Thickness(0, 0, 0, 7) };
            now.Children.Add(Text("TOCANDO AGORA", 9, Muted, true));
            contentLabel = Text("Conteúdo não identificado", 14, White, true); contentLabel.Margin = new Thickness(0, 3, 0, 1); now.Children.Add(contentLabel);
            contentHint = Text("Os apps nem sempre disponibilizam o título.", 10, Muted); now.Children.Add(contentHint);
            body.Children.Add(now);

            var volume = new StackPanel { Margin = new Thickness(0, 0, 0, 9) };
            var volumeHeader = new Grid();
            volumeHeader.Children.Add(Text("VOLUME", 10, Muted, true));
            volumeLabel = Text("—", 11, White, true, HorizontalAlignment.Right); volumeHeader.Children.Add(volumeLabel);
            volume.Children.Add(volumeHeader);
            volumeTrack = new Canvas { Height = 29, Margin = new Thickness(0, 3, 0, 0), Cursor = Cursors.Hand, ClipToBounds = false, Background = Brushes.Transparent };
            var trackBack = new Border { Height = 8, CornerRadius = new CornerRadius(5), Background = Brush("#273449"), IsHitTestVisible = false };
            Canvas.SetTop(trackBack, 10);
            volumeTrack.Children.Add(trackBack);
            volumeFill = new Border { Width = 0, Height = 8, HorizontalAlignment = HorizontalAlignment.Left,
                CornerRadius = new CornerRadius(5), IsHitTestVisible = false,
                Background = new LinearGradientBrush(Brush("#5595ff").Color, Brush("#65d9e8").Color, 0) };
            Canvas.SetTop(volumeFill, 10);
            volumeTrack.Children.Add(volumeFill);
            volumeThumb = new Ellipse { Width = 20, Height = 20, Fill = Brush("#d9fbff"), Stroke = Brush("#65d9e8"), StrokeThickness = 2 };
            Canvas.SetTop(volumeThumb, 4);
            volumeTrack.Children.Add(volumeThumb);
            volumeTrack.SizeChanged += delegate { SetVolumeFill(false); };
            volumeTrack.MouseLeftButtonDown += delegate(object sender, MouseButtonEventArgs e) { volumeDragging = true; volumeTrack.CaptureMouse(); UpdateVolumeFromPointer(e.GetPosition(volumeTrack).X); };
            volumeTrack.MouseMove += delegate(object sender, MouseEventArgs e) { if (volumeDragging && e.LeftButton == MouseButtonState.Pressed) UpdateVolumeFromPointer(e.GetPosition(volumeTrack).X); };
            volumeTrack.MouseLeftButtonUp += delegate(object sender, MouseButtonEventArgs e) { if (volumeDragging) { UpdateVolumeFromPointer(e.GetPosition(volumeTrack).X); volumeDragging = false; volumeTrack.ReleaseMouseCapture(); ApplyVolume(); } };
            volume.Children.Add(volumeTrack); body.Children.Add(volume);

            var actions = new UniformGrid { Columns = 4, Margin = new Thickness(-3, 0, -3, 8) };
            AddKeyTile(actions, "◖  Mudo", "KEY_MUTE"); AddKeyTile(actions, "⌂  Início", "KEY_HOME");
            AddKeyTile(actions, "▣  Fonte", "KEY_SOURCE"); AddKeyTile(actions, "☷  Guia", "KEY_GUIDE");
            body.Children.Add(actions);
            var services = new UniformGrid { Columns = 3, Margin = new Thickness(-2, 0, -2, 0) };
            AddService(services, "Netflix", "#e50914"); AddService(services, "Prime Video", "#0879d1"); AddService(services, "Globoplay", "#ed6a2c");
            body.Children.Add(services);
            AddPage(page, card);
        }

        private void BuildApps()
        {
            var page = new StackPanel { Tag = "apps" };
            var card = Card();
            card.Children.Add(Text("Aplicativos", 16, White, true));
            var hint = Text("Abrir direto na TV", 9, Muted); hint.Margin = new Thickness(0, 2, 0, 10); card.Children.Add(hint);
            AddAppRow(card, "Netflix", "#e50914"); AddAppRow(card, "Prime Video", "#0879d1");
            AddAppRow(card, "Globoplay", "#ed6a2c"); AddAppRow(card, "YouTube", "#ff2020"); AddAppRow(card, "Disney+", "#173aa8");
            AddAppRow(card, "Samsung TV Plus", "#142a49"); AddAppRow(card, "Twitch", "#6541c8");
            var note = Text("IDs dos aplicativos variam conforme o modelo e a região da TV.", 9, Muted); note.Margin = new Thickness(0, 10, 0, 0); card.Children.Add(note);
            AddPage(page, card);
        }

        private void BuildRemote()
        {
            var page = new StackPanel { Tag = "controle" };
            var card = Card();
            var header = new Grid(); header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var title = Text("Controle remoto", 16, White, true); Grid.SetColumn(title, 0); header.Children.Add(title);
            var back = Button("Voltar", delegate { RunAction("Voltar", delegate { SendKey("KEY_RETURN"); }); }, "#1c2b40", Muted, 72, 9); Grid.SetColumn(back, 1); header.Children.Add(back); card.Children.Add(header);
            var pad = new Grid { HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 10, 0, 10) };
            for (int i = 0; i < 3; i++) { pad.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); pad.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); }
            AddDpad(pad, "▲", "KEY_UP", 0, 1); AddDpad(pad, "◀", "KEY_LEFT", 1, 0); AddDpad(pad, "OK", "KEY_ENTER", 1, 1, true); AddDpad(pad, "▶", "KEY_RIGHT", 1, 2); AddDpad(pad, "▼", "KEY_DOWN", 2, 1);
            card.Children.Add(pad);
            var keys = new UniformGrid { Columns = 3, Margin = new Thickness(-2, 1, -2, 4) };
            string[,] grid = { { "Mudo", "KEY_MUTE" }, { "VOL +", "KEY_VOLUP" }, { "VOL −", "KEY_VOLDOWN" }, { "CH +", "KEY_CHUP" }, { "CH −", "KEY_CHDOWN" }, { "Menu", "KEY_MENU" }, { "Fonte", "KEY_SOURCE" }, { "Info", "KEY_INFO" }, { "Config", "KEY_SETTINGS" }, { "Lista CH", "KEY_CH_LIST" }, { "Guia", "KEY_GUIDE" }, { "Exit", "KEY_EXIT" } };
            for (int i = 0; i < grid.GetLength(0); i++) keys.Children.Add(Button(grid[i, 0], delegate { }, "#1a273a", White, 0, 9));
            for (int i = 0; i < grid.GetLength(0); i++) { var b = (Button)keys.Children[i]; string key = grid[i, 1]; b.Click += delegate { RunAction(key, delegate { SendKey(key); }); }; }
            card.Children.Add(keys);
            var media = new UniformGrid { Columns = 5, Margin = new Thickness(-2, 3, -2, 0) };
            AddMedia(media, "⏮", "KEY_REWIND"); AddMedia(media, "▶", "KEY_PLAY"); AddMedia(media, "Ⅱ", "KEY_PAUSE"); AddMedia(media, "⏭", "KEY_FF"); AddMedia(media, "■", "KEY_STOP");
            card.Children.Add(media);
            AddPage(page, card);
        }

        private void AddPage(UIElement page, UIElement child)
        {
            var content = child;
            if (child is StackPanel && ((FrameworkElement)child).Tag as string == "card")
                content = new Border { Background = Brush("#111c2a"), BorderBrush = Brush("#26374b"), BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(17), Padding = new Thickness(13), Child = child };
            ((Panel)page).Children.Add(content); pages.Children.Add(page); pageMap[(string)((FrameworkElement)page).Tag] = page;
        }

        private StackPanel Card()
        {
            return new StackPanel { Background = Brushes.Transparent, Margin = new Thickness(1), Tag = "card" };
        }

        private Button TabButton(string text, string key, Panel parent)
        {
            var b = Button(text, delegate { SelectPage(key); }, "#192537", White, 0, 10);
            b.Margin = new Thickness(3, 0, 3, 0); b.Height = 34; parent.Children.Add(b); return b;
        }

        private void SelectPage(string key)
        {
            foreach (var entry in pageMap) entry.Value.Visibility = entry.Key == key ? Visibility.Visible : Visibility.Collapsed;
            foreach (var entry in tabs) { entry.Value.Background = Brush(entry.Key == key ? "#65d9e8" : "#192537"); entry.Value.Foreground = Brush(entry.Key == key ? "#08151c" : "#f0f6ff"); }
        }

        private void AddKeyTile(Panel panel, string label, string key)
        {
            var b = Button(label, delegate { RunAction(label, delegate { SendKey(key); }); }, "#1a293d", White, 0, 9);
            b.Margin = new Thickness(3); b.Height = 42; panel.Children.Add(b);
        }

        private void AddService(Panel panel, string label, string color)
        {
            var b = Button(label, delegate { OpenApp(label); }, color, White, 0, 10);
            b.Margin = new Thickness(3, 2, 3, 2); b.Height = 34; panel.Children.Add(b);
        }

        private void AddAppRow(Panel panel, string name, string color)
        {
            var row = new Grid { Background = Brush("#192638"), Margin = new Thickness(0, 3, 0, 3), Height = 47 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var label = Text(name, 12, White, true); label.Margin = new Thickness(10, 0, 0, 0); label.VerticalAlignment = VerticalAlignment.Center; Grid.SetColumn(label, 0); row.Children.Add(label);
            var action = Button("Abrir  ›", delegate { OpenApp(name); }, "#111c2a", Muted, 72, 9); action.Margin = new Thickness(0, 5, 6, 5); Grid.SetColumn(action, 1); row.Children.Add(action);
            panel.Children.Add(row);
        }

        private void AddDpad(Grid panel, string text, string key, int r, int c, bool selected = false)
        {
            var b = Button(text, delegate { RunAction(text, delegate { SendKey(key); }); }, selected ? "#65d9e8" : "#1b2a3e", selected ? Color("#07151d") : White, selected ? 51 : 43, 12);
            b.Margin = new Thickness(3); b.Height = selected ? 48 : 40; Grid.SetRow(b, r); Grid.SetColumn(b, c); panel.Children.Add(b);
        }

        private void AddMedia(Panel panel, string label, string key)
        {
            var b = Button(label, delegate { RunAction(label, delegate { SendKey(key); }); }, "#1b2a3e", White, 0, 13); b.Margin = new Thickness(3); b.Height = 38; panel.Children.Add(b);
        }

        private Button IconButton(string label, Action action, int size)
        {
            var b = Button(label, action, "#141f30", Muted, size, 13); b.Height = size; b.Margin = new Thickness(3, 0, 0, 0); return b;
        }

        private Button Button(string label, Action action, string back, MediaColor fore, int width, double size)
        {
            var b = new Button { Content = label, Background = Brush(back), Foreground = new SolidColorBrush(fore), BorderThickness = new Thickness(0),
                FontSize = size, FontWeight = FontWeights.SemiBold, Padding = new Thickness(8, 5, 8, 5), Cursor = Cursors.Hand,
                Focusable = false, Width = width == 0 ? double.NaN : width, Height = 34 };
            var root = new FrameworkElementFactory(typeof(Border)); root.SetValue(Border.CornerRadiusProperty, new CornerRadius(11));
            root.SetBinding(Border.BackgroundProperty, new System.Windows.Data.Binding("Background") { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
            var presenter = new FrameworkElementFactory(typeof(ContentPresenter)); presenter.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            presenter.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center); presenter.SetValue(ContentPresenter.MarginProperty, new Thickness(5, 2, 5, 2)); root.AppendChild(presenter);
            b.Template = new ControlTemplate(typeof(Button)) { VisualTree = root };
            b.Click += delegate { if (action != null) action(); };
            b.MouseEnter += delegate { b.Opacity = .88; }; b.MouseLeave += delegate { b.Opacity = 1; };
            return b;
        }

        private Button Button(string label, Action action, string back, string fore, int width, double size)
        {
            return Button(label, action, back, Color(fore), width, size);
        }

        private static TextBlock Text(string value, double size, MediaColor color, bool bold = false, HorizontalAlignment alignment = HorizontalAlignment.Left)
        {
            return new TextBlock { Text = value, FontFamily = new System.Windows.Media.FontFamily("Segoe UI"), FontSize = size,
                FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal, Foreground = new SolidColorBrush(color),
                VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = alignment, TextTrimming = TextTrimming.CharacterEllipsis };
        }

        private static SolidColorBrush Brush(string hex) { return new SolidColorBrush(Color(hex)); }
        private static MediaColor Color(string hex) { return (MediaColor)System.Windows.Media.ColorConverter.ConvertFromString(hex); }

        private static string ReadSetting(string key, string fallback)
        {
            string fromEnvironment = Environment.GetEnvironmentVariable(key);
            if (!String.IsNullOrWhiteSpace(fromEnvironment)) return fromEnvironment.Trim().Trim('"', '\'');

            var starts = new List<string>();
            string executableDirectory = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
            if (!String.IsNullOrWhiteSpace(executableDirectory)) starts.Add(executableDirectory);
            starts.Add(Environment.CurrentDirectory);
            var checkedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string start in starts)
            {
                DirectoryInfo directory;
                try { directory = new DirectoryInfo(start); } catch { continue; }
                while (directory != null)
                {
                    string path = Path.Combine(directory.FullName, ".env");
                    if (checkedPaths.Add(path) && File.Exists(path))
                    {
                        foreach (string line in File.ReadAllLines(path))
                        {
                            string trimmed = line.Trim().TrimStart('\uFEFF');
                            if (trimmed.StartsWith("#") || !trimmed.Contains("=")) continue;
                            int split = trimmed.IndexOf('=');
                            string setting = trimmed.Substring(0, split).Trim();
                            if (setting.StartsWith("export ", StringComparison.OrdinalIgnoreCase)) setting = setting.Substring(7).Trim();
                            if (!String.Equals(setting, key, StringComparison.OrdinalIgnoreCase)) continue;
                            string value = trimmed.Substring(split + 1).Trim().Trim('"', '\'');
                            if (!String.IsNullOrWhiteSpace(value)) return value;
                        }
                    }
                    directory = directory.Parent;
                }
            }
            return fallback;
        }

        private string StatusJson()
        {
            if (String.IsNullOrWhiteSpace(ip)) throw new InvalidOperationException("Configure o IP da TV no arquivo .env.");
            var request = (HttpWebRequest)WebRequest.Create("https://" + ip + ":8002/api/v2/"); request.Timeout = 3500; request.ServerCertificateValidationCallback = delegate { return true; };
            using (var response = (HttpWebResponse)request.GetResponse()) using (var reader = new StreamReader(response.GetResponseStream())) return reader.ReadToEnd();
        }

        private void RefreshStatus()
        {
            if (statusReadPending) return;
            statusReadPending = true;
            Task.Run(delegate
            {
                try
                {
                    string json = StatusJson();
                    string power = Match(json, "PowerState"); string model = Match(json, "modelName");
                    string currentApp = FindRunningApp();
                    string smartThingsValue = ReadSmartThingsChannel();
                    bool smartThingsReturnedAppId = LooksLikeAppId(smartThingsValue);
                    string currentChannel = smartThingsReturnedAppId ? "" : smartThingsValue;
                    Dispatcher.BeginInvoke((Action)delegate {
                        stateLabel.Text = power == "on" ? "TV ligada" : power == "standby" ? "TV em espera" : "TV conectada";
                        modelLabel.Text = String.IsNullOrEmpty(model) ? "Samsung Smart TV" : model;
                        if (!String.IsNullOrWhiteSpace(currentChannel))
                            contentLabel.Text = String.IsNullOrWhiteSpace(currentApp) ? "Canal: " + currentChannel : currentApp + " · " + currentChannel;
                        else
                            contentLabel.Text = String.IsNullOrEmpty(currentApp) ? "App em execução não identificado" : "Aplicativo aberto: " + currentApp;
                        contentHint.Text = smartThingsReturnedAppId
                            ? "SmartThings retornou o ID de um app, não um canal; usando o app visível na TV."
                            : String.IsNullOrWhiteSpace(smartThingsToken) || String.IsNullOrWhiteSpace(smartThingsDeviceId)
                                ? "Configure SmartThings no .env para mostrar o canal atual."
                                : "App e canal atual; o título do programa depende dos dados da TV.";
                    });
                }
                catch { Dispatcher.BeginInvoke((Action)delegate { stateLabel.Text = "TV indisponível"; modelLabel.Text = "Confira a conexão e o IP"; contentLabel.Text = "Conteúdo não identificado"; contentHint.Text = "A conexão local não retornou o app atual."; }); }
                finally { Dispatcher.BeginInvoke((Action)delegate { statusReadPending = false; }); }
            });
        }

        private string FindRunningApp()
        {
            var apps = new List<KeyValuePair<string, string>> {
                new KeyValuePair<string, string>("Netflix", "3201907018807"), new KeyValuePair<string, string>("Prime Video", "3201910019365"),
                new KeyValuePair<string, string>("Globoplay", "3201908019022"), new KeyValuePair<string, string>("YouTube", "111299001912"),
                new KeyValuePair<string, string>("Disney+", "3201901017640"), new KeyValuePair<string, string>("Twitch", "3202203026841"),
                new KeyValuePair<string, string>("Samsung TV Plus", "3202405035480"),
                new KeyValuePair<string, string>("Samsung TV Plus", "com.samsung.tv.tvPlusDeeplinker"),
                new KeyValuePair<string, string>("Samsung TV Plus", "com.samsung.tv.tvplus")
            };
            var requests = new List<Task<KeyValuePair<string, string>>>();
            foreach (var app in apps)
            {
                string name = app.Key, id = app.Value;
                requests.Add(Task.Factory.StartNew(delegate { return ReadAppStatus(name, id); }));
            }
            Task.WaitAll(requests.ToArray());
            var visibleApps = new List<string>();
            foreach (var request in requests)
            {
                string json = request.Result.Value;
                if (IsStatusTrue(json, "visible") && !visibleApps.Contains(request.Result.Key)) visibleApps.Add(request.Result.Key);
            }
            if (visibleApps.Count == 1) return visibleApps[0];
            return "";
        }

        private KeyValuePair<string, string> ReadAppStatus(string name, string id)
        {
            try
            {
                var request = (HttpWebRequest)WebRequest.Create("https://" + ip + ":8002/api/v2/applications/" + id);
                request.Timeout = 2500; request.ServerCertificateValidationCallback = delegate { return true; };
                using (var response = (HttpWebResponse)request.GetResponse())
                using (var reader = new StreamReader(response.GetResponseStream())) return new KeyValuePair<string, string>(name, reader.ReadToEnd());
            }
            catch { return new KeyValuePair<string, string>(name, ""); }
        }

        private static bool IsStatusTrue(string json, string key)
        {
            return Regex.IsMatch(json ?? "", "\\\"" + Regex.Escape(key) + "\\\"\\s*:\\s*(?:true|\\\"true\\\")", RegexOptions.IgnoreCase);
        }

        private string ReadSmartThingsChannel()
        {
            if (String.IsNullOrWhiteSpace(smartThingsToken) || String.IsNullOrWhiteSpace(smartThingsDeviceId)) return "";
            try
            {
                string url = "https://api.smartthings.com/v1/devices/" + Uri.EscapeDataString(smartThingsDeviceId) +
                    "/components/main/capabilities/tvChannel/status";
                var request = (HttpWebRequest)WebRequest.Create(url);
                request.Method = "GET"; request.Timeout = 3500; request.Accept = "application/json";
                request.Headers[HttpRequestHeader.Authorization] = "Bearer " + smartThingsToken;
                using (var response = (HttpWebResponse)request.GetResponse())
                using (var reader = new StreamReader(response.GetResponseStream()))
                {
                    string json = reader.ReadToEnd();
                    var match = Regex.Match(json, "\\\"tvChannelName\\\"\\s*:\\s*\\{\\s*\\\"value\\\"\\s*:\\s*\\\"((?:\\\\.|[^\\\"\\\\])*)\\\"", RegexOptions.IgnoreCase);
                    return match.Success ? match.Groups[1].Value.Replace("\\\\\"", "\\\"").Replace("\\\\\\\\", "\\\\") : "";
                }
            }
            catch { return ""; }
        }

        private static bool LooksLikeAppId(string value)
        {
            return !String.IsNullOrWhiteSpace(value) && (value.StartsWith("org.tizen.", StringComparison.OrdinalIgnoreCase) ||
                value.StartsWith("com.samsung.", StringComparison.OrdinalIgnoreCase));
        }

        private void UpdateVolumeFromPointer(double x)
        {
            if (volumeTrack == null || volumeTrack.ActualWidth <= 0) return;
            volumeLevel = (int)Math.Round(Math.Max(0, Math.Min(volumeTrack.ActualWidth, x)) * 100.0 / volumeTrack.ActualWidth);
            volumeLabel.Text = volumeLevel.ToString() + "%";
            SetVolumeFill(false);
        }

        private void SetVolumeFill(bool animate)
        {
            if (volumeTrack == null || volumeTrack.ActualWidth <= 0) return;
            double width = volumeTrack.ActualWidth;
            var background = volumeTrack.Children.Count > 0 ? volumeTrack.Children[0] as FrameworkElement : null;
            if (background != null) background.Width = width;
            double target = width * volumeLevel / 100.0;
            if (animate)
            {
                volumeFill.BeginAnimation(FrameworkElement.WidthProperty, new DoubleAnimation(target, TimeSpan.FromMilliseconds(220)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
                volumeThumb.BeginAnimation(Canvas.LeftProperty, new DoubleAnimation(Math.Max(0, target - volumeThumb.Width / 2), TimeSpan.FromMilliseconds(220)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
            }
            else
            {
                volumeFill.BeginAnimation(FrameworkElement.WidthProperty, null);
                Canvas.SetLeft(volumeThumb, Math.Max(0, Math.Min(width - volumeThumb.Width, target - volumeThumb.Width / 2)));
                volumeFill.Width = target;
            }
        }

        private void ApplyVolume()
        {
            int desired = volumeLevel;
            RunAction("Volume " + desired + "%", delegate { Soap("SetVolume", "<DesiredVolume>" + desired + "</DesiredVolume>"); });
        }

        private void RefreshVolume()
        {
            if (volumeReadPending || volumeDragging || String.IsNullOrWhiteSpace(ip)) return;
            volumeReadPending = true;
            Task.Run(delegate
            {
                int current = -1;
                try { string response = Soap("GetVolume", ""); var match = Regex.Match(response, "<CurrentVolume>(\\d+)</CurrentVolume>"); if (match.Success) current = Int32.Parse(match.Groups[1].Value); }
                catch { }
                Dispatcher.BeginInvoke((Action)delegate
                {
                    volumeReadPending = false;
                    if (current >= 0 && !volumeDragging) { volumeLevel = Math.Max(0, Math.Min(100, current)); volumeLabel.Text = volumeLevel.ToString() + "%"; SetVolumeFill(true); }
                });
            });
        }

        private string Soap(string action, string arguments)
        {
            string service = "urn:schemas-upnp-org:service:RenderingControl:1";
            string body = "<?xml version=\"1.0\" encoding=\"utf-8\"?><s:Envelope xmlns:s=\"http://schemas.xmlsoap.org/soap/envelope/\" s:encodingStyle=\"http://schemas.xmlsoap.org/soap/encoding/\"><s:Body><u:" + action + " xmlns:u=\"" + service + "\"><InstanceID>0</InstanceID><Channel>Master</Channel>" + arguments + "</u:" + action + "></s:Body></s:Envelope>";
            byte[] data = Encoding.UTF8.GetBytes(body);
            var request = (HttpWebRequest)WebRequest.Create("http://" + ip + ":9197/upnp/control/RenderingControl1");
            request.Method = "POST"; request.Timeout = 3000; request.ContentType = "text/xml; charset=\"utf-8\"";
            request.Headers.Add("SOAPACTION", "\"" + service + "#" + action + "\"");
            request.ContentLength = data.Length;
            using (var stream = request.GetRequestStream()) stream.Write(data, 0, data.Length);
            using (var response = (HttpWebResponse)request.GetResponse()) using (var reader = new StreamReader(response.GetResponseStream())) return reader.ReadToEnd();
        }

        private static string Match(string json, string key)
        {
            var match = Regex.Match(json, "\"" + Regex.Escape(key) + "\"\\s*:\\s*\"([^\"]*)\"", RegexOptions.IgnoreCase);
            return match.Success ? match.Groups[1].Value : "";
        }

        private void RunAction(string label, Action action)
        {
            if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke((Action)delegate { RunAction(label, action); }); return; }
            if (busy) return; busy = true; messageLabel.Text = "Enviando: " + label + "…";
            Task.Run(delegate
            {
                string result;
                try { action(); result = "Enviado  ·  " + label; }
                catch (Exception ex) { result = "Falha: " + ex.Message; }
                Dispatcher.BeginInvoke((Action)delegate { messageLabel.Text = result; busy = false; RefreshStatus(); });
            });
        }

        private void TogglePower()
        {
            try { string json = StatusJson(); if (Match(json, "PowerState") == "on") PowerOff(); else Wake(); }
            catch (WebException) { Wake(); }
        }

        private void PowerOff()
        {
            try { string json = StatusJson(); if (Match(json, "PowerState") != "on") throw new InvalidOperationException("A TV já está em espera."); }
            catch (WebException) { throw new InvalidOperationException("Não consegui alcançar a TV. Ela pode estar desligada."); }
            SendKey("KEY_POWER");
        }

        private void Wake()
        {
            string macText = ReadSetting("MAC_TV", "");
            if (String.IsNullOrWhiteSpace(macText)) throw new InvalidOperationException("Configure MAC_TV no arquivo .env para usar Wake-on-LAN.");
            string compactMac = Regex.Replace(macText, "[:-]", "");
            if (!Regex.IsMatch(compactMac, "^[0-9a-fA-F]{12}$")) throw new InvalidOperationException("O valor MAC_TV no .env não é um MAC válido.");
            byte[] mac = PhysicalAddress.Parse(compactMac).GetAddressBytes();
            byte[] packet = new byte[102]; for (int i = 0; i < 6; i++) packet[i] = 0xff;
            for (int i = 1; i <= 16; i++) Buffer.BlockCopy(mac, 0, packet, i * 6, 6);
            string broadcast = ReadSetting("IP_BROADCAST", "255.255.255.255");
            using (var udp = new UdpClient()) { udp.EnableBroadcast = true; udp.Send(packet, packet.Length, new IPEndPoint(IPAddress.Parse(broadcast), 9)); }
        }

        private void OpenApp(string name)
        {
            RunAction("Abrir " + name, delegate
            {
                var ids = new Dictionary<string, string[]> {
                    { "Netflix", new[] { "3201907018807", "11101200001" } }, { "Prime Video", new[] { "3201910019365", "3201512006785" } },
                    { "Globoplay", new[] { "3201908019022" } }, { "YouTube", new[] { "111299001912" } }, { "Disney+", new[] { "3201901017640" } },
                    { "Twitch", new[] { "3202203026841" } }
                };
                if (name == "Samsung TV Plus") { SendKey("KEY_TV"); return; }
                if (!ids.ContainsKey(name)) throw new InvalidOperationException("Aplicativo desconhecido.");
                try
                {
                    string status = StatusJson();
                    if (Match(status, "PowerState") != "on") { Wake(); Thread.Sleep(11000); }
                }
                catch (WebException) { Wake(); Thread.Sleep(11000); }
                foreach (string id in ids[name])
                {
                    try { var request = (HttpWebRequest)WebRequest.Create("http://" + ip + ":8001/api/v2/applications/" + id); request.Method = "POST"; request.Timeout = 5000; using (request.GetResponse()) { } return; }
                    catch (WebException) { }
                }
                throw new InvalidOperationException("Não consegui abrir. Confira se o app está instalado na TV.");
            });
        }

        private void SendKey(string key)
        {
            if (String.IsNullOrWhiteSpace(ip)) throw new InvalidOperationException("Configure o IP da TV no arquivo .env.");
            string name = Convert.ToBase64String(Encoding.UTF8.GetBytes("Controle-Samsung-Windows"));
            string path = "/api/v2/channels/samsung.remote.control?name=" + name;
            if (!String.IsNullOrWhiteSpace(token)) path += "&token=" + token;
            using (var tcp = new TcpClient())
            {
                var connect = tcp.ConnectAsync(ip, 8002);
                if (!connect.Wait(7000)) throw new TimeoutException("Tempo esgotado ao conectar à TV.");
                using (var stream = new SslStream(tcp.GetStream(), false, delegate { return true; }))
                {
                    stream.ReadTimeout = 7000; stream.WriteTimeout = 7000;
                    stream.AuthenticateAsClient(ip, null, SslProtocols.Tls12, false);
                    byte[] nonce = new byte[16]; using (var random = new RNGCryptoServiceProvider()) random.GetBytes(nonce);
                    string wsKey = Convert.ToBase64String(nonce);
                    string request = "GET " + path + " HTTP/1.1\r\n" +
                        "Host: " + ip + ":8002\r\nUpgrade: websocket\r\nConnection: Upgrade\r\n" +
                        "Sec-WebSocket-Key: " + wsKey + "\r\nSec-WebSocket-Version: 13\r\n" +
                        "Origin: http://" + ip + ":8002\r\nUser-Agent: Python-websocket-client\r\n\r\n";
                    byte[] requestBytes = Encoding.ASCII.GetBytes(request);
                    stream.Write(requestBytes, 0, requestBytes.Length); stream.Flush();
                    string headers = ReadHttpHeaders(stream);
                    if (!headers.StartsWith("HTTP/1.1 101", StringComparison.Ordinal))
                        throw new InvalidOperationException("A TV recusou a conexão WebSocket. Verifique a autorização do controle na TV.");

                    bool connected = false;
                    DateTime deadline = DateTime.UtcNow.AddSeconds(7);
                    while (DateTime.UtcNow < deadline)
                    {
                        WsFrame frame = ReadWebSocketFrame(stream);
                        if (frame.Opcode == 8) throw new InvalidOperationException("A TV encerrou a conexão do controle.");
                        if (frame.Opcode == 9) { WriteWebSocketFrame(stream, 10, frame.Payload); continue; }
                        if (frame.Opcode != 1) continue;
                        string message = Encoding.UTF8.GetString(frame.Payload);
                        if (message.Contains("ms.channel.connect")) { connected = true; break; }
                        if (message.Contains("ms.channel.unauthorized"))
                            throw new InvalidOperationException("A TV recusou o token. Apague TOKEN_TV do .env e aprove a nova conexão na tela da TV.");
                    }
                    if (!connected) throw new TimeoutException("A TV não confirmou a conexão do controle.");
                    string payload = "{\"method\":\"ms.remote.control\",\"params\":{\"Cmd\":\"Click\",\"DataOfCmd\":\"" + key + "\",\"Option\":\"false\",\"TypeOfRemote\":\"SendRemoteKey\"}}";
                    WriteWebSocketFrame(stream, 1, Encoding.UTF8.GetBytes(payload));
                }
            }
        }

        private static string ReadHttpHeaders(Stream stream)
        {
            var header = new MemoryStream(); int state = 0;
            while (header.Length < 8192 && state < 4)
            {
                int value = stream.ReadByte();
                if (value < 0) throw new EndOfStreamException("A TV fechou o handshake WebSocket.");
                header.WriteByte((byte)value);
                state = state == 0 && value == 13 ? 1 : state == 1 && value == 10 ? 2 : state == 2 && value == 13 ? 3 : state == 3 && value == 10 ? 4 : 0;
            }
            if (state != 4) throw new InvalidOperationException("Resposta WebSocket inválida da TV.");
            return Encoding.ASCII.GetString(header.ToArray());
        }

        private sealed class WsFrame { public int Opcode; public byte[] Payload; }

        private static int ReadRequiredByte(Stream stream)
        {
            int value = stream.ReadByte();
            if (value < 0) throw new EndOfStreamException("A TV encerrou a conexão WebSocket.");
            return value;
        }

        private static byte[] ReadExact(Stream stream, int count)
        {
            byte[] data = new byte[count]; int offset = 0;
            while (offset < count) { int read = stream.Read(data, offset, count - offset); if (read <= 0) throw new EndOfStreamException(); offset += read; }
            return data;
        }

        private static WsFrame ReadWebSocketFrame(Stream stream)
        {
            int first = ReadRequiredByte(stream), second = ReadRequiredByte(stream);
            int opcode = first & 15; bool masked = (second & 128) != 0; ulong length = (uint)(second & 127);
            if (length == 126) length = (uint)((ReadRequiredByte(stream) << 8) | ReadRequiredByte(stream));
            else if (length == 127) { length = 0; for (int i = 0; i < 8; i++) length = (length << 8) | (uint)ReadRequiredByte(stream); }
            if (length > 65536) throw new InvalidOperationException("A TV enviou uma mensagem WebSocket inesperadamente grande.");
            byte[] mask = masked ? ReadExact(stream, 4) : null;
            byte[] payload = ReadExact(stream, (int)length);
            if (masked) for (int i = 0; i < payload.Length; i++) payload[i] = (byte)(payload[i] ^ mask[i % 4]);
            return new WsFrame { Opcode = opcode, Payload = payload };
        }

        private static void WriteWebSocketFrame(Stream stream, int opcode, byte[] payload)
        {
            byte[] mask = new byte[4]; using (var random = new RNGCryptoServiceProvider()) random.GetBytes(mask);
            using (var frame = new MemoryStream())
            {
                frame.WriteByte((byte)(128 | opcode));
                if (payload.Length < 126) frame.WriteByte((byte)(128 | payload.Length));
                else if (payload.Length <= 65535) { frame.WriteByte(254); frame.WriteByte((byte)(payload.Length >> 8)); frame.WriteByte((byte)payload.Length); }
                else { frame.WriteByte(255); for (int i = 7; i >= 0; i--) frame.WriteByte((byte)(((ulong)payload.Length >> (8 * i)) & 255)); }
                frame.Write(mask, 0, mask.Length);
                for (int i = 0; i < payload.Length; i++) frame.WriteByte((byte)(payload[i] ^ mask[i % 4]));
                byte[] data = frame.ToArray(); stream.Write(data, 0, data.Length); stream.Flush();
            }
        }

        private bool StartupEnabled()
        {
            try { using (var key = Registry.CurrentUser.OpenSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Run")) return key != null && key.GetValue("SamsungTVRemote") != null; }
            catch { return false; }
        }

        private void SetStartup(bool enabled)
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Run", true))
                {
                    if (key == null) return;
                    if (enabled) key.SetValue("SamsungTVRemote", "\"" + ProcessPath() + "\" --tray");
                    else key.DeleteValue("SamsungTVRemote", false);
                }
                messageLabel.Text = "Inicialização do Windows atualizada.";
            }
            catch (Exception ex) { messageLabel.Text = "Falha: " + ex.Message; }
        }

        private static string ProcessPath() { return System.Reflection.Assembly.GetExecutingAssembly().Location; }
        private void ShowWindow() { Show(); WindowState = WindowState.Normal; Activate(); }
        private void HideWindow() { Hide(); }
    }
}
