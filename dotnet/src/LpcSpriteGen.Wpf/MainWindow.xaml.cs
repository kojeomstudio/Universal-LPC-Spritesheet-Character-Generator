// Main WPF window — VS 2022-inspired, top-tab layout.
//   Tabs: Generate  |  Browse Parts  |  Current Selections  |  Credits
//
// Drives the Core Renderer + RandomGenerator. All runs log to log/ next to the exe.
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using LpcSpriteGen.Core.Catalog;
using LpcSpriteGen.Core.Characters;
using LpcSpriteGen.Core.Constants;
using LpcSpriteGen.Core.Diagnostics;
using LpcSpriteGen.Core.Zip;
using Microsoft.Win32;
using SkiaSharp;

namespace LpcSpriteGen.Wpf;

public partial class MainWindow : Window
{
    private LpcCatalog? _catalog;
    private Core.Rendering.Renderer? _renderer;
    private readonly Selections _selections = new();
    private string _bodyType = "male";
    private SKBitmap? _lastFullSheet;
    private string? _previewAnimation = "walk";

    // Selection list view-model rows.
    public ObservableCollection<SelectionRow> CurrentSelections { get; } = new();

    public MainWindow()
    {
        // Logging FIRST — even the InitializeComponent() call below leaves a trail.
        Logger.Init(minLevel: LogLevel.Debug, mirrorToConsole: false);
        Logger.Info("WPF GUI starting");
        try
        {
            InitializeComponent();
            DataContext = this;
            SelectionsList.ItemsSource = CurrentSelections;
            // Hook global unhandled-exception handler so we always log a crash.
            AppDomain.CurrentDomain.UnhandledException += (_, e) =>
                Logger.Error("AppDomain unhandled: " + (e.ExceptionObject as Exception)?.Message,
                             e.ExceptionObject as Exception);
            Dispatcher.UnhandledException += (_, e) =>
            {
                Logger.Error("Dispatcher unhandled: " + e.Exception.Message, e.Exception);
                e.Handled = true;
                MessageBox.Show(e.Exception.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            };
            LogPathText.Text = $"log: {Path.GetFileName(Logger.LogFile ?? "")}";
        }
        catch (Exception e)
        {
            Logger.Error("MainWindow ctor failed: " + e.Message, e);
            throw;
        }
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        StatusText.Text = "Loading catalog...";
        Logger.Info("Window_Loaded — loading catalog");
        try
        {
            await Task.Run(() =>
            {
                var repo = ResolveRepoRoot();
                Logger.Info($"repo root resolved: {repo}");
                var loaded = new CatalogLoader(
                    Path.Combine(repo, "sheet_definitions"),
                    Path.Combine(repo, "palette_definitions")).Load();
                _catalog = new LpcCatalog(loaded);
                _renderer = new Core.Rendering.Renderer(_catalog, Path.Combine(repo, "spritesheets"));
                Logger.Info($"catalog loaded: {_catalog.AllItemIds.Count} items");
            });

            BodyTypeBox.ItemsSource = LpcConstants.BodyTypes;
            BodyTypeBox.SelectedItem = _bodyType;
            AnimationBox.ItemsSource = new[] { "spellcast", "thrust", "walk", "slash", "shoot", "hurt", "idle", "jump", "sit", "emote", "run", "combat" };
            AnimationBox.SelectedItem = _previewAnimation;

            PopulateTree();
            ResetToDefaultCharacter();
            await RenderAsync();
        }
        catch (Exception ex)
        {
            Logger.Error("Window_Loaded failed", ex);
            // 카탈로그를 못 찾았을 때 사용자가 조치할 수 있는 구체적인 안내.
            var repo = ResolveRepoRoot();
            var msg = $"Failed to load catalog.\n\nResolved repo root: {repo}\n" +
                      $"Looked for: {repo}\\sheet_definitions and palette_definitions\n\n" +
                      $"Set LPC_REPO_ROOT environment variable to the LPC submodule root " +
                      $"(the folder containing sheet_definitions/).\n\nError: {ex.Message}";
            MessageBox.Show(msg, "Startup error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // ── Catalog tree ────────────────────────────────────────────────────────────────

    private void PopulateTree(string? filter = null)
    {
        if (_catalog == null) return;
        CatalogTree.Items.Clear();
        foreach (var child in _catalog.Tree.Children)
            AddTreeNodes(CatalogTree.Items, child, filter);
    }

    private void AddTreeNodes(ItemCollection parent, CategoryTreeNode node, string? filter)
    {
        bool filtering = !string.IsNullOrWhiteSpace(filter) && filter.Length >= 2;
        var myItems = node.Items
            .Where(id => !filtering ||
                         id.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                         (_catalog!.GetItem(id).IsOk &&
                          _catalog.GetItem(id).Value.Lite.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)))
            .ToList();
        if (filtering && myItems.Count == 0 && node.Children.Count == 0) return;

        var nodeItem = new TreeViewItem { Header = node.Label ?? node.Key, IsExpanded = filtering };
        foreach (var id in myItems)
        {
            var name = _catalog!.GetItem(id).IsOk ? _catalog.GetItem(id).Value.Lite.Name : id;
            nodeItem.Items.Add(new TreeViewItem { Header = name, Tag = id });
        }
        foreach (var child in node.Children)
            AddTreeNodes(nodeItem.Items, child, filter);
        parent.Add(nodeItem);
    }

    private void CatalogItem_Selected(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (CatalogTree.SelectedItem is TreeViewItem { Tag: string itemId } tvi && _catalog != null)
        {
            var r = _catalog.GetItem(itemId);
            if (!r.IsOk) return;
            var item = r.Value;
            var typeName = item.Lite.TypeName;
            _selections[typeName] = new Selection
            {
                ItemId = itemId,
                Name = item.Lite.Name,
                Variant = item.Lite.Variants.FirstOrDefault() ?? "",
                Recolor = item.Lite.Recolors.Length > 0 && item.Lite.Recolors[0].Variants.Length > 0
                    ? item.Lite.Recolors[0].Variants[0]
                    : "",
            };
            Logger.Info($"selected {typeName}={itemId}");
            RefreshSelections();
            // Jump to Generate tab so the user sees the result.
            MainTabs.SelectedIndex = 0;
            _ = RenderAsync();
        }
    }

    private void Search_TextChanged(object sender, TextChangedEventArgs e)
    {
        PopulateTree(SearchBox.Text.Trim());
    }

    // ── Selections panel ─────────────────────────────────────────────────────────────

    private void RefreshSelections()
    {
        CurrentSelections.Clear();
        foreach (var (k, sel) in _selections)
            CurrentSelections.Add(new SelectionRow { TypeName = k, DisplayName = sel.Name, Selection = sel });
        SelectionCountText.Text = $"{_selections.Count} items";
        UpdateCreditsBox();
    }

    private void RemoveSelection_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button b && b.Tag is string typeName)
        {
            _selections.Remove(typeName);
            Logger.Info($"removed {typeName}");
            RefreshSelections();
            _ = RenderAsync();
        }
    }

    private void ClearAll_Click(object sender, RoutedEventArgs e)
    {
        ResetToDefaultCharacter();
        _ = RenderAsync();
    }

    private void ResetToDefaultCharacter()
    {
        _selections.Clear();
        _selections["body"] = new Selection { ItemId = "body", Recolor = "light", Name = "Body color (light)" };
        _selections["head"] = new Selection { ItemId = "heads_human_male", Recolor = "light", Name = "Human Male (light)" };
        _selections["expression"] = new Selection { ItemId = "face_neutral", Recolor = "light", Name = "Neutral" };
        RefreshSelections();
    }

    // ── Render ───────────────────────────────────────────────────────────────────────

    private async Task RenderAsync()
    {
        if (_renderer == null) return;
        StatusText.Text = "Rendering...";
        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            var sheet = await Task.Run(() => _renderer.RenderCharacterAsync(_selections, _bodyType));
            _lastFullSheet?.Dispose();
            _lastFullSheet = sheet;
            UpdatePreview();
            sw.Stop();
            RenderTimeText.Text = $"Render: {sw.ElapsedMilliseconds}ms";
            StatusText.Text = "Ready";
        }
        catch (Exception e)
        {
            Logger.Error("Render failed", e);
            StatusText.Text = "Render failed";
            MessageBox.Show(e.Message, "Render error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void UpdatePreview()
    {
        if (_lastFullSheet == null) return;
        var offsets = AnimationTables.AnimationOffsets;
        var configs = AnimationTables.AnimationConfigs;
        int row;
        int numRows;
        if (offsets.TryGetValue(_previewAnimation ?? "walk", out var y))
        {
            row = y / LpcConstants.FrameSize;
            numRows = 4;
        }
        else if (configs.TryGetValue(_previewAnimation ?? "walk", out var cfg))
        {
            row = cfg.Row;
            numRows = cfg.Num;
        }
        else { row = 8; numRows = 4; }

        int y0 = row * LpcConstants.FrameSize;
        int y1 = (row + numRows) * LpcConstants.FrameSize;
        int cropW = Math.Min(LpcConstants.SheetWidth, _lastFullSheet.Width);
        int cropH = Math.Min(y1 - y0, _lastFullSheet.Height - y0);
        if (cropW <= 0 || cropH <= 0) return;

        var cropRect = SKRectI.Create(0, y0, cropW, cropH);
        using var animBlock = ExtractSubset(_lastFullSheet, cropRect);
        PreviewImage.Source = ToBitmapImage(animBlock);
    }

    /// <summary>Wrapper around SKBitmap.ExtractSubset (instance API writes into a dest).</summary>
    private static SKBitmap ExtractSubset(SKBitmap src, SKRectI rect)
    {
        var dst = new SKBitmap(rect.Width, rect.Height, src.ColorType, src.AlphaType);
        if (!src.ExtractSubset(dst, rect))
        {
            dst.Dispose();
            throw new InvalidOperationException($"ExtractSubset failed for rect {rect}");
        }
        return dst;
    }

    private static BitmapImage ToBitmapImage(SKBitmap bmp)
    {
        var bi = new BitmapImage();
        using var image = SKImage.FromBitmap(bmp);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        using var ms = new MemoryStream();
        data.SaveTo(ms);
        ms.Position = 0;
        bi.BeginInit();
        bi.CacheOption = BitmapCacheOption.OnLoad;
        bi.StreamSource = ms;
        bi.EndInit();
        bi.Freeze();
        return bi;
    }

    // ── Toolbar / menu commands ──────────────────────────────────────────────────────

    private void BodyType_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // Skip during initialization (SelectedItem not yet set up).
        if (!IsLoaded || BodyTypeBox.SelectedItem is not string s || s == _bodyType) return;
        _bodyType = s;
        Logger.Info($"bodyType → {_bodyType}");
        _ = RenderAsync();
    }

    private void BodyTypeMenu_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem mi && mi.Tag is string s)
        {
            _bodyType = s;
            BodyTypeBox.SelectedItem = s;
            _ = RenderAsync();
        }
    }

    private void Animation_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded || AnimationBox.SelectedItem is not string s) return;
        _previewAnimation = s;
        UpdatePreview();
    }

    private void Zoom_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        // CRASH GUARD: Slider fires ValueChanged during InitializeComponent BEFORE ZoomLabel exists.
        if (!IsInitialized || ZoomLabel == null) return;
        var zoom = ZoomSlider.Value;
        PreviewImage.LayoutTransform = new System.Windows.Media.ScaleTransform(zoom, zoom);
        ZoomLabel.Text = $"{zoom * 100:0}%";
    }

    private void Preview_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        // Ctrl+wheel = zoom (matches desktop conventions; replaces the JS pinch-to-zoom).
        if (Keyboard.Modifiers != ModifierKeys.Control) return;
        var delta = e.Delta > 0 ? 0.25 : -0.25;
        var v = Math.Clamp(ZoomSlider.Value + delta, ZoomSlider.Minimum, ZoomSlider.Maximum);
        ZoomSlider.Value = v;
        e.Handled = true;
    }

    private async void Randomize_Click(object sender, RoutedEventArgs e) => await RandomizeAsync(null);

    private async void RandomizeSeeded_Click(object sender, RoutedEventArgs e)
    {
        var input = InteractionDialog("Randomize with seed", "Seed (integer):", "");
        if (int.TryParse(input, out var seed)) await RandomizeAsync(seed);
    }

    private async Task RandomizeAsync(int? seed)
    {
        if (_catalog == null) return;
        StatusText.Text = "Randomizing...";
        Logger.Info($"randomize seed={seed}");
        var result = await Task.Run(() => new RandomGenerator(_catalog).Generate(seed, _bodyType));
        _selections.Clear();
        foreach (var (k, v) in result.Selections) _selections[k] = v;
        RefreshSelections();
        await RenderAsync();
    }

    private void New_Click(object sender, RoutedEventArgs e)
    {
        ResetToDefaultCharacter();
        _ = RenderAsync();
    }

    private async void Open_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog { Filter = "Selections JSON (*.json)|*.json|All files (*.*)|*.*" };
        if (dlg.ShowDialog() != true) return;
        try
        {
            var payload = JsonSerializer.Deserialize<SelectionsPayload>(File.ReadAllText(dlg.FileName),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (payload == null) return;
            _selections.Clear();
            foreach (var (k, v) in payload.Selections) _selections[k] = v;
            if (payload.BodyType != null) { _bodyType = payload.BodyType; BodyTypeBox.SelectedItem = _bodyType; }
            RefreshSelections();
            Logger.Info($"opened {dlg.FileName}");
            await RenderAsync();
        }
        catch (Exception ex)
        {
            Logger.Error("Open failed", ex);
            MessageBox.Show(ex.Message, "Open failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new SaveFileDialog { Filter = "Selections JSON (*.json)|*.json", FileName = "character.json" };
        if (dlg.ShowDialog() != true) return;
        var payload = new { bodyType = _bodyType, selections = _selections };
        File.WriteAllText(dlg.FileName, JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }));
        StatusText.Text = $"Saved {dlg.FileName}";
        Logger.Info($"saved {dlg.FileName}");
    }

    private void ExportPng_Click(object sender, RoutedEventArgs e)
    {
        if (_lastFullSheet == null) return;
        var dlg = new SaveFileDialog { Filter = "PNG (*.png)|*.png", FileName = "character.png" };
        if (dlg.ShowDialog() != true) return;
        try
        {
            using (var image = SKImage.FromBitmap(_lastFullSheet))
            using (var data = image.Encode(SKEncodedImageFormat.Png, 100))
            using (var fs = File.Create(dlg.FileName))
            {
                data.SaveTo(fs);
            }
            // Always emit credits alongside (legally required attribution).
            var creditsPath = Path.ChangeExtension(dlg.FileName, ".credits.txt");
            File.WriteAllText(creditsPath, BuildCreditsText());
            StatusText.Text = $"Exported {dlg.FileName} (+ credits)";
            Logger.Info($"exported PNG {dlg.FileName}");
        }
        catch (Exception ex)
        {
            Logger.Error("ExportPng failed", ex);
            MessageBox.Show(ex.Message, "Export failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void ExportZipAnim_Click(object sender, RoutedEventArgs e) => await ExportZipAsync(ZipLayout.ByAnimation);
    private async void ExportZipFrame_Click(object sender, RoutedEventArgs e) => await ExportZipAsync(ZipLayout.ByFrame);

    private async Task ExportZipAsync(ZipLayout layout)
    {
        if (_catalog == null || _renderer == null) return;
        var dlg = new SaveFileDialog { Filter = "ZIP (*.zip)|*.zip", FileName = $"character-{layout}.zip" };
        if (dlg.ShowDialog() != true) return;
        try
        {
            StatusText.Text = $"Building {layout} zip...";
            var exporter = new ZipExporter(_catalog, _renderer);
            await Task.Run(() => exporter.ExportAsync(_selections, _bodyType, layout, dlg.FileName));
            StatusText.Text = $"Exported {dlg.FileName}";
            Logger.Info($"exported {layout} zip {dlg.FileName}");
        }
        catch (Exception ex)
        {
            Logger.Error("ExportZip failed", ex);
            MessageBox.Show(ex.Message, "Export failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ExportCredits_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new SaveFileDialog { Filter = "Text (*.txt)|*.txt", FileName = "credits.txt" };
        if (dlg.ShowDialog() != true) return;
        File.WriteAllText(dlg.FileName, BuildCreditsText());
        StatusText.Text = $"Exported {dlg.FileName}";
    }

    private void CopyCredits_Click(object sender, RoutedEventArgs e)
    {
        try { Clipboard.SetText(BuildCreditsText()); StatusText.Text = "Credits copied"; }
        catch (Exception ex) { Logger.Error("clipboard failed", ex); }
    }

    private void UpdateCreditsBox()
    {
        if (CreditsBox != null) CreditsBox.Text = BuildCreditsText();
    }

    private string BuildCreditsText()
    {
        if (_catalog == null) return "";
        var sb = new StringBuilder();
        sb.AppendLine("LPC Sprite Generator — Credits");
        sb.AppendLine(new string('-', 40));
        sb.AppendLine();
        var seen = new HashSet<string>();
        foreach (var (_, sel) in _selections)
        {
            var r = _catalog.GetItem(sel.ItemId);
            if (!r.IsOk) continue;
            foreach (var c in r.Value.Credits)
                foreach (var a in c.Authors)
                {
                    var lic = c.Licenses.Length > 0 ? c.Licenses[0] : "";
                    if (seen.Add(a + "|" + lic))
                        sb.AppendLine($"- {a} ({(string.IsNullOrEmpty(lic) ? "CC-BY" : lic)}): {r.Value.Lite.Name}");
                }
        }
        sb.AppendLine();
        sb.AppendLine("Asset licenses (CC-BY / CC-BY-SA / GPL / OGA-BY) require attribution.");
        return sb.ToString();
    }

    private void OpenLogFolder_Click(object sender, RoutedEventArgs e)
    {
        if (Logger.LogDirectory != null)
            try { System.Diagnostics.Process.Start("explorer.exe", Logger.LogDirectory); }
            catch (Exception ex) { Logger.Error("open log folder failed", ex); }
    }

    private void ShowCliHelp_Click(object sender, RoutedEventArgs e)
    {
        MessageBox.Show(
            "Headless CLI is at dotnet/src/LpcSpriteGen.Headless/.\n\n" +
            "Examples:\n" +
            "  lpcsprites --random --output out.png\n" +
            "  lpcsprites --random --seed 42 --count 10 --output-dir ./out\n" +
            "  lpcsprites --selections in.json --output out.png\n" +
            "  lpcsprites --dump-catalog --indent > catalog.json\n" +
            "  lpcsprites --describe body --indent\n" +
            "  lpcsprites --validate-selections in.json\n\n" +
            "Run lpcsprites --help for the full reference.",
            "Headless CLI", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void About_Click(object sender, RoutedEventArgs e)
    {
        MessageBox.Show(
            "LPC Sprite Generator (C# / WPF edition)\n\n" +
            "Renders Universal LPC Sprite Sheet characters from modular parts.\n" +
            "Original LPC assets © their authors (CC-BY/CC-BY-SA/GPL/OGA-BY).\n\n" +
            $"Log: {Logger.LogFile ?? "(none)"}",
            "About", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void Exit_Click(object sender, RoutedEventArgs e) => Close();

    // ── Helpers ──────────────────────────────────────────────────────────────────────

    private static string ResolveRepoRoot() => Core.Diagnostics.Paths.ResolveRepoRoot();

    private static string InteractionDialog(string title, string prompt, string defaultValue)
    {
        var dlg = new Window
        {
            Title = title, Width = 360, Height = 160,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = System.Windows.Application.Current.FindResource("VsBackgroundBrush") as System.Windows.Media.Brush,
        };
        var panel = new StackPanel { Margin = new Thickness(12) };
        var tb = new TextBlock { Text = prompt, Margin = new Thickness(0, 0, 0, 6),
                                 Foreground = System.Windows.Application.Current.FindResource("VsTextPrimaryBrush") as System.Windows.Media.Brush };
        panel.Children.Add(tb);
        var box = new TextBox { Text = defaultValue };
        panel.Children.Add(box);
        var ok = new Button { Content = "OK", Width = 80, HorizontalAlignment = HorizontalAlignment.Right,
                              Margin = new Thickness(0, 8, 0, 0) };
        panel.Children.Add(ok);
        ok.Click += (s, e) => { dlg.DialogResult = true; dlg.Close(); };
        dlg.Content = panel;
        return dlg.ShowDialog() == true ? box.Text : "";
    }

    private sealed class SelectionsPayload
    {
        public Dictionary<string, Selection> Selections { get; set; } = new();
        public string? BodyType { get; set; }
    }
}

/// <summary>One row in the Current Selections list.</summary>
public sealed class SelectionRow
{
    public string TypeName { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public Selection Selection { get; set; } = new();
    public string DetailLine =>
        $"{TypeName} • {Selection.ItemId}" +
        (!string.IsNullOrEmpty(Selection.Recolor) ? $" • recolor: {Selection.Recolor}" : "") +
        (!string.IsNullOrEmpty(Selection.Variant) ? $" • variant: {Selection.Variant}" : "");
}
