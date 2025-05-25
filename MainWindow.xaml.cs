using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using ListView = System.Windows.Controls.ListView;
using Color = System.Windows.Media.Color;
using Brushes = System.Windows.Media.Brushes;
using Orientation = System.Windows.Controls.Orientation;
using Button = System.Windows.Controls.Button;
using Control = System.Windows.Controls.Control;
using ListViewItem = System.Windows.Controls.ListViewItem;
using TextBox = System.Windows.Controls.TextBox;
using MessageBox = System.Windows.MessageBox;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using VerticalAlignment = System.Windows.VerticalAlignment;
using Cursors = System.Windows.Input.Cursors;
using System.Drawing;
using System.IO;
using System.Linq;
using Microsoft.Win32;
using OpenFileDialog = Microsoft.Win32.OpenFileDialog;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using Imaging = System.Windows.Interop.Imaging;
using System.Diagnostics;
using IOPath = System.IO.Path;
using System.ComponentModel;
using System.Windows.Forms; // For ColorDialog
using System.Text.Json;
using System.Windows.Forms.Integration;
using NotifyIcon = System.Windows.Forms.NotifyIcon;
using ToolStripMenuItem = System.Windows.Forms.ToolStripMenuItem;

namespace DesktopIconManager
{
    public class AppConfig
    {
        public double WindowWidth { get; set; }
        public double WindowHeight { get; set; }
        public double WindowLeft { get; set; }
        public double WindowTop { get; set; }
        public WindowState WindowState { get; set; }
        public List<CategoryConfig> Categories { get; set; } = new List<CategoryConfig>();
        public bool AutoStartup { get; set; } = false;  // 新增開機自動啟動設定
    }

    public class CategoryConfig
    {
        public string Name { get; set; } = string.Empty;
        public double Opacity { get; set; }
        public Color BackgroundColor { get; set; }
        public double WindowWidth { get; set; }
        public double WindowHeight { get; set; }
        public double WindowLeft { get; set; }
        public double WindowTop { get; set; }
        public List<string> Files { get; set; } = new List<string>();
    }

    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public class CategoryInfo : System.ComponentModel.INotifyPropertyChanged
    {
        private string _name;
        public string Name
        {
            get { return _name; }
            set
            {
                if (_name != value)
                {
                    // TODO: Add validation for empty or duplicate names here later if needed
                    _name = value;
                    OnPropertyChanged(nameof(Name));

                    // 同步更新到 FloatingGroupWindow 的標題
                    Window?.SetTitle(value);
                }
            }
        }
        public FloatingGroupWindow? Window { get; set; }
        public int FileCount => Window?.GetFiles().Count ?? 0;

        private bool _isEditing = false;
        public bool IsEditing
        {
            get { return _isEditing; }
            set
            {
                if (_isEditing != value)
                {
                    _isEditing = value;
                    OnPropertyChanged(nameof(IsEditing));
                }
            }
        }

        private System.Windows.Media.Color _backgroundColor;
        public System.Windows.Media.Color BackgroundColor
        {
            get { return _backgroundColor; }
            set
            {
                if (_backgroundColor != value)
                {
                    _backgroundColor = value;
                    OnPropertyChanged(nameof(BackgroundColor));
                }
            }
        }

        private double _opacity;
        public double Opacity
        {
            get { return _opacity; }
            set
            {
                if (_opacity != value)
                {
                    // 限制透明度在 0 到 1 之間
                    _opacity = Math.Max(0.0, Math.Min(1.0, value));
                    OnPropertyChanged(nameof(Opacity));
                }
            }
        }

        public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(propertyName));
        }
    }

    public partial class MainWindow : Window
    {
        private ObservableCollection<CategoryInfo> categories = new ObservableCollection<CategoryInfo>();
        private Dictionary<string, FloatingGroupWindow> categoryWindows = new Dictionary<string, FloatingGroupWindow>();
        private DateTime lastClickTime = DateTime.MinValue;
        private const int DoubleClickThreshold = 300; // 毫秒
        private System.Windows.Media.Color _selectedColor = System.Windows.Media.Color.FromArgb(0xFF, 0xA0, 0xE0, 0xA0); // 預設顏色，用於顏色選取器 (完全不透明)
        private CategoryInfo? _currentSelectedCategory = null; // 儲存當前選取的分類
        private const string CONFIG_FILE = "config.json";
        private string ConfigFilePath => System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, CONFIG_FILE);
        private NotifyIcon? _notifyIcon;
        private bool _isClosing = false;

        public MainWindow()
        {
            InitializeComponent();
            CategoryListView.ItemsSource = categories;
            InitializeNotifyIcon();
            LoadConfiguration();

            // Check if the application was started with /minimized argument
            string[] args = Environment.GetCommandLineArgs();
            if (args.Length > 1 && args[1].ToLower() == "/minimized")
            {
                this.WindowState = WindowState.Minimized;
                this.Hide();
            }
        }

        private void InitializeNotifyIcon()
        {
            try
            {
                // 使用完整路徑載入圖示
                string iconPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "default_icon.png");
                if (System.IO.File.Exists(iconPath))
                {
                    using (var bitmap = new System.Drawing.Bitmap(iconPath))
                    {
                        var iconHandle = bitmap.GetHicon();
                        var icon = System.Drawing.Icon.FromHandle(iconHandle);
                        
                        _notifyIcon = new NotifyIcon
                        {
                            Icon = icon,
                            Visible = true,
                            Text = "DesktopIconManager"
                        };

                        // 創建右鍵選單
                        var contextMenu = new ContextMenuStrip();
                        
                        var showMenuItem = new ToolStripMenuItem("顯示主視窗");
                        showMenuItem.Click += (s, e) => ShowMainWindow();
                        contextMenu.Items.Add(showMenuItem);

                        // 添加開機自動啟動選項
                        var startupMenuItem = new ToolStripMenuItem("開機自動啟動");
                        startupMenuItem.CheckOnClick = true;
                        startupMenuItem.Checked = IsStartupEnabled();
                        startupMenuItem.Click += (s, e) => ToggleStartup(startupMenuItem);
                        contextMenu.Items.Add(startupMenuItem);

                        contextMenu.Items.Add(new ToolStripSeparator());

                        var exitMenuItem = new ToolStripMenuItem("結束程式");
                        exitMenuItem.Click += (s, e) => ExitApplication();
                        contextMenu.Items.Add(exitMenuItem);

                        _notifyIcon.ContextMenuStrip = contextMenu;
                        _notifyIcon.DoubleClick += (s, e) => ShowMainWindow();
                    }
                }
                else
                {
                    MessageBox.Show($"找不到圖示檔案：{iconPath}", "錯誤", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"初始化系統匣圖示時發生錯誤：{ex.Message}", "錯誤", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private bool IsStartupEnabled()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey("SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Run", false);
                return key?.GetValue("DesktopIconManager") != null;
            }
            catch
            {
                return false;
            }
        }

        private void ToggleStartup(ToolStripMenuItem menuItem)
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey("SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Run", true);
                if (key != null)
                {
                    if (menuItem.Checked)
                    {
                        // 啟用開機自動啟動
                        var appPath = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;
                        if (string.IsNullOrEmpty(appPath))
                        {
                            throw new Exception("無法獲取應用程式路徑");
                        }
                        key.SetValue("DesktopIconManager", $"\"{appPath}\" /minimized");
                    }
                    else
                    {
                        // 停用開機自動啟動
                        key.DeleteValue("DesktopIconManager", false);
                    }

                    // 更新設定
                    var config = LoadConfig();
                    if (config != null)
                    {
                        config.AutoStartup = menuItem.Checked;
                        SaveConfig(config);
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"設定開機自動啟動時發生錯誤：{ex.Message}", "錯誤", MessageBoxButton.OK, MessageBoxImage.Error);
                menuItem.Checked = !menuItem.Checked; // 恢復原來的狀態
            }
        }

        private AppConfig? LoadConfig()
        {
            try
            {
                if (File.Exists(ConfigFilePath))
                {
                    string json = File.ReadAllText(ConfigFilePath);
                    return JsonSerializer.Deserialize<AppConfig>(json);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"載入配置時發生錯誤：{ex.Message}", "錯誤", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            return null;
        }

        private void SaveConfig(AppConfig config)
        {
            try
            {
                string json = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(ConfigFilePath, json);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"儲存配置時發生錯誤：{ex.Message}", "錯誤", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ShowMainWindow()
        {
            this.Show();
            this.WindowState = WindowState.Normal;
            this.Activate();
        }

        private void ExitApplication()
        {
            _isClosing = true;
            SaveConfiguration();
            _notifyIcon?.Dispose();
            System.Windows.Application.Current.Shutdown();
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            if (!_isClosing)
            {
                e.Cancel = true;
                this.Hide();
                return;
            }

            base.OnClosing(e);
            SaveConfiguration();
        }

        private void LoadConfiguration()
        {
            try
            {
                if (File.Exists(ConfigFilePath))
                {
                    string json = File.ReadAllText(ConfigFilePath);
                    var config = JsonSerializer.Deserialize<AppConfig>(json);
                    if (config != null)
                    {
                        // 載入主視窗設定
                        this.Width = config.WindowWidth;
                        this.Height = config.WindowHeight;
                        this.Left = config.WindowLeft;
                        this.Top = config.WindowTop;
                        this.WindowState = config.WindowState;

                        // 載入分類設定
                        foreach (var categoryConfig in config.Categories)
                        {
                            var window = new FloatingGroupWindow();
                            window.Width = categoryConfig.WindowWidth;
                            window.Height = categoryConfig.WindowHeight;
                            window.Left = categoryConfig.WindowLeft;
                            window.Top = categoryConfig.WindowTop;

                            var category = new CategoryInfo
                            {
                                Name = categoryConfig.Name,
                                Window = window,
                                BackgroundColor = categoryConfig.BackgroundColor,
                                Opacity = categoryConfig.Opacity
                            };

                            window.CategoryInfo = category;
                            window.SetTitle(categoryConfig.Name);

                            // 載入檔案
                            foreach (var filePath in categoryConfig.Files)
                            {
                                if (File.Exists(filePath))
                                {
                                    window.AddFile(filePath);
                                }
                            }

                            category.PropertyChanged += Category_PropertyChanged;
                            categories.Add(category);
                            categoryWindows[categoryConfig.Name] = window;
                            window.Show();
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"載入配置時發生錯誤：{ex.Message}", "錯誤", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void SaveConfiguration()
        {
            try
            {
                var config = new AppConfig
                {
                    WindowWidth = this.Width,
                    WindowHeight = this.Height,
                    WindowLeft = this.Left,
                    WindowTop = this.Top,
                    WindowState = this.WindowState,
                    Categories = new List<CategoryConfig>()
                };

                foreach (var category in categories)
                {
                    if (category.Window != null)
                    {
                        var categoryConfig = new CategoryConfig
                        {
                            Name = category.Name,
                            Opacity = category.Opacity,
                            BackgroundColor = category.BackgroundColor,
                            WindowWidth = category.Window.Width,
                            WindowHeight = category.Window.Height,
                            WindowLeft = category.Window.Left,
                            WindowTop = category.Window.Top,
                            Files = category.Window.GetFiles().Select(f => f.FullPath).ToList()
                        };
                        config.Categories.Add(categoryConfig);
                    }
                }

                string json = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(ConfigFilePath, json);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"儲存配置時發生錯誤：{ex.Message}", "錯誤", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void WindowHeader_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                this.DragMove();
            }
        }

        private void MinimizeButton_Click(object sender, RoutedEventArgs e)
        {
            this.WindowState = WindowState.Minimized;
        }

        private void MaximizeButton_Click(object sender, RoutedEventArgs e)
        {
            this.WindowState = this.WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        private void AddCategoryButton_Click(object sender, RoutedEventArgs e)
        {
            string categoryName = CategoryNameTextBox.Text.Trim();
            if (string.IsNullOrEmpty(categoryName))
            {
                MessageBox.Show("請輸入分類名稱", "錯誤", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            if (categories.Any(c => c.Name == categoryName))
            {
                MessageBox.Show("分類名稱已存在", "錯誤", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            // 讀取新分類的透明度 (百分比)
            double newCategoryOpacity = 0xA0 / 255.0; // 預設為與預設背景顏色相同的透明度
            if (double.TryParse(NewCategoryOpacityTextBox.Text, out double parsedOpacityPercent))
            {
                // 將百分比轉換為 0-1 的小數，並限制在 0 到 1 之間
                newCategoryOpacity = Math.Max(0.0, Math.Min(1.0, parsedOpacityPercent / 100.0));
            }
            //else: 可以添加錯誤提示

            var window = new FloatingGroupWindow();
            // 應用窗口預設大小和位置
            window.Width = 300;
            window.Height = 200;
            window.Left = 300;
            window.Top = 200;

            var category = new CategoryInfo 
            {
                Name = categoryName,
                Window = window,
                // 為新分類設定預設背景顏色 (完全不透明) 和讀取的透明度
                BackgroundColor = System.Windows.Media.Color.FromArgb(0xFF, 0xA0, 0xE0, 0xA0), // 預設顏色 (完全不透明)
                Opacity = newCategoryOpacity
            };

            // 將 CategoryInfo 設置給 FloatingGroupWindow
            window.CategoryInfo = category;

            window.SetTitle(categoryName);
            window.Show();

            // 訂閱 PropertyChanged 事件以處理名稱變更
            category.PropertyChanged += Category_PropertyChanged;
            
            categories.Add(category);
            categoryWindows[categoryName] = window;
            CategoryNameTextBox.Clear();
        }

        private void Category_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(CategoryInfo.Name))
            {
                var category = sender as CategoryInfo;
                if (category != null)
                {
                    // 在名稱更改時更新 dictionary 的鍵
                    // 需要先找到舊的名稱，這裡假設名稱在 dictionary 中是唯一的鍵
                    // 或者在 CategoryInfo 中儲存舊名稱，但這樣會增加複雜性
                    // 更簡單的方法是在 CategoryInfo 中儲存 GUID 或其他唯一 ID 作為 dictionary 的鍵
                    // 為了本次修改，我們先假設名稱是唯一的，並嘗試找到舊鍵
                    // 注意：如果在名稱更改時有並發操作，這種方法可能會有問題

                    // 尋找舊的分類名稱
                    string? oldName = categoryWindows.FirstOrDefault(x => x.Value == category.Window).Key;

                    if (oldName != null && oldName != category.Name)
                    {
                        // 移除舊的 entry
                        categoryWindows.Remove(oldName);
                        // 添加新的 entry
                        categoryWindows[category.Name] = category.Window;
                    }
                }
            }
        }

        private void DeleteCategoryButton_Click(object sender, RoutedEventArgs e)
        {
            var button = sender as Button;
            var category = button?.Tag as CategoryInfo ?? CategoryListView.SelectedItem as CategoryInfo;
            
            if (category != null)
            {
                var result = MessageBox.Show("確定要刪除此分類嗎？", "確認刪除", 
                    MessageBoxButton.YesNo, MessageBoxImage.Question);
                
                if (result == MessageBoxResult.Yes)
                {
                    // 直接關閉 FloatingGroupWindow
                    category.Window?.Close();

                    // 從 dictionary 和 collection 中移除
                    if (categoryWindows.ContainsKey(category.Name))
                    {
                         categoryWindows.Remove(category.Name);
                    }
                    categories.Remove(category);

                    // 取消訂閱 PropertyChanged 事件
                    category.PropertyChanged -= Category_PropertyChanged;
                }
            }
        }

        private void CategoryListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var selectedCategory = CategoryListView.SelectedItem as CategoryInfo;
            _currentSelectedCategory = selectedCategory; // 更新當前選取的分類

            if (selectedCategory != null && selectedCategory.Window != null)
            {
                // 激活 FloatingGroupWindow
                selectedCategory.Window.Activate();

                // 更新編輯區域的顯示值
                SelectedCategoryColorPreview.Background = new SolidColorBrush(selectedCategory.BackgroundColor);
                // 將 0-1 的透明度轉換為百分比顯示
                SelectedCategoryOpacityTextBox.Text = (selectedCategory.Opacity * 100.0).ToString("F0"); // "F0" 不顯示小數點

                // 啟用編輯控制項
                SelectedCategoryColorPreview.IsEnabled = true;
                //SelectColorButton.IsEnabled = true; // Assuming button is named SelectColorButton
                SelectedCategoryOpacityTextBox.IsEnabled = true;
            }
            else
            {
                // 如果沒有選取分類，禁用編輯控制項並清除顯示值
                SelectedCategoryColorPreview.Background = new SolidColorBrush(System.Windows.Media.Colors.Transparent);
                SelectedCategoryOpacityTextBox.Text = string.Empty;

                SelectedCategoryColorPreview.IsEnabled = false;
                //SelectColorButton.IsEnabled = false; // Assuming button is named SelectColorButton
                SelectedCategoryOpacityTextBox.IsEnabled = false;
            }
        }

        private void ViewFilesButton_Click(object sender, RoutedEventArgs e)
        {
            var button = sender as Button;
            var category = button?.Tag as CategoryInfo;
            
            if (category != null && categoryWindows.ContainsKey(category.Name))
            {
                var window = categoryWindows[category.Name];
                var files = window.GetFiles();
                
                // 显示文件列表
                string fileList = string.Join("\n", files.Select(f => f.Name));
                MessageBox.Show($"分類 '{category.Name}' 中的檔案：\n\n{fileList}", 
                              "檔案列表", 
                              MessageBoxButton.OK, 
                              MessageBoxImage.Information);
                
                // 更新ListView显示
                CategoryListView.Items.Refresh();
            }
        }

        private void AddFileButton_Click(object sender, RoutedEventArgs e)
        {
            var selectedCategory = CategoryListView.SelectedItem as CategoryInfo;
            if (selectedCategory == null)
            {
                MessageBox.Show("請先選擇一個分類", "錯誤", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            var openFileDialog = new OpenFileDialog
            {
                Multiselect = true,
                Title = "選擇要添加的檔案"
            };

            if (openFileDialog.ShowDialog() == true)
            {
                foreach (string filePath in openFileDialog.FileNames)
                {
                    if (File.Exists(filePath))
                    {
                        var fileInfo = new FileInfo(filePath);
                        var icon = GetFileIcon(filePath);
                        
                        var fileItem = new FileItem
                        {
                            Name = fileInfo.Name,
                            FullPath = filePath,
                            Icon = icon
                        };

                        selectedCategory.Window.AddFile(filePath);
                    }
                }
            }
        }

        private ImageSource? GetFileIcon(string filePath)
        {
            try
            {
                // 如果是 .ico 檔案，使用 IconBitmapDecoder
                if (IOPath.GetExtension(filePath).ToLower() == ".ico")
                {
                    try
                    {
                        var iconSource = LoadPngIcon(filePath);
                        if (iconSource != null)
                        {
                            iconSource.Freeze();
                            return iconSource;
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Error loading .ico file: {ex.Message}");
                        Debug.WriteLine($"Stack trace: {ex.StackTrace}");
                    }
                }

                var guid = typeof(IShellItemImageFactory).GUID;
                SHCreateItemFromParsingName(filePath, IntPtr.Zero, ref guid, out IShellItemImageFactory factory);

                var size = new SIZE { cx = 256, cy = 256 };
                factory.GetImage(size, SIIGBF.BiggerSizeOk, out var hbitmap);

                if (hbitmap != IntPtr.Zero)
                {
                    try
                    {
                        var source = Imaging.CreateBitmapSourceFromHBitmap(
                            hbitmap,
                            IntPtr.Zero,
                            Int32Rect.Empty,
                            BitmapSizeOptions.FromEmptyOptions());

                        source.Freeze();
                        return source;
                    }
                    finally
                    {
                        DeleteObject(hbitmap);
                    }
                }

                // Fallback to traditional method
                using (System.Drawing.Icon icon = System.Drawing.Icon.ExtractAssociatedIcon(filePath))
                {
                    var source = Imaging.CreateBitmapSourceFromHIcon(
                        icon.Handle,
                        Int32Rect.Empty,
                        BitmapSizeOptions.FromEmptyOptions());

                    source.Freeze();
                    return source;
                }
            }
            catch
            {
                return null;
            }
        }

        private ImageSource? LoadPngIcon(string path)
        {
            try
            {
                using var stream = File.OpenRead(path);
                var decoder = new IconBitmapDecoder(
                    stream,
                    BitmapCreateOptions.IgnoreColorProfile | BitmapCreateOptions.PreservePixelFormat,
                    BitmapCacheOption.Default);

                // 獲取最大尺寸的圖示
                var largestFrame = decoder.Frames
                    .OrderByDescending(f => f.PixelWidth * f.PixelHeight)
                    .FirstOrDefault();

                if (largestFrame != null)
                {
                    // 創建新的 BitmapSource 以確保高畫質
                    var source = new WriteableBitmap(largestFrame);
                    source.Freeze();
                    return source;
                }

                return null;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error in LoadPngIcon: {ex.Message}");
                Debug.WriteLine($"Stack trace: {ex.StackTrace}");
                return null;
            }
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
        private static extern void SHCreateItemFromParsingName(
            [In][MarshalAs(UnmanagedType.LPWStr)] string pszPath,
            IntPtr pbc,
            [In] ref Guid riid,
            [Out][MarshalAs(UnmanagedType.Interface)] out IShellItemImageFactory ppv
        );

        [DllImport("gdi32.dll")]
        private static extern bool DeleteObject(IntPtr hObject);

        [DllImport("gdi32.dll")]
        private static extern IntPtr CreateCompatibleDC(IntPtr hDC);

        [DllImport("gdi32.dll")]
        private static extern IntPtr CreateCompatibleBitmap(IntPtr hDC, int nWidth, int nHeight);

        [DllImport("gdi32.dll")]
        private static extern IntPtr SelectObject(IntPtr hDC, IntPtr hObject);

        [DllImport("gdi32.dll")]
        private static extern bool DeleteDC(IntPtr hDC);

        [DllImport("gdi32.dll")]
        private static extern bool BitBlt(IntPtr hObject, int nXDest, int nYDest, int nWidth, int nHeight, IntPtr hObjectSource, int nXSrc, int nYSrc, uint dwRop);

        private const uint SRCCOPY = 0x00CC0020;
        private const uint SRCAND = 0x008800C6;
        private const uint SRCPAINT = 0x00EE0086;

        [ComImport]
        [Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IShellItemImageFactory
        {
            void GetImage(SIZE size, SIIGBF flags, out IntPtr phbm);
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct SIZE
        {
            public int cx;
            public int cy;
            public SIZE(int x, int y) { cx = x; cy = y; }
        }

        [Flags]
        private enum SIIGBF
        {
            ResizeToFit = 0x00,
            BiggerSizeOk = 0x01,
            MemoryOnly = 0x02,
            IconOnly = 0x04,
            ThumbnailOnly = 0x08,
            InCacheOnly = 0x10,
        }

        private void CategoryNameTextBlock_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            var textBlock = sender as TextBlock;
            if (textBlock != null)
            {
                var category = textBlock.DataContext as CategoryInfo;
                if (category != null)
                {
                    var currentTime = DateTime.Now;
                    var timeSinceLastClick = (currentTime - lastClickTime).TotalMilliseconds;
                    lastClickTime = currentTime;

                    // 如果是雙擊
                    if (timeSinceLastClick < DoubleClickThreshold)
                    {
                        category.IsEditing = true;
                        // 在這裡嘗試找到對應的 TextBox 並設置焦點
                        var container = VisualTreeHelper.GetParent(textBlock) as Grid;
                        if (container != null)
                        {
                            var textBox = container.Children.OfType<TextBox>().FirstOrDefault();
                            if (textBox != null)
                            {
                                Dispatcher.BeginInvoke(
                                    System.Windows.Threading.DispatcherPriority.Input,
                                    new Action(() =>
                                    {
                                        textBox.Focus();
                                        textBox.SelectAll();
                                    }));
                            }
                        }
                    }
                }
            }
        }

        private void CategoryNameTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            var textBox = sender as TextBox;
            if (textBox != null)
            {
                var category = textBox.DataContext as CategoryInfo;
                if (category != null)
                {
                    category.IsEditing = false;
                    // 可以在這裡添加驗證邏輯
                }
            }
        }

        private void CategoryNameTextBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                var textBox = sender as TextBox;
                if (textBox != null)
                {
                    var category = textBox.DataContext as CategoryInfo;
                    if (category != null)
                    {
                        // 在失去焦點時處理名稱驗證和更新
                        // 這裡只負責退出編輯狀態
                        category.IsEditing = false;
                        
                        // 移除焦點以觸發 LostFocus 事件
                        // 找到 TextBox 的父級容器來轉移焦點
                        var parent = VisualTreeHelper.GetParent(textBox) as UIElement;
                        if (parent != null)
                        {
                            parent.Focus();
                        }
                    }
                }
            }
        }

        private void SelectColorButton_Click(object sender, RoutedEventArgs e)
        {
            if (_currentSelectedCategory == null) return; // 如果沒有選取分類，不執行操作

            var colorDialog = new ColorDialog();
            // 將 WPF 的 Color 轉換為 System.Drawing.Color
            colorDialog.Color = System.Drawing.Color.FromArgb(_currentSelectedCategory.BackgroundColor.A, _currentSelectedCategory.BackgroundColor.R, _currentSelectedCategory.BackgroundColor.G, _currentSelectedCategory.BackgroundColor.B);

            if (colorDialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                // 將 System.Drawing.Color 轉換回 WPF 的 Color
                _currentSelectedCategory.BackgroundColor = System.Windows.Media.Color.FromArgb(colorDialog.Color.A, colorDialog.Color.R, colorDialog.Color.G, colorDialog.Color.B);
                // CategoryInfo 的 PropertyChanged 會觸發 FloatingGroupWindow 更新
                // 同時更新 MainWindow 的預覽
                SelectedCategoryColorPreview.Background = new SolidColorBrush(_currentSelectedCategory.BackgroundColor);
            }
        }

        private void SelectedCategoryOpacityTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_currentSelectedCategory == null) return; // 如果沒有選取分類，不執行操作

            if (double.TryParse(SelectedCategoryOpacityTextBox.Text, out double parsedOpacityPercent))
            {
                // 將百分比轉換為 0-1 的小數，並更新 CategoryInfo 的 Opacity 屬性
                _currentSelectedCategory.Opacity = Math.Max(0.0, Math.Min(1.0, parsedOpacityPercent / 100.0));
                // CategoryInfo 的 Opacity 屬性設置器已經包含範圍限制和 PropertyChanged 通知
            }
            // else: 可以添加錯誤提示，或者在 LostFocus 時處理無效輸入
        }
    }
}