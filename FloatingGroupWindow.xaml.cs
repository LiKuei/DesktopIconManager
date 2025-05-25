using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using System.Drawing;
using System.Windows.Media;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Win32;
using DragEventArgs = System.Windows.DragEventArgs;
using DataFormats = System.Windows.DataFormats;
using DragDropEffects = System.Windows.DragDropEffects;
using MessageBox = System.Windows.MessageBox;
using OpenFileDialog = Microsoft.Win32.OpenFileDialog;
using Clipboard = System.Windows.Clipboard;
using System.Diagnostics;
using System.Windows.Media.Animation;
using System.Runtime.InteropServices;
using IWshRuntimeLibrary;
using File = System.IO.File;
using Imaging = System.Windows.Interop.Imaging;
using System.Text;

namespace DesktopIconManager
{
    [ComImport]
    [Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IShellItemImageFactory
    {
        void GetImage(SIZE size, SIIGBF flags, out IntPtr phbm);
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct SIZE
    {
        public int cx;
        public int cy;
        public SIZE(int x, int y) { cx = x; cy = y; }
    }

    [Flags]
    internal enum SIIGBF
    {
        ResizeToFit = 0x00,
        BiggerSizeOk = 0x01,
        MemoryOnly = 0x02,
        IconOnly = 0x04,
        ThumbnailOnly = 0x08,
        InCacheOnly = 0x10,
    }

    public class FileItem
    {
        public required string Name { get; set; }
        public required string FullPath { get; set; }
        public ImageSource? Icon { get; set; }
    }

    public partial class FloatingGroupWindow : Window
    {
        private TextBlock titleTextBlock;
        public string GroupTitle { get; private set; } = string.Empty;
        private const int WS_EX_TOOLWINDOW = 0x00000080;
        private const int GWL_EXSTYLE = -20;
        private ObservableCollection<FileItem> files = new ObservableCollection<FileItem>();

        // Windows Shell API constants
        private const uint SHGFI_ICON = 0x000000100;
        private const uint SHGFI_LARGEICON = 0x000000000;
        private const uint SHGFI_SMALLICON = 0x000000001;
        private const uint SHGFI_USEFILEATTRIBUTES = 0x000000010;
        private const uint SHGFI_ADDOVERLAYS = 0x000000020;
        private const uint SHGFI_OVERLAYINDEX = 0x000000040;
        private const uint SHGFI_SYSICONINDEX = 0x000004000;
        private const uint SHGFI_ICONLOCATION = 0x000001000;

        // Icon sizes
        private const int SHIL_EXTRALARGE = 0x02;  // 48x48
        private const int SHIL_JUMBO = 0x04;       // 256x256

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private struct SHFILEINFO
        {
            public IntPtr hIcon;
            public int iIcon;
            public uint dwAttributes;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
            public string szDisplayName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
            public string szTypeName;
        }

        [DllImport("shell32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr SHGetFileInfo(string pszPath, uint dwFileAttributes, ref SHFILEINFO psfi, uint cbSizeFileInfo, uint uFlags);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool DestroyIcon(IntPtr hIcon);

        [DllImport("shell32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr ExtractIconEx(string lpszFile, int nIconIndex, IntPtr[] phiconLarge, IntPtr[] phiconSmall, uint nIcons);

        [DllImport("shell32.dll", CharSet = CharSet.Auto)]
        private static extern int SHGetImageList(int iImageList, ref Guid riid, out IntPtr ppv);

        [ComImport]
        [Guid("46EB5926-582E-4017-9FDF-E8998DAA0950")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IImageList
        {
            [PreserveSig]
            int GetIcon(int i, int flags, out IntPtr picon);
        }

        [ComImport]
        [Guid("00021401-0000-0000-C000-000000000046")]
        internal class ShellLink
        {
        }

        [ComImport]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        [Guid("000214F9-0000-0000-C000-000000000046")]
        internal interface IShellLink
        {
            void GetPath([Out] StringBuilder pszFile, int cch, out WIN32_FIND_DATA pfd, uint fFlags);
            void GetIDList(out IntPtr ppidl);
            void SetIDList(IntPtr pidl);
            void GetDescription([Out] StringBuilder pszName, int cch);
            void SetDescription(string pszName);
            void GetWorkingDirectory([Out] StringBuilder pszDir, int cch);
            void SetWorkingDirectory(string pszDir);
            void GetArguments([Out] StringBuilder pszArgs, int cch);
            void SetArguments(string pszArgs);
            void GetHotkey(out short pwHotkey);
            void SetHotkey(short wHotkey);
            void GetShowCmd(out int piShowCmd);
            void SetShowCmd(int iShowCmd);
            void GetIconLocation([Out] StringBuilder pszIconPath, int cch, out int piIcon);
            void SetIconLocation(string pszIconPath, int iIcon);
            void SetRelativePath(string pszPathRel, uint dwReserved);
            void Resolve(IntPtr hwnd, uint fFlags);
            void SetPath(string pszFile);
        }

        [ComImport]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        [Guid("0000010b-0000-0000-C000-000000000046")]
        internal interface IPersistFile
        {
            void GetClassID(out Guid pClassID);
            void IsDirty();
            void Load([MarshalAs(UnmanagedType.LPWStr)] string pszFileName, uint dwMode);
            void Save([MarshalAs(UnmanagedType.LPWStr)] string pszFileName, bool fRemember);
            void SaveCompleted([MarshalAs(UnmanagedType.LPWStr)] string pszFileName);
            void GetCurFile([MarshalAs(UnmanagedType.LPWStr)] out string ppszFileName);
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        public struct WIN32_FIND_DATA
        {
            public uint dwFileAttributes;
            public FILETIME ftCreationTime;
            public FILETIME ftLastAccessTime;
            public FILETIME ftLastWriteTime;
            public uint nFileSizeHigh;
            public uint nFileSizeLow;
            public uint dwReserved0;
            public uint dwReserved1;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
            public string cFileName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 14)]
            public string cAlternateFileName;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct FILETIME
        {
            public uint dwLowDateTime;
            public uint dwHighDateTime;
        }

        [DllImport("shell32.dll", CharSet = CharSet.Auto)]
        public static extern IntPtr ExtractIcon(IntPtr hInst, string lpszExeFileName, int nIconIndex);

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

        private DateTime lastClickTime = DateTime.MinValue;
        private const int DoubleClickThreshold = 300; // 毫秒

        public CategoryInfo? CategoryInfo { get; set; }

        public FloatingGroupWindow()
        {
            InitializeComponent();
            titleTextBlock = TitleText;
            FileItemsControl.ItemsSource = files;
            this.Loaded += FloatingGroupWindow_Loaded;
            this.Closed += FloatingGroupWindow_Closed;

            // 啟用拖放功能
            FileItemsControl.AllowDrop = true;
            FileItemsControl.Drop += FileItemsControl_Drop;
            FileItemsControl.DragEnter += FileItemsControl_DragEnter;
            FileItemsControl.DragOver += FileItemsControl_DragOver;
        }

        public List<FileItem> GetFiles()
        {
            return files.ToList();
        }

        public void AddFile(string filePath)
        {
            try
            {
                Debug.WriteLine($"Adding file: {filePath}");
                if (!File.Exists(filePath))
                {
                    Debug.WriteLine($"File does not exist: {filePath}");
                    MessageBox.Show($"文件不存在: {filePath}", "錯誤", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                var fileInfo = new FileInfo(filePath);
                Debug.WriteLine($"File info - Name: {fileInfo.Name}, Size: {fileInfo.Length}, LastWriteTime: {fileInfo.LastWriteTime}");

                var icon = GetFileIcon(filePath);
                Debug.WriteLine($"Icon retrieved: {icon != null}");
                
                // 检查文件是否已经存在
                if (!files.Any(f => f.FullPath.Equals(filePath, StringComparison.OrdinalIgnoreCase)))
                {
                    // 移除副檔名
                    string displayName = Path.GetFileNameWithoutExtension(fileInfo.Name);
                    
                    files.Add(new FileItem
                    {
                        Name = displayName,
                        FullPath = filePath,
                        Icon = icon
                    });
                    Debug.WriteLine($"Added file: {filePath}");
                    Debug.WriteLine($"AddFile: Icon is null? {icon == null}");
                }
                else
                {
                    Debug.WriteLine($"File already exists: {filePath}");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error adding file: {ex.Message}");
                Debug.WriteLine($"Stack trace: {ex.StackTrace}");
                MessageBox.Show($"無法添加文件: {ex.Message}", "錯誤", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void FloatingGroupWindow_Loaded(object sender, RoutedEventArgs e)
        {
            // 设置窗口样式，使其只显示在桌面上
            IntPtr hwnd = new WindowInteropHelper(this).Handle;
            int style = GetWindowLong(hwnd, GWL_EXSTYLE);
            SetWindowLong(hwnd, GWL_EXSTYLE, style | WS_EX_TOOLWINDOW);

            // 将窗口设置为桌面窗口
            SetParent(hwnd, GetDesktopWindow());

            // 訂閱 CategoryInfo 的 PropertyChanged 事件
            if (CategoryInfo != null)
            {
                CategoryInfo.PropertyChanged += CategoryInfo_PropertyChanged;
                // 根據 CategoryInfo 的初始值設置窗口樣式
                UpdateWindowStyleFromCategoryInfo();
            }

            // 確保窗口可以接收拖放
            this.AllowDrop = true;
            Debug.WriteLine("Window loaded and configured for drag and drop");
        }

        private void FloatingGroupWindow_Closed(object? sender, EventArgs e)
        {
            // 取消訂閱 CategoryInfo 的 PropertyChanged 事件
            if (CategoryInfo != null)
            {
                CategoryInfo.PropertyChanged -= CategoryInfo_PropertyChanged;
            }
        }

        private void CategoryInfo_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(CategoryInfo.BackgroundColor) || e.PropertyName == nameof(CategoryInfo.Opacity))
            {
                UpdateWindowStyleFromCategoryInfo();
            }
            else if (e.PropertyName == nameof(CategoryInfo.Name))
            {
                 // 如果名稱在 MainWindow 中被編輯，這裡同步更新標題
                 SetTitle(CategoryInfo?.Name ?? GroupTitle);
            }
        }

        private void UpdateWindowStyleFromCategoryInfo()
        {
            if (CategoryInfo != null)
            {
                // 获取原始背景颜色 (忽略其 Alpha 通道，因为我们将使用 CategoryInfo.Opacity 来设置总透明度)
                System.Windows.Media.Color originalColor = CategoryInfo.BackgroundColor;

                // 计算新的 Alpha 值：直接使用 CategoryInfo.Opacity (0-1) 乘以 255
                byte newAlpha = (byte)(CategoryInfo.Opacity * 255.0);

                // 创建带有新 Alpha 值和原始 RGB 值的颜色
                System.Windows.Media.Color semiTransparentColor = System.Windows.Media.Color.FromArgb(newAlpha, originalColor.R, originalColor.G, originalColor.B);

                // 使用带有新 Alpha 值的颜色创建 SolidColorBrush 并设置给窗口背景
                this.Background = new SolidColorBrush(semiTransparentColor);

                // 保留窗口本身的 Opacity 为 1.0，确保内容不透明
                this.Opacity = 1.0;
            }
        }

        public void SetTitle(string title)
        {
            titleTextBlock.Text = title;
            GroupTitle = title;
        }

        // 支援拖曳
        private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            this.DragMove();
        }

        private void FileItemsControl_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            var contextMenu = FileItemsControl.ContextMenu;
            if (contextMenu != null)
            {
                contextMenu.IsOpen = true;
                e.Handled = true;
            }
        }

        private void FileItem_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            var border = sender as Border;
            if (border != null)
            {
                var fileItem = border.DataContext as FileItem;
                if (fileItem != null)
                {
                    var currentTime = DateTime.Now;
                    var timeSinceLastClick = (currentTime - lastClickTime).TotalMilliseconds;
                    lastClickTime = currentTime;

                    // 如果是雙擊（兩次點擊時間間隔小於閾值）
                    if (timeSinceLastClick < DoubleClickThreshold)
                    {
                        try
                        {
                            // 添加视觉反馈
                            var animation = new ColorAnimation(Colors.LightBlue, TimeSpan.FromMilliseconds(100));
                            border.Background = new SolidColorBrush(Colors.White);
                            border.Background.BeginAnimation(SolidColorBrush.ColorProperty, animation);

                            // 打开文件
                            System.Diagnostics.Process.Start(new ProcessStartInfo
                            {
                                FileName = fileItem.FullPath,
                                UseShellExecute = true
                            });
                        }
                        catch (Exception ex)
                        {
                            MessageBox.Show($"無法打開文件: {ex.Message}", "錯誤", MessageBoxButton.OK, MessageBoxImage.Error);
                        }
                    }
                }
            }
        }

        private void FileItem_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            var border = sender as Border;
            if (border != null)
            {
                var fileItem = border.DataContext as FileItem;
                if (fileItem != null)
                {
                    var contextMenu = new ContextMenu();
                    
                    var openMenuItem = new MenuItem { Header = "開啟" };
                    openMenuItem.Click += (s, args) =>
                    {
                        try
                        {
                            System.Diagnostics.Process.Start(new ProcessStartInfo
                            {
                                FileName = fileItem.FullPath,
                                UseShellExecute = true
                            });
                        }
                        catch (Exception ex)
                        {
                            MessageBox.Show($"無法打開文件: {ex.Message}", "錯誤", MessageBoxButton.OK, MessageBoxImage.Error);
                        }
                    };

                    var removeMenuItem = new MenuItem { Header = "移除" };
                    removeMenuItem.Click += (s, args) =>
                    {
                        files.Remove(fileItem);
                    };

                    contextMenu.Items.Add(openMenuItem);
                    contextMenu.Items.Add(new Separator());
                    contextMenu.Items.Add(removeMenuItem);

                    contextMenu.IsOpen = true;
                    e.Handled = true;
                }
            }
        }

        private void AddFileMenuItem_Click(object sender, RoutedEventArgs e)
        {
            var openFileDialog = new OpenFileDialog
            {
                Multiselect = true,
                Title = "選擇要添加的檔案"
            };

            if (openFileDialog.ShowDialog() == true)
            {
                foreach (string filePath in openFileDialog.FileNames)
                {
                    AddFile(filePath);
                }
            }
        }

        private void PasteMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (Clipboard.ContainsFileDropList())
            {
                var fileList = Clipboard.GetFileDropList();
                foreach (string filePath in fileList)
                {
                    if (File.Exists(filePath))
                    {
                        AddFile(filePath);
                    }
                }
            }
        }

        private void FileItemsControl_DragEnter(object sender, DragEventArgs e)
        {
            Debug.WriteLine("DragEnter event triggered");
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                e.Effects = DragDropEffects.Copy;
                e.Handled = true;
                Debug.WriteLine("DragEnter: File drop data present");
            }
            else
            {
                Debug.WriteLine("DragEnter: No file drop data");
            }
        }

        private void FileItemsControl_DragOver(object sender, DragEventArgs e)
        {
            Debug.WriteLine("DragOver event triggered");
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                e.Effects = DragDropEffects.Copy;
                e.Handled = true;
                Debug.WriteLine("DragOver: File drop data present");
            }
            else
            {
                Debug.WriteLine("DragOver: No file drop data");
            }
        }

        private void FileItemsControl_Drop(object sender, DragEventArgs e)
        {
            Debug.WriteLine("Drop event triggered");
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                string[] droppedFiles = (string[])e.Data.GetData(DataFormats.FileDrop);
                Debug.WriteLine($"Drop: Received {droppedFiles.Length} files");
                foreach (string file in droppedFiles)
                {
                    if (File.Exists(file))
                    {
                        Debug.WriteLine($"Drop: Adding file {file}");
                        AddFile(file);
                    }
                }
                e.Handled = true;
            }
            else
            {
                Debug.WriteLine("Drop: No file drop data");
            }
        }

        private ImageSource? GetFileIcon(string path)
        {
            try
            {
                Debug.WriteLine($"Getting icon for path: {path}");
                
                if (!File.Exists(path))
                {
                    Debug.WriteLine($"File does not exist: {path}");
                    return null;
                }

                string targetPath = path;
                string? customIconPath = null;
                int iconIndex = 0;

                if (Path.GetExtension(path).ToLower() == ".lnk")
                {
                    try
                    {
                        var shellLink = (IShellLink)new ShellLink();
                        ((IPersistFile)shellLink).Load(path, 0);

                        // 獲取自定義圖示路徑
                        StringBuilder iconPath = new StringBuilder(260);
                        shellLink.GetIconLocation(iconPath, iconPath.Capacity, out iconIndex);
                        customIconPath = iconPath.ToString();
                        Debug.WriteLine($"Custom Icon Path: {customIconPath}, Index: {iconIndex}");

                        // 獲取目標文件路徑
                        StringBuilder targetPathBuilder = new StringBuilder(260);
                        WIN32_FIND_DATA findData;
                        shellLink.GetPath(targetPathBuilder, targetPathBuilder.Capacity, out findData, 0);
                        targetPath = targetPathBuilder.ToString();
                        Debug.WriteLine($"Using target path: {targetPath}");

                        // 如果存在自定義圖示路徑，嘗試使用它
                        if (!string.IsNullOrEmpty(customIconPath))
                        {
                            // 如果圖示路徑是相對路徑，轉換為絕對路徑
                            if (!Path.IsPathRooted(customIconPath))
                            {
                                customIconPath = Path.Combine(Path.GetDirectoryName(path) ?? "", customIconPath);
                            }

                            if (File.Exists(customIconPath))
                            {
                                Debug.WriteLine($"Using custom icon path: {customIconPath}");
                                
                                // 如果是 .ico 檔案，使用 IconBitmapDecoder
                                if (Path.GetExtension(customIconPath).ToLower() == ".ico")
                                {
                                    try
                                    {
                                        var iconSource = LoadPngIcon(customIconPath);
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

                                try
                                {
                                    var guid = typeof(IShellItemImageFactory).GUID;
                                    SHCreateItemFromParsingName(customIconPath, IntPtr.Zero, ref guid, out IShellItemImageFactory factory);

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
                                }
                                catch (Exception ex)
                                {
                                    Debug.WriteLine($"Error getting high quality custom icon: {ex.Message}");
                                    Debug.WriteLine($"Stack trace: {ex.StackTrace}");
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Error resolving shortcut: {ex.Message}");
                        Debug.WriteLine($"Stack trace: {ex.StackTrace}");
                    }
                }

                // 如果是 .ico 檔案，使用 IconBitmapDecoder
                if (Path.GetExtension(targetPath).ToLower() == ".ico")
                {
                    try
                    {
                        var iconSource = LoadPngIcon(targetPath);
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

                // 嘗試獲取目標文件的高畫質圖示
                try
                {
                    var guid = typeof(IShellItemImageFactory).GUID;
                    SHCreateItemFromParsingName(targetPath, IntPtr.Zero, ref guid, out IShellItemImageFactory factory);

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
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error getting high quality icon: {ex.Message}");
                    Debug.WriteLine($"Stack trace: {ex.StackTrace}");
                }

                // 如果高畫質圖示獲取失敗，使用傳統方法
                SHFILEINFO shinfo = new SHFILEINFO();
                uint flags = SHGFI_ICON | SHGFI_LARGEICON | SHGFI_ADDOVERLAYS;
                
                Debug.WriteLine("Falling back to SHGetFileInfo...");
                IntPtr fileIcon = SHGetFileInfo(
                    targetPath,
                    (uint)File.GetAttributes(targetPath),
                    ref shinfo,
                    (uint)Marshal.SizeOf(shinfo),
                    flags
                );

                if (shinfo.hIcon != IntPtr.Zero)
                {
                    try
                    {
                        var source = Imaging.CreateBitmapSourceFromHIcon(
                            shinfo.hIcon,
                            Int32Rect.Empty,
                            BitmapSizeOptions.FromEmptyOptions());

                        source.Freeze();
                        return source;
                    }
                    finally
                    {
                        DestroyIcon(shinfo.hIcon);
                    }
                }

                return null;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"GetFileIcon error: {ex.Message}");
                Debug.WriteLine($"Stack trace: {ex.StackTrace}");
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

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern IntPtr GetDesktopWindow();

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern IntPtr SetParent(IntPtr hWndChild, IntPtr hWndNewParent);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
    }
}
