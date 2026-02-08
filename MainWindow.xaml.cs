using Microsoft.Win32;
using PCSXMemoryEditor.Entities;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Linq;
using System.Net;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using WpfHexaEditor.Core;

namespace PCSXMemoryEditor
{
    

    public partial class MainWindow : Window
    {
        private MemoryMappedFile _mmf;
        private MemoryMappedViewStream _eeStream;
        private SortedSet<long> _foundAddresses = new SortedSet<long>();
        private SortedDictionary<long, ScanResult> scanResults = new SortedDictionary<long, ScanResult>();
        private ObservableCollection<FrozenValue> _frozenList = new ObservableCollection<FrozenValue>();
        private DispatcherTimer _freezeTimer;
        private byte[] _lastSnapshot; // 内存快照，用于变动追踪

        [DllImport("user32.dll")]
        private static extern bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);
        private const uint WM_KEYDOWN = 0x0100;
        private const uint WM_KEYUP = 0x0101;
        private const int VK_SPACE = 0x20;

        public MainWindow()
        {
            InitializeComponent();
            
            
            
            SetupTimer();
            FreezeListUI.ItemsSource = _frozenList;
            FreezeListUI.SelectionChanged += FreezeListUI_SelectionChanged;

            // 订阅 HexEditor 的位置改变事件
            MyHexEditor.SelectionStartChanged += MyHexEditor_SelectionStartChanged;


        }

        private void MyHexEditor_SelectionStartChanged(object sender, EventArgs e)
        {
            // 获取当前 HexEditor 的光标位置（偏移量）
            long currentPosition = MyHexEditor.SelectionStart;

            // 将偏移量转换为 PS2 内存地址（基址 0x20000000）
            // 使用 Dispatcher 确保在 UI 线程更新 TextBox
            Dispatcher.BeginInvoke(new Action(() => {
                TxtAddress.Text = (0x20000000 + currentPosition).ToString("X8");
            }));
        }


        private void SetupTimer()
        {
            _freezeTimer = new DispatcherTimer();
            _freezeTimer.Interval = TimeSpan.FromMilliseconds(15);
            _freezeTimer.Tick += FreezeTimer_Tick;
            _freezeTimer.Start();
        }

        // --- 核心功能：变动追踪过滤 ---
        private async void FilterChanged_Click(object sender, RoutedEventArgs e)
        {
            if (_eeStream == null || _foundAddresses.Count == 0)
            {
                MessageBox.Show("请先进行首次扫描以建立基准！");
                return;
            }

            string mode = (sender as Button).Tag.ToString();
            string typeTag = (ComboDataType.SelectedItem as ComboBoxItem).Tag.ToString();
            List<long> nextList = new List<long>();

            // 1. 在后台读取当前内存并比对，防止UI假死
            await Task.Run(() =>
            {
                byte[] currentMem = new byte[32 * 1024 * 1024];
                lock (_eeStream)
                {
                    _eeStream.Position = 0;
                    _eeStream.Read(currentMem, 0, currentMem.Length);
                }

                // 如果从未建立快照，则以当前内存为准
                if (_lastSnapshot == null)
                    _lastSnapshot = currentMem;

                foreach (long addr in _foundAddresses)
                {
                    int i = (int)addr;
                    bool isMatch = false;

                    try
                    {
                        if (typeTag == "Float")
                        {
                            float oldV = BitConverter.ToSingle(_lastSnapshot, i);
                            float newV = BitConverter.ToSingle(currentMem, i);
                            if (mode == "Increased")
                                isMatch = newV > oldV;
                            else if (mode == "Decreased")
                                isMatch = newV < oldV;
                            else if (mode == "Changed")
                                isMatch = Math.Abs(newV - oldV) > 0.001;
                            else if (mode == "Unchanged")
                                isMatch = Math.Abs(newV - oldV) < 0.001;
                        }
                        else
                        {
                            long oldV = ReadAsLong(_lastSnapshot, i, typeTag);
                            long newV = ReadAsLong(currentMem, i, typeTag);
                            if (mode == "Increased")
                                isMatch = newV > oldV;
                            else if (mode == "Decreased")
                                isMatch = newV < oldV;
                            else if (mode == "Changed")
                                isMatch = newV != oldV;
                            else if (mode == "Unchanged")
                                isMatch = newV == oldV;
                        }
                    }
                    catch { continue; }

                    if (isMatch)
                        nextList.Add(addr);
                }

                // 2. 重要：将当前内存存为下一次比对的“旧快照”
                _lastSnapshot = currentMem;
                _foundAddresses = [.. nextList];
            });

            UpdateUI();
        }

        private long ReadAsLong(byte[] buffer, int index, string tag)
        {
            if (tag == "1")
                return buffer[index];
            if (tag == "2")
                return BitConverter.ToInt16(buffer, index);
            return BitConverter.ToInt32(buffer, index);
        }

        // --- 基础连接与扫描功能 ---
        private void Connect_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var process = Process.GetProcessesByName("pcsx2-qt").FirstOrDefault() ??
                              Process.GetProcessesByName("pcsx2").FirstOrDefault();
                if (process == null)
                {
                    MessageBox.Show("未找到 PCSX2 进程，请先启动模拟器。");
                    return;
                }

                _mmf = MemoryMappedFile.OpenExisting($"pcsx2_{process.Id}", MemoryMappedFileRights.ReadWrite);
                _eeStream = _mmf.CreateViewStream(0, 32 * 1024 * 1024, MemoryMappedFileAccess.ReadWrite);
                MyHexEditor.Stream = _eeStream;
                MessageBox.Show("成功连接到 PCSX2 内存映射！");
            }
            catch (Exception ex) { MessageBox.Show("连接失败: " + ex.Message); }
        }

        private async void FirstScan_Click(object sender, RoutedEventArgs e)
        {
            if (_eeStream == null)
                return;

            // 1. 在进入后台线程前，提取所有 UI 数据
            string target = TxtSearchValue.Text;
            string typeTag = (ComboDataType.SelectedItem as ComboBoxItem)?.Tag.ToString() ?? "4";
            int byteSize = typeTag == "Float" ? 4 : int.Parse(typeTag);

            // 清空状态
            scanResults.Clear();
            _foundAddresses.Clear();

            await Task.Run(() => {
                byte[] buffer = new byte[32 * 1024 * 1024];

                lock (_eeStream)
                {
                    _eeStream.Position = 0;
                    _eeStream.Read(buffer, 0, buffer.Length);
                }

                _lastSnapshot = (byte[])buffer.Clone();

                // 2. 使用本地临时 List，避免操作 SortedSet 的开销
                List<long> tempList = new List<long>();

                for (int i = 0; i <= buffer.Length - byteSize; i++)
                {
                    // 假设 CompareValue 是你自定义的高效比较方法
                    if (string.IsNullOrEmpty(target) || CompareValue(buffer, i, target, typeTag))
                    {
                        tempList.Add(i);
                    }
                }

                // 3. 批量构建 SortedSet，这比一个一个 Add 快得多
                var sortedSet = new SortedSet<long>(tempList);

                // 回到主线程更新（Dispatcher）或直接赋值
                Application.Current.Dispatcher.Invoke(() => {
                    _foundAddresses = sortedSet;
                });
            });

            UpdateUI();
        }

        private async void NextScan_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(TxtSearchValue.Text) || _eeStream == null)
                return;
            string target = TxtSearchValue.Text;
            string typeTag = (ComboDataType.SelectedItem as ComboBoxItem).Tag.ToString();
            List<long> nextList = new List<long>();

            await Task.Run(() =>
            {
                byte[] temp = new byte[4];
                byte[] fullBuffer = new byte[32 * 1024 * 1024]; // 用于更新快照
                lock (_eeStream)
                {
                    _eeStream.Position = 0;
                    _eeStream.Read(fullBuffer, 0, fullBuffer.Length);

                    foreach (var addr in _foundAddresses)
                    {
                        if (CompareValue(fullBuffer, (int)addr, target, typeTag))
                            nextList.Add(addr);
                    }
                    _lastSnapshot = fullBuffer; // 再次扫描也应同步更新快照
                }
            });
            _foundAddresses = [.. nextList];
            UpdateUI();
        }

        


        private void UpdateUI()
        {
            TxtStatus.Text = $"结果: {_foundAddresses.Count}";

            List<long> removeList=new List<long>();

            foreach (long addr in scanResults.Keys)
            {
                if (!_foundAddresses.Contains(addr))
                {
                    removeList.Add(addr);
                }
            }

            foreach (long addr in removeList)
            {
                scanResults.Remove(addr);
            }

            foreach (long addr in _foundAddresses)
            {
                ScanResult scanResult;
                if (!scanResults.TryGetValue(addr, out scanResult))
                {
                    scanResult=new ScanResult();
                    scanResult.Address = $"0x{(0x20000000 + addr):X8}";
                    scanResult.RawAddress = addr;
                    scanResults.Add(addr, scanResult);
                }
                scanResult.Value = GetCurrentValueAt(addr);
            }

            ResultGrid.ItemsSource = new List<ScanResult>(scanResults.Values);
        }

        private string GetCurrentValueAt(long addr)
        {
            if (_lastSnapshot == null)
                return "??";
            string typeTag = (ComboDataType.SelectedItem as ComboBoxItem).Tag.ToString();
            try
            {
                if (typeTag == "1")
                    return _lastSnapshot[addr].ToString();
                if (typeTag == "2")
                    return BitConverter.ToInt16(_lastSnapshot, (int)addr).ToString();
                if (typeTag == "4")
                    return BitConverter.ToInt32(_lastSnapshot, (int)addr).ToString();
                if (typeTag == "Float")
                    return BitConverter.ToSingle(_lastSnapshot, (int)addr).ToString("F2");
            }
            catch { }
            return "??";
        }

        // --- 锁定逻辑与辅助功能 ---
        private void FreezeTimer_Tick(object sender, EventArgs e)
        {
            if (_eeStream == null || _frozenList.Count == 0)
                return;
            lock (_eeStream)
            {
                foreach (var item in _frozenList)
                {
                    try
                    {
                        byte[] data;
                        if (item.TypeTag == "1")
                            data = new byte[] { byte.Parse(item.Value) };
                        else if (item.TypeTag == "2")
                            data = BitConverter.GetBytes(short.Parse(item.Value));
                        else if (item.TypeTag == "4")
                            data = BitConverter.GetBytes(int.Parse(item.Value));
                        else
                            data = BitConverter.GetBytes(float.Parse(item.Value));

                        _eeStream.Position = item.RawAddress;
                        _eeStream.Write(data, 0, data.Length);
                    }
                    catch { continue; }
                }
            }
        }

        private void BtnAddFreeze_Click(object sender, RoutedEventArgs e)
        {
            if (ResultGrid.SelectedItem is ScanResult res)
            {
                if (!_frozenList.Any(f => f.RawAddress == res.RawAddress))
                {
                    _frozenList.Add(new FrozenValue
                    {
                        RawAddress = res.RawAddress,
                        Address = res.Address,
                        TypeTag = (ComboDataType.SelectedItem as ComboBoxItem).Tag.ToString(),
                        Value = res.Value
                    });
                }
            }
        }

        private void BtnUpdateFreeze_Click(object sender, RoutedEventArgs e)
        {
            if (FreezeListUI.SelectedItem is FrozenValue selected)
            {
                selected.Value = TxtEditFrozenValue.Text;
            }
        }

        private bool CompareValue(byte[] buffer, int index, string targetStr, string typeTag)
        {
            try
            {
                switch (typeTag)
                {
                    case "1":
                        return buffer[index] == byte.Parse(targetStr);
                    case "2":
                        return BitConverter.ToInt16(buffer, index) == short.Parse(targetStr);
                    case "4":
                        return BitConverter.ToInt32(buffer, index) == int.Parse(targetStr);
                    case "Float":
                        return Math.Abs(BitConverter.ToSingle(buffer, index) - float.Parse(targetStr)) < 0.001;
                    default:
                        return false;
                }
            }
            catch { return false; }
        }

        private void GoToAddress_Click(object sender, RoutedEventArgs e)
        {
            if (long.TryParse(TxtAddress.Text, System.Globalization.NumberStyles.HexNumber, null, out long addr))
                MyHexEditor.SetPosition(addr & 0x01FFFFFF, 1);
        }

        private void ResultGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (ResultGrid.SelectedItem is ScanResult res)
                MyHexEditor.SetPosition(res.RawAddress, 1);
        }

        private void FreezeListUI_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (FreezeListUI.SelectedItem is FrozenValue selected)
                TxtEditFrozenValue.Text = selected.Value;
        }

       

        private void BtnManualAdd_Click(object sender, RoutedEventArgs e)
        {
            if (_eeStream == null)
            {
                MessageBox.Show("请先连接 PCSX2");
                return;
            }

            try
            {
                // 1. 解析十六进制地址
                string addrStr = TxtManualAddress.Text.Trim().Replace("0x", "");
                if (!long.TryParse(addrStr, System.Globalization.NumberStyles.HexNumber, null, out long fullAddr))
                {
                    MessageBox.Show("无效的十六进制地址格式");
                    return;
                }

                // 2. 计算相对于 EE 内存开始位置的偏移
                // PS2 内存通常以 0x20000000 开头，我们需要的是 0x0 - 0x01FFFFFF 之间的偏移量
                long rawAddr = fullAddr & 0x01FFFFFF;

                // 3. 获取当前选择的数据类型
                string selectedTag = (ComboDataType.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "4";

                // 4. 加入锁定列表
                if (!_frozenList.Any(f => f.RawAddress == rawAddr))
                {
                    _frozenList.Add(new FrozenValue
                    {
                        RawAddress = rawAddr,
                        Address = $"0x{fullAddr:X8}",
                        TypeTag = selectedTag,
                        Value = TxtManualValue.Text.Trim()
                    });

                    // 提示成功并清空输入
                    TxtManualValue.Clear();
                }
                else
                {
                    MessageBox.Show("该地址已在锁定列表中");
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("手动添加失败: " + ex.Message);
            }
        }

        private void ToggleHexDec_Click(object sender, RoutedEventArgs e)
        {
            if (MyHexEditor == null)
                return;

            // 某些版本中属性名为 DataVisualType
            if (ChkHexDecimal.IsChecked == true)
            {
                MyHexEditor.DataStringVisual = DataVisualType.Hexadecimal;
                ChkHexDecimal.Content = "16进制模式";
            }
            else
            {
                MyHexEditor.DataStringVisual = DataVisualType.Decimal;
                ChkHexDecimal.Content = "10进制模式";
            }
        }


        // 导出内存镜像功能
        private async void ExportMemory_Click(object sender, RoutedEventArgs e)
        {
            if (_eeStream == null)
            {
                MessageBox.Show("请先连接 PCSX2");
                return;
            }

            SaveFileDialog sfd = new SaveFileDialog { Filter = "BIN文件|*.bin", FileName = "PCSX2_Memory_Dump.bin" };
            if (sfd.ShowDialog() == true)
            {
                try
                {
                    // 尝试暂停游戏以获取稳定的镜像（通过发送空格）
                    SendKeys(VK_SPACE);

                    await Task.Run(() => {
                        lock (_eeStream)
                        {
                            using (var fs = File.Create(sfd.FileName))
                            {
                                long originalPosition = _eeStream.Position;
                                _eeStream.Position = 0;
                                _eeStream.CopyTo(fs);
                                _eeStream.Position = originalPosition;
                            }
                        }
                    });

                    // 恢复游戏运行
                    SendKeys(VK_SPACE);
                    MessageBox.Show("内存镜像导出成功！");
                }
                catch (Exception ex)
                {
                    MessageBox.Show("导出失败: " + ex.Message);
                }
            }
        }

        // 模拟按键发送，用于远程控制模拟器暂停
        private void SendKeys(int key)
        {
            var p = Process.GetProcessesByName("pcsx2-qt").FirstOrDefault() ??
                    Process.GetProcessesByName("pcsx2").FirstOrDefault();
            if (p != null)
            {
                PostMessage(p.MainWindowHandle, WM_KEYDOWN, (IntPtr)key, IntPtr.Zero);
                PostMessage(p.MainWindowHandle, WM_KEYUP, (IntPtr)key, IntPtr.Zero);
            }
        }

        private void LockCurrentAddress_Click(object sender, RoutedEventArgs e)
        {
            if (_eeStream == null)
            {
                MessageBox.Show("请先连接 PCSX2");
                return;
            }

            try
            {
                long rawAddr = MyHexEditor.SelectionStart;
                string displayAddr = $"0x{(0x20000000 + rawAddr):X8}";
                string selectedTag = (ComboDataType.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "4";
                string currentValue = GetCurrentValueAt(rawAddr);

                // 5. 查找是否已存在
                var existingItem = _frozenList.FirstOrDefault(f => f.RawAddress == rawAddr);

                if (existingItem == null)
                {
                    // 创建新对象
                    var newItem = new FrozenValue
                    {
                        RawAddress = rawAddr,
                        Address = displayAddr,
                        TypeTag = selectedTag,
                        Value = currentValue
                    };

                    // 添加到集合
                    _frozenList.Add(newItem);

                    // --- 核心优化部分：选中并滚动 ---
                    // 使用 Dispatcher 确保在 UI 渲染新项后再执行操作
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        // 设置为选中项
                        FreezeListUI.SelectedItem = newItem;
                        // 滚动到该项，确保在显示区域
                        FreezeListUI.ScrollIntoView(newItem);
                    }));

                    TxtStatus.Text = $"已锁定地址: {displayAddr}";
                }
                else
                {
                    // 如果已存在，直接选中它并滚动过去，给予视觉反馈
                    FreezeListUI.SelectedItem = existingItem;
                    FreezeListUI.ScrollIntoView(existingItem);
                    MessageBox.Show("该地址已在锁定列表中");
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("添加锁定失败: " + ex.Message);
            }
        }

        private void RefreshHex_Click(object sender, RoutedEventArgs e)
        {
            if (_eeStream == null || MyHexEditor == null)
            {
                MessageBox.Show("请先连接 PCSX2");
                return;
            }

            try
            {
                // 1. 强制清空编辑器内部缓存并重绘
                MyHexEditor.RefreshView();

                // 2. 可选：更新一次快照，确保搜索结果中的“当前值”也是最新的
                UpdateSnapshotSync();

                // 3. 状态栏反馈
                TxtStatus.Text = $"视图已刷新: {DateTime.Now:HH:mm:ss}";
            }
            catch (Exception ex)
            {
                MessageBox.Show("刷新失败: " + ex.Message);
            }
        }

        // 辅助方法：同步刷新当前内存快照
        private void UpdateSnapshotSync()
        {
            if (_eeStream == null)
                return;

            lock (_eeStream)
            {
                if (_lastSnapshot == null)
                    _lastSnapshot = new byte[32 * 1024 * 1024];
                long currentPos = _eeStream.Position;
                _eeStream.Position = 0;
                _eeStream.Read(_lastSnapshot, 0, _lastSnapshot.Length);
                _eeStream.Position = currentPos;
            }
        }


        // 保存功能
        private void SaveFrozenList_Click(object sender, RoutedEventArgs e)
        {
            if (_frozenList.Count == 0)
            {
                MessageBox.Show("当前列表为空，无需保存。");
                return;
            }

            SaveFileDialog sfd = new SaveFileDialog
            {
                Filter = "锁定列表文件 (*.json)|*.json",
                FileName = "PCSX2_Cheats.json"
            };

            if (sfd.ShowDialog() == true)
            {
                try
                {
                    // 将 ObservableCollection 转换为 JSON 字符串
                    string jsonString = JsonSerializer.Serialize(_frozenList, new JsonSerializerOptions { WriteIndented = true });
                    File.WriteAllText(sfd.FileName, jsonString);
                    MessageBox.Show("保存成功！");
                }
                catch (Exception ex)
                {
                    MessageBox.Show("保存失败: " + ex.Message);
                }
            }
        }

        // 载入功能
        private void LoadFrozenList_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog ofd = new OpenFileDialog
            {
                Filter = "锁定列表文件 (*.json)|*.json"
            };

            if (ofd.ShowDialog() == true)
            {
                try
                {
                    string jsonString = File.ReadAllText(ofd.FileName);
                    var loadedData = JsonSerializer.Deserialize<List<FrozenValue>>(jsonString);

                    if (loadedData != null)
                    {
                        // 清空当前列表并导入
                        _frozenList.Clear();
                        foreach (var item in loadedData)
                        {
                            _frozenList.Add(item);
                        }
                        MessageBox.Show($"成功载入 {loadedData.Count} 条锁定项。");
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show("载入失败，请检查文件格式是否正确: " + ex.Message);
                }
            }
        }

        private void FreezeListUI_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            // 1. 获取双击的锁定项
            if (FreezeListUI.SelectedItem is FrozenValue selected)
            {
                if (_eeStream == null)
                    return;

                try
                {
                    // 2. 调用编辑器的跳转方法
                    // rawAddress 已经是 0 - 32MB 的偏移量，直接使用即可
                    MyHexEditor.SetPosition(selected.RawAddress, 1);

                    // 3. 同时更新顶部地址栏显示，保持 UI 同步
                    TxtAddress.Text = (0x20000000 + selected.RawAddress).ToString("X8");

                    // 4. 反馈状态
                    TxtStatus.Text = $"已定位到锁定地址: {selected.Address}";
                }
                catch (Exception ex)
                {
                    MessageBox.Show("定位失败: " + ex.Message);
                }
            }
        }

        private void MenuRemoveFreeze_Click(object sender, RoutedEventArgs e)
        {
            if (FreezeListUI.SelectedItem is FrozenValue val)
            {
                _frozenList.Remove(val);
            }
        }

        private void MenuClearAllFreeze_Click(object sender, RoutedEventArgs e)
        {
            if (MessageBox.Show("确定要清空所有锁定项吗？", "询问", MessageBoxButton.YesNo) == MessageBoxResult.Yes)
            {
                _frozenList.Clear();
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            _freezeTimer?.Stop();
            _eeStream?.Dispose();
            _mmf?.Dispose();
            base.OnClosed(e);
        }
    }
}