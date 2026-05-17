using System.Drawing;
using TmrOverlay.App.Localhost;
using TmrOverlay.App.Overlays.BrowserSources;
using TmrOverlay.App.Overlays.Content;
using TmrOverlay.App.Overlays.Styling;
using TmrOverlay.App.Overlays.TrackMap;
using TmrOverlay.Core.Overlays;
using TmrOverlay.Core.Settings;

namespace TmrOverlay.App.Overlays.SettingsPanel;

internal static class SettingsOverlayTabSections
{
    public sealed record OverlayChromeSettingsRow(
        string Label,
        string TestKey,
        string PracticeKey,
        string QualifyingKey,
        string RaceKey);

    private sealed record BrowserSizeReadoutBinding(
        OverlayDefinition Definition,
        OverlaySettings Settings);

    private sealed class ContentColumnRowBinding
    {
        public required OverlayContentColumnDefinition Definition { get; init; }

        public required Panel Row { get; init; }
    }

    public static int AddOverlayBasics(
        Control parent,
        OverlayDefinition definition,
        OverlaySettings settings,
        int controlTop,
        Action saveAndApply)
    {
        var enabledCheckBox = SettingsUi.CreateCheckBox("Visible", settings.Enabled, 22, controlTop, 220);
        enabledCheckBox.CheckedChanged += (_, _) =>
        {
            settings.Enabled = enabledCheckBox.Checked;
            saveAndApply();
        };
        parent.Controls.Add(enabledCheckBox);

        var optionsTop = controlTop + 46;
        optionsTop = AddScaleOption(parent, definition, settings, optionsTop, saveAndApply);
        optionsTop = AddOpacityOption(parent, definition, settings, optionsTop, saveAndApply);
        return optionsTop;
    }

    public static int AddScaleOption(
        Control parent,
        OverlayDefinition definition,
        OverlaySettings settings,
        int optionsTop,
        Action saveAndApply)
    {
        if (!definition.ShowScaleControl)
        {
            return optionsTop;
        }

        var scaleLabel = SettingsUi.CreateLabel("Scale", 22, optionsTop + 4, 160);
        var scaleInput = new NumericUpDown
        {
            DecimalPlaces = 0,
            Increment = 5,
            Location = new Point(180, optionsTop),
            Maximum = 200,
            Minimum = 60,
            Size = new Size(90, 28),
            TabStop = true,
            TextAlign = HorizontalAlignment.Right,
            Value = (decimal)Math.Round(Math.Clamp(settings.Scale, 0.6d, 2d) * 100d)
        };
        var percentLabel = SettingsUi.CreateLabel("%", 278, optionsTop + 4, 40);
        scaleInput.ValueChanged += (_, _) =>
        {
            settings.Scale = Math.Clamp((double)scaleInput.Value / 100d, 0.6d, 2d);
            settings.Width = ScaleDimension(definition.DefaultWidth, settings.Scale);
            settings.Height = ScaleDimension(definition.DefaultHeight, settings.Scale);
            saveAndApply();
        };

        parent.Controls.Add(scaleLabel);
        parent.Controls.Add(scaleInput);
        parent.Controls.Add(percentLabel);
        return optionsTop + 50;
    }

    private static int AddOpacityOption(
        Control parent,
        OverlayDefinition definition,
        OverlaySettings settings,
        int optionsTop,
        Action saveAndApply)
    {
        if (!definition.ShowOpacityControl)
        {
            return optionsTop;
        }

        var isTrackMap = string.Equals(definition.Id, TrackMapOverlayDefinition.Definition.Id, StringComparison.OrdinalIgnoreCase);
        var minimumOpacity = isTrackMap ? 0d : 0.2d;
        var opacityLabel = SettingsUi.CreateLabel(
            isTrackMap
                ? "Map fill"
                : "Opacity",
            22,
            optionsTop + 4,
            160);
        var opacityInput = new NumericUpDown
        {
            DecimalPlaces = 0,
            Increment = 5,
            Location = new Point(180, optionsTop),
            Maximum = 100,
            Minimum = (decimal)(minimumOpacity * 100d),
            Size = new Size(90, 28),
            TabStop = true,
            TextAlign = HorizontalAlignment.Right,
            Value = (decimal)Math.Round(Math.Clamp(settings.Opacity, minimumOpacity, 1d) * 100d)
        };
        var percentLabel = SettingsUi.CreateLabel("%", 278, optionsTop + 4, 40);
        opacityInput.ValueChanged += (_, _) =>
        {
            settings.Opacity = Math.Clamp((double)opacityInput.Value / 100d, minimumOpacity, 1d);
            saveAndApply();
        };

        parent.Controls.Add(opacityLabel);
        parent.Controls.Add(opacityInput);
        parent.Controls.Add(percentLabel);
        return optionsTop + 50;
    }

    public static void AddChromeSettingsPage(
        Control parent,
        OverlaySettings settings,
        string title,
        IReadOnlyList<OverlayChromeSettingsRow> rows,
        Action saveAndApply)
    {
        parent.Controls.Add(SettingsUi.CreateSectionLabel(title, 18, 18, 500));
        if (rows.Count == 0)
        {
            parent.Controls.Add(SettingsUi.CreateMutedLabel($"No {title.ToLowerInvariant()} controls for this overlay.", 22, 62, 420));
            return;
        }

        parent.Controls.Add(SettingsUi.CreateLabel("Item", 22, 62, 120));
        var sessionColumns = OverlaySettingsSessionColumns.ChromeColumnsFor(settings.Id);
        for (var sessionIndex = 0; sessionIndex < sessionColumns.Count; sessionIndex++)
        {
            parent.Controls.Add(SettingsUi.CreateLabel(
                sessionColumns[sessionIndex].Label,
                ChromeSessionColumnX(sessionIndex),
                62,
                120));
        }

        for (var index = 0; index < rows.Count; index++)
        {
            var row = rows[index];
            var rowTop = 100 + index * 38;
            parent.Controls.Add(SettingsUi.CreateLabel(row.Label, 22, rowTop + 4, 150));
            for (var sessionIndex = 0; sessionIndex < sessionColumns.Count; sessionIndex++)
            {
                var sessionKind = sessionColumns[sessionIndex].Kind;
                AddChromeSessionCheckBox(parent, settings, row, sessionKind, ChromeSessionColumnX(sessionIndex), rowTop, saveAndApply);
            }
        }
    }

    public static void AddLocalhostOptions(
        Control parent,
        OverlayDefinition definition,
        OverlaySettings settings,
        LocalhostOverlayOptions localhostOptions,
        int top,
        Action<string> copyTextToClipboard)
    {
        const int x = 22;
        parent.Controls.Add(SettingsUi.CreateSectionLabel("Localhost browser source", x, top, 500));
        if (BrowserOverlayPageRenderer.TryGetRouteForOverlayId(definition.Id, out var route))
        {
            var url = $"{localhostOptions.Prefix.TrimEnd('/')}{route}";
            parent.Controls.Add(SettingsUi.CreateLabel("URL", x + 4, top + 42, 42));
            parent.Controls.Add(SettingsUi.CreateSelectableValueBox(url, x + 52, top + 36, 360, 30));
            var copyButton = SettingsUi.CreateActionButton("Copy", x + 420, top + 36, 64);
            copyButton.Click += (_, _) => copyTextToClipboard(url);
            parent.Controls.Add(copyButton);
            var sizeText = BrowserSizeText(definition, settings);
            parent.Controls.Add(SettingsUi.CreateLabel("OBS", x + 500, top + 42, 36));
            var sizeBox = SettingsUi.CreateSelectableValueBox(sizeText, x + 538, top + 36, 86, 30);
            sizeBox.Tag = new BrowserSizeReadoutBinding(definition, settings);
            parent.Controls.Add(sizeBox);
            parent.Controls.Add(SettingsUi.CreateMutedLabel("This browser-source route does not require the native overlay to be visible.", x + 4, top + 76, 620));
            return;
        }

        parent.Controls.Add(SettingsUi.CreateMutedLabel("No localhost route is available for this overlay yet.", x + 4, top + 42, 560));
    }

    public static void RefreshBrowserSizeReadouts(Control root)
    {
        foreach (Control child in root.Controls)
        {
            if (child is TextBox { Tag: BrowserSizeReadoutBinding binding } textBox)
            {
                textBox.Text = BrowserSizeText(binding.Definition, binding.Settings);
            }

            if (child.HasChildren)
            {
                RefreshBrowserSizeReadouts(child);
            }
        }
    }

    public static int AddContentColumnSettingsPage(
        Control parent,
        OverlaySettings settings,
        OverlayContentDefinition contentDefinition,
        int top,
        Action saveAndApply)
    {
        var definitions = contentDefinition.Columns;
        if (definitions.Count == 0)
        {
            return AddContentBlockSettings(parent, settings, contentDefinition.Blocks ?? [], top, saveAndApply);
        }

        parent.Controls.Add(SettingsUi.CreateSectionLabel("Content columns", 18, top, 500));
        parent.Controls.Add(SettingsUi.CreateLabel("Order", 22, top + 42, 70));
        parent.Controls.Add(SettingsUi.CreateLabel("Column", 102, top + 42, 150));
        parent.Controls.Add(SettingsUi.CreateLabel("Show", 316, top + 42, 70));
        parent.Controls.Add(SettingsUi.CreateLabel("Width", 386, top + 42, 80));
        parent.Controls.Add(SettingsUi.CreateLabel("Range", 488, top + 42, 120));

        var orderedDefinitions = OrderedColumnDefinitions(settings, definitions);
        var rows = new List<ContentColumnRowBinding>();
        var listPanel = new Panel
        {
            AllowDrop = true,
            Location = new Point(18, top + 70),
            Size = new Size(650, Math.Max(1, orderedDefinitions.Count) * 40 + 2)
        };
        listPanel.DragEnter += (_, e) => SetColumnDragEffect(e);
        listPanel.DragOver += (_, e) => SetColumnDragEffect(e);
        listPanel.DragDrop += (_, e) =>
        {
            var draggedId = ReadDraggedColumnId(e);
            if (draggedId is null || orderedDefinitions.Count == 0)
            {
                return;
            }

            MoveColumn(draggedId, orderedDefinitions[^1].Id, insertAfterTarget: true);
        };

        foreach (var columnDefinition in orderedDefinitions)
        {
            var state = OverlayContentColumnSettings.ToState(settings, columnDefinition, definitions.Count);
            var row = new Panel
            {
                AllowDrop = true,
                BackColor = OverlayTheme.Colors.PanelBackground,
                Size = new Size(640, 34),
                Tag = columnDefinition.Id
            };
            row.DragEnter += (_, e) => SetColumnDragEffect(e);
            row.DragOver += (_, e) => SetColumnDragEffect(e);
            row.DragDrop += (_, e) =>
            {
                var draggedId = ReadDraggedColumnId(e);
                if (draggedId is null)
                {
                    return;
                }

                var point = row.PointToClient(new Point(e.X, e.Y));
                MoveColumn(draggedId, columnDefinition.Id, point.Y >= row.Height / 2);
            };

            var handle = SettingsUi.CreateLabel("::", 12, 5, 36);
            handle.Cursor = Cursors.SizeAll;
            handle.MouseDown += (_, _) => handle.DoDragDrop(columnDefinition.Id, DragDropEffects.Move);
            row.Controls.Add(handle);

            row.Controls.Add(SettingsUi.CreateLabel(ColumnSettingsLabel(columnDefinition), 84, 5, 190));

            var enabled = SettingsUi.CreateCheckBox(
                string.Empty,
                state.Enabled,
                318,
                3,
                32);
            enabled.CheckedChanged += (_, _) =>
            {
                settings.SetBooleanOption(columnDefinition.EnabledKey(settings.Id), enabled.Checked);
                ApplyContentColumnRowStyle(row, enabled.Checked);
                saveAndApply();
            };
            row.Controls.Add(enabled);

            var width = SettingsUi.CreateIntegerInput(
                state.Width,
                columnDefinition.MinimumWidth,
                columnDefinition.MaximumWidth,
                x: 386,
                y: 3);
            width.ValueChanged += (_, _) =>
            {
                settings.SetIntegerOption(
                    columnDefinition.WidthKey(settings.Id),
                    (int)width.Value,
                    columnDefinition.MinimumWidth,
                    columnDefinition.MaximumWidth);
                saveAndApply();
            };
            row.Controls.Add(width);

            row.Controls.Add(SettingsUi.CreateLabel($"{columnDefinition.MinimumWidth}-{columnDefinition.MaximumWidth}", 488, 5, 90));

            listPanel.Controls.Add(row);
            ApplyContentColumnRowStyle(row, state.Enabled);
            rows.Add(new ContentColumnRowBinding
            {
                Definition = columnDefinition,
                Row = row
            });
        }

        parent.Controls.Add(listPanel);
        LayoutRows();
        return AddContentBlockSettings(parent, settings, contentDefinition.Blocks ?? [], top + 86 + listPanel.Height, saveAndApply);

        void MoveColumn(string draggedId, string targetId, bool insertAfterTarget)
        {
            var sourceIndex = orderedDefinitions.FindIndex(definition => string.Equals(definition.Id, draggedId, StringComparison.Ordinal));
            var targetIndex = orderedDefinitions.FindIndex(definition => string.Equals(definition.Id, targetId, StringComparison.Ordinal));
            if (sourceIndex < 0 || targetIndex < 0 || sourceIndex == targetIndex)
            {
                return;
            }

            var moved = orderedDefinitions[sourceIndex];
            var insertIndex = targetIndex + (insertAfterTarget ? 1 : 0);
            orderedDefinitions.RemoveAt(sourceIndex);
            if (sourceIndex < insertIndex)
            {
                insertIndex--;
            }

            orderedDefinitions.Insert(Math.Clamp(insertIndex, 0, orderedDefinitions.Count), moved);
            PersistOrder();
            LayoutRows();
            saveAndApply();
        }

        void PersistOrder()
        {
            for (var index = 0; index < orderedDefinitions.Count; index++)
            {
                var definition = orderedDefinitions[index];
                settings.SetIntegerOption(
                    definition.OrderKey(settings.Id),
                    index + 1,
                    minimum: 1,
                    maximum: Math.Max(1, definitions.Count));
            }
        }

        void LayoutRows()
        {
            for (var index = 0; index < orderedDefinitions.Count; index++)
            {
                var definition = orderedDefinitions[index];
                var row = rows.FirstOrDefault(row => string.Equals(row.Definition.Id, definition.Id, StringComparison.Ordinal));
                if (row is null)
                {
                    continue;
                }

                row.Row.Location = new Point(0, index * 40);
            }
        }
    }

    private static int AddContentBlockSettings(
        Control parent,
        OverlaySettings settings,
        IReadOnlyList<OverlayContentBlockDefinition> blocks,
        int top,
        Action saveAndApply)
    {
        if (blocks.Count == 0)
        {
            return top;
        }

        parent.Controls.Add(SettingsUi.CreateSectionLabel("Content blocks", 18, top + 12, 500));
        var rowTop = top + 52;
        foreach (var block in blocks)
        {
            var row = new Panel
            {
                BackColor = OverlayTheme.Colors.PanelBackground,
                Location = new Point(18, rowTop),
                Size = new Size(650, 76),
                Tag = block.Id
            };

            var enabled = SettingsUi.CreateCheckBox(
                block.Label,
                OverlayContentColumnSettings.BlockEnabled(settings, block),
                12,
                8,
                220);
            enabled.CheckedChanged += (_, _) =>
            {
                settings.SetBooleanOption(block.EnabledOptionKey, enabled.Checked);
                ApplyContentBlockRowStyle(row, enabled.Checked);
                saveAndApply();
            };
            row.Controls.Add(enabled);

            if (block.CountOptionKey is { } countOptionKey && block.CountLabel is { } countLabel)
            {
                row.Controls.Add(SettingsUi.CreateLabel(countLabel, 316, 12, 120));
                var count = SettingsUi.CreateIntegerInput(
                    OverlayContentColumnSettings.BlockCount(settings, block),
                    block.MinimumCount,
                    block.MaximumCount,
                    x: 446,
                    y: 8);
                count.ValueChanged += (_, _) =>
                {
                    settings.SetIntegerOption(
                        countOptionKey,
                        (int)count.Value,
                        block.MinimumCount,
                        block.MaximumCount);
                    saveAndApply();
                };
                row.Controls.Add(count);
            }

            row.Controls.Add(SettingsUi.CreateMutedLabel(block.Description, 36, 42, 580));
            parent.Controls.Add(row);
            ApplyContentBlockRowStyle(row, enabled.Checked);
            rowTop += 86;
        }

        return rowTop;
    }

    private static string ColumnSettingsLabel(OverlayContentColumnDefinition definition)
    {
        return string.IsNullOrWhiteSpace(definition.SettingsLabel)
            ? definition.Label
            : definition.SettingsLabel;
    }

    private static void ApplyContentColumnRowStyle(Panel row, bool enabled)
    {
        row.BackColor = enabled
            ? OverlayTheme.Colors.PanelBackground
            : Color.FromArgb(255, 22, 29, 34);
        foreach (Control child in row.Controls)
        {
            child.ForeColor = enabled
                ? OverlayTheme.Colors.TextControl
                : OverlayTheme.Colors.TextMuted;
        }
    }

    private static void ApplyContentBlockRowStyle(Panel row, bool enabled)
    {
        row.BackColor = enabled
            ? OverlayTheme.Colors.PanelBackground
            : Color.FromArgb(255, 22, 29, 34);
        foreach (Control child in row.Controls)
        {
            child.ForeColor = enabled
                ? OverlayTheme.Colors.TextControl
                : OverlayTheme.Colors.TextMuted;
        }
    }

    private static List<OverlayContentColumnDefinition> OrderedColumnDefinitions(
        OverlaySettings settings,
        IReadOnlyList<OverlayContentColumnDefinition> definitions)
    {
        var definitionsById = definitions.ToDictionary(definition => definition.Id, StringComparer.Ordinal);
        var ordered = OverlayContentColumnSettings.ColumnsFor(settings, definitions)
            .Select(column => definitionsById.TryGetValue(column.Id, out var definition) ? definition : null)
            .OfType<OverlayContentColumnDefinition>()
            .ToList();
        foreach (var definition in definitions.OrderBy(definition => definition.DefaultOrder))
        {
            if (ordered.Any(existing => string.Equals(existing.Id, definition.Id, StringComparison.Ordinal)))
            {
                continue;
            }

            ordered.Add(definition);
        }

        return ordered;
    }

    private static void SetColumnDragEffect(DragEventArgs e)
    {
        e.Effect = ReadDraggedColumnId(e) is null ? DragDropEffects.None : DragDropEffects.Move;
    }

    private static string? ReadDraggedColumnId(DragEventArgs e)
    {
        if (e.Data?.GetData(typeof(string)) is string draggedColumnId)
        {
            return draggedColumnId;
        }

        if (e.Data?.GetDataPresent(DataFormats.UnicodeText) == true)
        {
            return e.Data.GetData(DataFormats.UnicodeText) as string;
        }

        return e.Data?.GetDataPresent(DataFormats.Text) == true
            ? e.Data.GetData(DataFormats.Text) as string
            : null;
    }

    private static string BrowserSizeText(OverlayDefinition definition, OverlaySettings settings)
    {
        var recommendedSize = BrowserOverlayRecommendedSize.For(definition, settings);
        return $"{recommendedSize.Width}x{recommendedSize.Height}";
    }

    public static void AddDescriptorOptions(
        Control parent,
        IReadOnlyList<OverlaySettingsOptionDescriptor> options,
        OverlaySettings settings,
        int top,
        Action saveAndApply)
    {
        for (var index = 0; index < options.Count; index++)
        {
            var option = options[index];
            var x = option.Kind == OverlaySettingsOptionKind.Boolean && index % 2 == 1 ? 260 : 22;
            var y = top + (option.Kind == OverlaySettingsOptionKind.Boolean ? index / 2 : index) * 40;

            if (option.Kind == OverlaySettingsOptionKind.Boolean)
            {
                var checkBox = SettingsUi.CreateCheckBox(
                    option.Label,
                    settings.GetBooleanOption(option.Key, option.BooleanDefault),
                    x,
                    y,
                    220);
                checkBox.CheckedChanged += (_, _) =>
                {
                    settings.SetBooleanOption(option.Key, checkBox.Checked);
                    saveAndApply();
                };
                parent.Controls.Add(checkBox);
                continue;
            }

            if (option.Kind == OverlaySettingsOptionKind.Integer)
            {
                parent.Controls.Add(SettingsUi.CreateLabel(option.Label, 22, y + 4, 120));
                var input = SettingsUi.CreateIntegerInput(
                    settings.GetIntegerOption(option.Key, option.IntegerDefault, option.Minimum, option.Maximum),
                    option.Minimum,
                    option.Maximum,
                    150,
                    y);
                input.ValueChanged += (_, _) =>
                {
                    settings.SetIntegerOption(option.Key, (int)input.Value, option.Minimum, option.Maximum);
                    saveAndApply();
                };
                parent.Controls.Add(input);
            }
        }
    }

    private static void AddChromeSessionCheckBox(
        Control parent,
        OverlaySettings settings,
        OverlayChromeSettingsRow row,
        OverlaySessionKind sessionKind,
        int x,
        int y,
        Action saveAndApply)
    {
        var checkBox = SettingsUi.CreateCheckBox(
            string.Empty,
            OverlaySettingsSessionColumns.ChromeEnabledFor(settings, row, sessionKind),
            x,
            y,
            32);
        checkBox.CheckedChanged += (_, _) =>
        {
            OverlaySettingsSessionColumns.SetChromeEnabledFor(settings, row, sessionKind, checkBox.Checked);
            saveAndApply();
        };
        parent.Controls.Add(checkBox);
    }

    private static int ChromeSessionColumnX(int sessionIndex)
    {
        return 196 + sessionIndex * 130;
    }

    private static int ScaleDimension(int defaultDimension, double scale)
    {
        return Math.Max(80, (int)Math.Round(defaultDimension * Math.Clamp(scale, 0.6d, 2d)));
    }
}
