using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace LumosExecutorTouchGrid
{
    /// <summary>
    /// Table: one row per executor page, one column per executor. All rows / columns are star sized,
    /// so the table always fills the window; tiles scale their content with the cell size.
    /// </summary>
    internal sealed class TouchGridView : UserControl
    {
        private static readonly Brush WindowBackground = Frozen(Color.FromRgb(0x1B, 0x1B, 0x1D));
        private static readonly Brush HeaderText = Frozen(Color.FromRgb(0xC8, 0xC8, 0xCE));
        private static readonly Brush HeaderBackground = Frozen(Color.FromRgb(0x25, 0x25, 0x28));

        private readonly Grid _grid = new Grid();
        private readonly TextBlock _placeholder = new TextBlock
        {
            Text = "Keine Executor-Pages (kein Projekt geladen?)",
            Foreground = HeaderText,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            FontSize = 16
        };

        private readonly Dictionary<(int row, int col), ExecutorTile> _tiles = new Dictionary<(int, int), ExecutorTile>();
        private readonly List<TextBlock> _headers = new List<TextBlock>();
        private string _structureKey;

        public GridModel Model { get; }

        public TouchGridView(GridModel model)
        {
            Model = model;
            Background = WindowBackground;
            Content = new Grid { Children = { _grid, _placeholder } };
            Model.SnapshotReady += s => Dispatcher.BeginInvoke(DispatcherPriority.Normal, (Action)(() => Apply(s)));
            IsVisibleChanged += (s, e) => Model.Visible = IsVisible;
        }

        private void Apply(GridSnapshot snap)
        {
            string key = snap.StructureKey;
            if (key != _structureKey)
            {
                Rebuild(snap);
                _structureKey = key;
            }
            _placeholder.Visibility = snap.Rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            for (int r = 0; r < snap.Rows.Count; r++)
            {
                RowData row = snap.Rows[r];
                _headers[r].Text = row.PageNumber + "\n" + row.PageName;
                for (int c = 0; c < snap.Columns; c++)
                {
                    if (_tiles.TryGetValue((r, c), out ExecutorTile tile))
                    {
                        tile.Apply(c < row.Cells.Count ? row.Cells[c] : null);
                    }
                }
            }
        }

        public void ReleaseAll()
        {
            foreach (ExecutorTile t in _tiles.Values)
            {
                t.ForceRelease();
            }
        }

        private void Rebuild(GridSnapshot snap)
        {
            ReleaseAll();
            _grid.Children.Clear();
            _grid.RowDefinitions.Clear();
            _grid.ColumnDefinitions.Clear();
            _tiles.Clear();
            _headers.Clear();

            int cols = snap.Columns;
            // page header column (narrow) + executor columns
            _grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.45, GridUnitType.Star), MinWidth = 40 });
            for (int c = 0; c < cols; c++)
            {
                _grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            }
            for (int r = 0; r < snap.Rows.Count; r++)
            {
                _grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

                var header = new TextBlock
                {
                    Foreground = HeaderText,
                    FontWeight = FontWeights.SemiBold,
                    TextAlignment = TextAlignment.Center,
                    TextWrapping = TextWrapping.Wrap,
                    FontSize = 12,
                    Margin = new Thickness(2),
                    VerticalAlignment = VerticalAlignment.Center
                };
                var headerHost = new Border
                {
                    Background = HeaderBackground,
                    Margin = new Thickness(2),
                    CornerRadius = new CornerRadius(3),
                    Child = new Viewbox { Stretch = Stretch.Uniform, StretchDirection = StretchDirection.DownOnly, Child = header }
                };
                Grid.SetRow(headerHost, r);
                Grid.SetColumn(headerHost, 0);
                _grid.Children.Add(headerHost);
                _headers.Add(header);

                for (int c = 0; c < cols; c++)
                {
                    var tile = new ExecutorTile();
                    Grid.SetRow(tile, r);
                    Grid.SetColumn(tile, c + 1);
                    _grid.Children.Add(tile);
                    _tiles[(r, c)] = tile;
                }
            }
        }

        private static Brush Frozen(Color c)
        {
            var b = new SolidColorBrush(c);
            b.Freeze();
            return b;
        }
    }
}
