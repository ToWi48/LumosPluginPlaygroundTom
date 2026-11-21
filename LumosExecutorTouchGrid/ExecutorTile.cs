using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Lumos.GUI.Facade.Executor;
using LumosLIB.Kernel.Log;

namespace LumosExecutorTouchGrid
{
    /// <summary>
    /// One pushable executor field. Press = executor button 1 down, release = button 1 up
    /// (so Go / Toggle / Flash / Temp all behave like on the executor itself). Touch and mouse, multi-touch capable.
    /// The content is laid out at a fixed design size inside a Viewbox and scales with the cell.
    /// </summary>
    internal sealed class ExecutorTile : Border
    {
        private static readonly ILumosLog Log = LumosLogger.getInstance(nameof(ExecutorTile));

        private static readonly Brush TileBackground = Frozen(Color.FromRgb(0x2B, 0x2B, 0x2E));
        private static readonly Brush EmptyBackground = Frozen(Color.FromRgb(0x22, 0x22, 0x24));
        private static readonly Brush TileBorder = Frozen(Color.FromRgb(0x3C, 0x3C, 0x42));
        private static readonly Brush DimText = Frozen(Color.FromRgb(0x9A, 0x9A, 0xA0));
        private static readonly Brush InfoText = Frozen(Color.FromRgb(0xD8, 0xD8, 0xDC));
        private static readonly Color DefaultColor = Color.FromRgb(0xB0, 0xB0, 0xB8);

        private readonly Border _colorBar = new Border { Width = 4, HorizontalAlignment = HorizontalAlignment.Left };
        private readonly Border _activeBar = new Border { Width = 8, HorizontalAlignment = HorizontalAlignment.Right, Visibility = Visibility.Collapsed };
        private readonly TextBlock _title = new TextBlock { FontSize = 15, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
        private readonly TextBlock _type = new TextBlock { FontSize = 9.5, FontWeight = FontWeights.Bold, Foreground = DimText, TextTrimming = TextTrimming.CharacterEllipsis };
        private readonly TextBlock _info = new TextBlock { FontSize = 11, Foreground = InfoText, TextTrimming = TextTrimming.CharacterEllipsis };
        private readonly TextBlock _counter = new TextBlock { FontSize = 9.5, Foreground = DimText, HorizontalAlignment = HorizontalAlignment.Left };
        private readonly TextBlock _button = new TextBlock { FontSize = 9.5, Foreground = DimText, HorizontalAlignment = HorizontalAlignment.Right };

        private CellData _data;
        private IExecutorFacade _pressedExecutor;
        private int _pressCount;

        public ExecutorTile()
        {
            CornerRadius = new CornerRadius(3);
            BorderThickness = new Thickness(1);
            BorderBrush = TileBorder;
            Background = TileBackground;
            Margin = new Thickness(2);
            SnapsToDevicePixels = true;
            ClipToBounds = true;
            IsManipulationEnabled = false;
            Stylus.SetIsPressAndHoldEnabled(this, false); // no right-click circle on long press
            Stylus.SetIsFlicksEnabled(this, false);

            // fixed design surface (170 x 84), scaled uniformly to the cell
            var text = new Grid { Width = 170, Height = 84, Margin = new Thickness(0) };
            text.RowDefinitions.Add(new RowDefinition { Height = new GridLength(24) });
            text.RowDefinitions.Add(new RowDefinition { Height = new GridLength(16) });
            text.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            text.RowDefinitions.Add(new RowDefinition { Height = new GridLength(16) });
            var pad = new Thickness(10, 0, 12, 0);
            _title.Margin = new Thickness(10, 5, 12, 0);
            _type.Margin = pad;
            _info.Margin = pad;
            _info.VerticalAlignment = VerticalAlignment.Center;
            var footer = new Grid { Margin = new Thickness(10, 0, 12, 3) };
            footer.Children.Add(_counter);
            footer.Children.Add(_button);
            Grid.SetRow(_title, 0);
            Grid.SetRow(_type, 1);
            Grid.SetRow(_info, 2);
            Grid.SetRow(footer, 3);
            text.Children.Add(_title);
            text.Children.Add(_type);
            text.Children.Add(_info);
            text.Children.Add(footer);

            var root = new Grid();
            root.Children.Add(new Viewbox { Stretch = Stretch.Uniform, Child = text, HorizontalAlignment = HorizontalAlignment.Left });
            root.Children.Add(_colorBar);
            root.Children.Add(_activeBar);
            Child = root;
        }

        public void Apply(CellData d)
        {
            if (d != null && d.Equals(_data))
            {
                _data = d; // keep the newest facade reference
                return;
            }
            _data = d;
            if (d == null || d.Empty)
            {
                Background = EmptyBackground;
                Opacity = 0.45;
                _title.Text = d?.Title ?? string.Empty;
                _title.Foreground = DimText;
                _type.Text = _info.Text = _counter.Text = _button.Text = string.Empty;
                _colorBar.Background = null;
                _activeBar.Visibility = Visibility.Collapsed;
                Cursor = null;
                return;
            }
            Opacity = 1;
            Cursor = Cursors.Hand;
            Color c = d.Color.A == 0 || (d.Color.R | d.Color.G | d.Color.B) == 0 ? DefaultColor : Color.FromRgb(d.Color.R, d.Color.G, d.Color.B);
            var colorBrush = Frozen(c);
            _title.Text = d.Title ?? string.Empty;
            _title.Foreground = colorBrush;
            _type.Text = d.TypeLabel ?? string.Empty;
            _info.Text = d.Info ?? string.Empty;
            _counter.Text = d.Counter ?? string.Empty;
            _button.Text = d.ButtonLabel ?? string.Empty;
            _colorBar.Background = colorBrush;
            _activeBar.Background = colorBrush;
            _activeBar.Visibility = d.Active ? Visibility.Visible : Visibility.Collapsed;
            UpdateBackground();
        }

        private void UpdateBackground()
        {
            if (_data == null || _data.Empty)
            {
                return;
            }
            Color c = _data.Color.A == 0 ? DefaultColor : _data.Color;
            if (_pressCount > 0)
            {
                Background = Frozen(Color.FromArgb(0x90, c.R, c.G, c.B));
            }
            else if (_data.Active)
            {
                Background = Frozen(Blend(Color.FromRgb(0x2B, 0x2B, 0x2E), c, 0.28));
            }
            else
            {
                Background = TileBackground;
            }
        }

        // ------------------------------------------------------------------ input

        protected override void OnTouchDown(TouchEventArgs e)
        {
            e.Handled = true;
            CaptureTouch(e.TouchDevice);
            Press();
        }

        protected override void OnTouchUp(TouchEventArgs e)
        {
            e.Handled = true;
            ReleaseTouchCapture(e.TouchDevice);
            Release();
        }

        protected override void OnLostTouchCapture(TouchEventArgs e)
        {
            base.OnLostTouchCapture(e);
            // capture lost without TouchUp (window deactivated, ...): make sure the button is released
            if (_pressCount > 0 && TouchesCaptured.GetEnumerator().MoveNext() == false)
            {
                _pressCount = 1;
                Release();
            }
        }

        protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
        {
            if (e.StylusDevice != null)
            {
                return; // promoted from touch, already handled
            }
            e.Handled = true;
            CaptureMouse();
            Press();
        }

        protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
        {
            if (e.StylusDevice != null)
            {
                return;
            }
            e.Handled = true;
            ReleaseMouseCapture(); // -> OnLostMouseCapture
        }

        protected override void OnLostMouseCapture(MouseEventArgs e)
        {
            base.OnLostMouseCapture(e);
            if (e.StylusDevice == null)
            {
                Release();
            }
        }

        /// <summary>Releases button 1 if the tile is held down (tile is about to be removed).</summary>
        public void ForceRelease()
        {
            if (_pressCount > 0)
            {
                _pressCount = 1;
                Release();
            }
        }

        private void Press()
        {
            if (_data == null || _data.Empty || _data.Executor == null)
            {
                return;
            }
            _pressCount++;
            if (_pressCount == 1)
            {
                _pressedExecutor = _data.Executor;
                SendButton(_pressedExecutor, true);
            }
            UpdateBackground();
        }

        private void Release()
        {
            if (_pressCount == 0)
            {
                return;
            }
            _pressCount--;
            if (_pressCount == 0 && _pressedExecutor != null)
            {
                SendButton(_pressedExecutor, false);
                _pressedExecutor = null;
            }
            UpdateBackground();
        }

        /// <summary>Kernel call off the UI thread, order of down/up is kept per tile by chaining.</summary>
        private Task _sendChain = Task.CompletedTask;

        private void SendButton(IExecutorFacade ex, bool active)
        {
            _sendChain = _sendChain.ContinueWith(_ =>
            {
                try
                {
                    ex.button1(active);
                }
                catch (Exception e)
                {
                    Log.Debug("TouchGrid: button1({0}) on {1} failed: {2}", active, ex.Name, e.Message);
                }
            }, TaskScheduler.Default);
        }

        // ------------------------------------------------------------------ helpers

        private static Brush Frozen(Color c)
        {
            var b = new SolidColorBrush(c);
            b.Freeze();
            return b;
        }

        private static Color Blend(Color a, Color b, double t)
        {
            return Color.FromRgb(
                (byte)(a.R + (b.R - a.R) * t),
                (byte)(a.G + (b.G - a.G) * t),
                (byte)(a.B + (b.B - a.B) * t));
        }
    }
}
