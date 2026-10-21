using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using WindowWrangler.Core;

namespace WindowWrangler;

// The FancyZones-style picker: monitors across the top, templates and custom layouts below.
// Clicking a card applies it to the selected monitor straight away.
public partial class LayoutsWindow : Window
{
    const string EditGlyph = "", CopyGlyph = "", DeleteGlyph = "";

    readonly Func<WranglerSettings> _settings;
    readonly Action _save;
    List<MonitorInfo> _monitors = [];
    string? _selectedMonitorId;

    // For the XAML previewer.
    public LayoutsWindow() : this(() => new WranglerSettings(), () => { }) { }

    public LayoutsWindow(Func<WranglerSettings> settings, Action save)
    {
        InitializeComponent();
        _settings = settings;
        _save = save;
        // Monitors come and go (docking), so look again whenever the window comes forward.
        Activated += (_, _) => Refresh();
        Refresh();
    }

    WranglerSettings Settings => _settings();
    MonitorInfo? SelectedMonitor => _monitors.FirstOrDefault(m => m.Id == _selectedMonitorId);

    public void Refresh()
    {
        _monitors = Monitors.All();
        if (SelectedMonitor is null)
            _selectedMonitorId = (_monitors.FirstOrDefault(m => m.IsPrimary) ?? _monitors.FirstOrDefault())?.Id;
        BuildMonitors();
        BuildLayouts();
    }

    void BuildMonitors()
    {
        MonitorPanel.Children.Clear();
        foreach (var monitor in _monitors)
        {
            var card = new Border
            {
                Classes = { "monitor" },
                Child = new StackPanel
                {
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Children =
                    {
                        Centered(new TextBlock { Text = monitor.Number.ToString(), FontSize = 24, FontWeight = FontWeight.SemiBold, Classes = { "title" } }),
                        Centered(new TextBlock { Text = $"{monitor.Bounds.Width} x {monitor.Bounds.Height}", FontSize = 12, Classes = { "title" } }),
                        Centered(new TextBlock { Text = $"{monitor.Scale * 100:0}%", FontSize = 10, Classes = { "muted" } }),
                    },
                },
            };
            if (monitor.Id == _selectedMonitorId) card.Classes.Add("selected");
            card.PointerPressed += (_, _) =>
            {
                _selectedMonitorId = monitor.Id;
                BuildMonitors();
                BuildLayouts();
            };
            MonitorPanel.Children.Add(card);
        }
    }

    static TextBlock Centered(TextBlock text)
    {
        text.HorizontalAlignment = HorizontalAlignment.Center;
        return text;
    }

    void BuildLayouts()
    {
        var applied = SelectedMonitor is { } monitor ? Settings.LayoutIdFor(monitor.Id) : null;

        TemplatePanel.Children.Clear();
        TemplatePanel.Children.Add(Card(null, LayoutTemplates.NoneId, "No layout", applied));
        foreach (var template in LayoutTemplates.All)
            TemplatePanel.Children.Add(Card(template, template.Id, template.Name, applied,
                (EditGlyph, "Customize a copy", () => Customize(template))));

        CustomPanel.Children.Clear();
        foreach (var layout in Settings.UserLayouts)
            CustomPanel.Children.Add(Card(layout, layout.Id, layout.Name, applied,
                (EditGlyph, "Edit", () => OpenEditor(layout.Clone())),
                (CopyGlyph, "Duplicate", () => Duplicate(layout)),
                (DeleteGlyph, "Delete", () => _ = Delete(layout))));
        CustomEmpty.IsVisible = !Settings.UserLayouts.Any();
    }

    Border Card(ZoneLayout? layout, string id, string name, string? applied, params (string Glyph, string Tip, Action Act)[] actions)
    {
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2 };
        foreach (var (glyph, tip, act) in actions)
        {
            var button = new Button { Content = glyph, Classes = { "icon" } };
            ToolTip.SetTip(button, tip);
            AutomationProperties.SetName(button, $"{tip} {name}");
            button.Click += (_, e) =>
            {
                e.Handled = true;
                act();
            };
            buttons.Children.Add(button);
        }

        var header = new DockPanel { Height = 30 };
        DockPanel.SetDock(buttons, Dock.Right);
        header.Children.Add(buttons);
        if (layout?.QuickKey is { } key)
        {
            var badge = new Border
            {
                Classes = { "badge" },
                Child = new TextBlock { Text = key.ToString(), FontSize = 11, FontWeight = FontWeight.SemiBold },
            };
            ToolTip.SetTip(badge, $"Win+Ctrl+Alt+{key}");
            DockPanel.SetDock(badge, Dock.Right);
            header.Children.Add(badge);
        }
        header.Children.Add(new TextBlock
        {
            Text = name,
            FontWeight = FontWeight.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Classes = { "title" },
        });

        var body = new Grid
        {
            Children =
            {
                new StackPanel
                {
                    Children = { header, new LayoutPreview { Layout = layout, Height = 108, Margin = new(0, 8, 4, 0) } },
                },
            },
        };
        bool isDefault = id == Settings.DefaultLayoutId;
        if (isDefault)
        {
            // Folded corner, like a dog-eared page, marks the default.
            var corner = new Avalonia.Controls.Shapes.Path
            {
                Data = Geometry.Parse("M 22,0 L 22,22 L 0,22 Z"),
                Classes = { "default-corner" },
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Bottom,
                // Out over the padding to sit in the card's corner.
                Margin = new(0, 0, -10, -14),
            };
            ToolTip.SetTip(corner, "Default layout for monitors you haven't picked one for");
            body.Children.Add(corner);
        }

        var makeDefault = new MenuItem { Header = "Make default", IsEnabled = !isDefault };
        makeDefault.Click += (_, _) => MakeDefault(id);

        var card = new Border
        {
            Classes = { "card" },
            ClipToBounds = true,
            Child = body,
            ContextMenu = new ContextMenu { Items = { makeDefault } },
        };
        if (id == applied) card.Classes.Add("selected");
        card.PointerPressed += (_, e) =>
        {
            if (e.GetCurrentPoint(card).Properties.IsLeftButtonPressed) Apply(id);
        };
        // Same as FancyZones: double-click applies and closes.
        card.DoubleTapped += (_, _) => Close();
        return card;
    }

    void Apply(string layoutId)
    {
        if (SelectedMonitor is not { } monitor) return;
        Settings.MonitorLayouts[monitor.Id] = layoutId;
        _save();
        BuildLayouts();
        if (Settings.FindLayout(layoutId) is { } layout)
            ZoneOverlayWindow.Flash(monitor, layout.GetZones(monitor.WorkArea));
    }

    // Monitors without a layout of their own (new ones included) use the default.
    void MakeDefault(string layoutId)
    {
        Settings.DefaultLayoutId = layoutId;
        _save();
        BuildLayouts();
    }

    void Customize(ZoneLayout template)
    {
        var copy = template.Clone();
        copy.Id = Guid.NewGuid().ToString("N");
        copy.Name = NextName(template.Name);
        OpenEditor(copy);
    }

    void Duplicate(ZoneLayout layout)
    {
        var copy = layout.Clone();
        copy.Id = Guid.NewGuid().ToString("N");
        copy.Name = NextName(layout.Name);
        // Quick keys belong to one layout; the copy would steal it.
        copy.QuickKey = null;
        Settings.AddOrUpdate(copy);
        _save();
        BuildLayouts();
    }

    async Task Delete(ZoneLayout layout)
    {
        if (!await ConfirmDialog.Show(this, $"Delete \"{layout.Name}\"?", "Monitors using it go back to the default layout.", "Delete"))
            return;
        Settings.RemoveLayout(layout.Id);
        _save();
        BuildLayouts();
    }

    void CreateGrid_Click(object? sender, RoutedEventArgs e)
    {
        var layout = GridLayout.CreateColumns(2);
        layout.Name = NextName("Custom layout");
        OpenEditor(layout);
    }

    void CreateCanvas_Click(object? sender, RoutedEventArgs e) =>
        OpenEditor(new CanvasLayout { Name = NextName("Custom layout") });

    string NextName(string baseName)
    {
        var names = Settings.UserLayouts.Select(l => l.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        for (int i = 1; ; i++)
        {
            var name = i == 1 && baseName != "Custom layout" ? $"{baseName} copy" : $"{baseName} {i}";
            if (!names.Contains(name)) return name;
        }
    }

    void OpenEditor(ZoneLayout layout)
    {
        if (SelectedMonitor is not { } monitor) return;

        var editor = new LayoutEditorWindow(layout, monitor);
        editor.Saved += edited =>
        {
            Settings.AddOrUpdate(edited);
            Settings.MonitorLayouts[monitor.Id] = edited.Id;
            _save();
        };
        // Get out of the way while editing, like FancyZones does.
        editor.Closed += (_, _) =>
        {
            Show();
            Activate();
            BuildLayouts();
        };
        Hide();
        editor.Show();
    }
}
