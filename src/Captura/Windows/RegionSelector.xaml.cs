﻿using Captura.Models;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using ManagedBass;
using Screna;
using Color = System.Windows.Media.Color;

namespace Captura
{
    public partial class RegionSelector
    {
        readonly IVideoSourcePicker _videoSourcePicker;

        bool _widthBoxChanging, _heightBoxChanging, _resizing;

        DispatcherTimer _followTimer;

        public RegionSelector(IVideoSourcePicker VideoSourcePicker)
        {
            _videoSourcePicker = VideoSourcePicker;

            InitializeComponent();

            // Prevent Closing by User
            Closing += (S, E) => E.Cancel = true;

            InitDimensionBoxes();

            ModesBox.ItemsSource = new[]
            {
                new KeyValuePair<InkCanvasEditingMode, string>(InkCanvasEditingMode.None, "Pointer"),
                new KeyValuePair<InkCanvasEditingMode, string>(InkCanvasEditingMode.Ink, "Pencil"),
                new KeyValuePair<InkCanvasEditingMode, string>(InkCanvasEditingMode.EraseByPoint, "Eraser"),
                new KeyValuePair<InkCanvasEditingMode, string>(InkCanvasEditingMode.EraseByStroke, "Stroke Eraser")
            };

            ModesBox.SelectedIndex = 0;
            ColorPicker.SelectedColor = Color.FromRgb(27, 27, 27);
            SizeBox.Value = 10;

            InkCanvas.DefaultDrawingAttributes.FitToCurve = true;

            _followTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(100)
            };

            _followTimer.Tick += (Sender, Args) =>
            {
                var curPos = MouseCursor.CursorPosition;

                var region = SelectedRegion;

                if (Rectangle.Inflate(region, -100, -100).Contains(curPos))
                    return;

                var deskRect = WindowProvider.DesktopRectangle;

                var left = (curPos.X - region.Width / 2).Clip(deskRect.Left, deskRect.Right - region.Width) / Dpi.X;
                var top = (curPos.Y - region.Height / 2).Clip(deskRect.Top, deskRect.Bottom - region.Height) / Dpi.Y;

                BeginAnimation(LeftProperty, new DoubleAnimation(left, new Duration(TimeSpan.FromMilliseconds(300))));
                BeginAnimation(TopProperty, new DoubleAnimation(top, new Duration(TimeSpan.FromMilliseconds(300))));
            };

            Loaded += (S, E) => _followTimer.Start();
        }

        void SizeBox_OnValueChanged(object Sender, RoutedPropertyChangedEventArgs<object> E)
        {
            if (InkCanvas != null && E.NewValue is int i)
                InkCanvas.DefaultDrawingAttributes.Height = InkCanvas.DefaultDrawingAttributes.Width = i;
        }

        void ModesBox_OnSelectionChanged(object Sender, SelectionChangedEventArgs E)
        {
            if (ModesBox.SelectedValue is InkCanvasEditingMode mode)
            {
                InkCanvas.EditingMode = mode;

                if (mode == InkCanvasEditingMode.Ink)
                {
                    InkCanvas.UseCustomCursor = true;
                    InkCanvas.Cursor = Cursors.Pen;
                }
                else InkCanvas.UseCustomCursor = false;

                InkCanvas.Background = new SolidColorBrush(mode == InkCanvasEditingMode.None
                    ? Colors.Transparent
                    : Color.FromArgb(1, 0, 0, 0));
            }
        }

        void ColorPicker_OnSelectedColorChanged(object Sender, RoutedPropertyChangedEventArgs<Color?> E)
        {
            if (E.NewValue != null && InkCanvas != null)
                InkCanvas.DefaultDrawingAttributes.Color = E.NewValue.Value;
        }

        const int LeftOffset = 3,
            TopOffset = 3;

        Rectangle? _region;
        
        void InitDimensionBoxes()
        {
            WidthBox.Minimum = (int)((Region.MinWidth - LeftOffset * 2) * Dpi.X);
            HeightBox.Minimum = (int)((Region.MinHeight - TopOffset * 2) * Dpi.Y);

            void SizeChange()
            {
                if (_widthBoxChanging || _heightBoxChanging)
                    return;

                _resizing = true;

                var selectedRegion = SelectedRegion;

                WidthBox.Value = selectedRegion.Width;
                HeightBox.Value = selectedRegion.Height;

                _resizing = false;
            }

            SizeChanged += (S, E) => SizeChange();

            SizeChange();

            WidthBox.ValueChanged += (S, E) =>
            {
                if (!_resizing && E.NewValue is int width)
                {
                    _widthBoxChanging = true;

                    var selectedRegion = SelectedRegion;

                    selectedRegion.Width = width;

                    SelectedRegion = selectedRegion;

                    _widthBoxChanging = false;
                }
            };

            HeightBox.ValueChanged += (S, E) =>
            {
                if (!_resizing && E.NewValue is int height)
                {
                    _heightBoxChanging = true;

                    var selectedRegion = SelectedRegion;

                    selectedRegion.Height = height;

                    SelectedRegion = selectedRegion;

                    _heightBoxChanging = false;
                }
            };
        }

        void CloseButton_Click(object Sender, RoutedEventArgs E)
        {
            Hide();

            SelectorHidden?.Invoke();
        }

        public event Action SelectorHidden;
        
        protected override void OnLocationChanged(EventArgs E)
        {
            base.OnLocationChanged(E);

            UpdateRegion();
        }

        // Prevent Maximizing
        protected override void OnStateChanged(EventArgs E)
        {
            if (WindowState != WindowState.Normal)
                WindowState = WindowState.Normal;

            base.OnStateChanged(E);
        }

        protected override void OnRenderSizeChanged(SizeChangedInfo SizeInfo)
        {
            UpdateRegion();

            InkCanvas.Strokes.Clear();

            base.OnRenderSizeChanged(SizeInfo);
        }

        void UpdateRegion()
        {
            _region = Dispatcher.Invoke(() =>
                new Rectangle((int)((Left + LeftOffset) * Dpi.X),
                    (int)((Top + TopOffset) * Dpi.Y),
                    (int)((Region.ActualWidth - 2 * LeftOffset) * Dpi.X),
                    (int)((Region.ActualHeight - 2 * TopOffset) * Dpi.Y)));

            UpdateRegionName?.Invoke(_region.ToString().Replace("{", "")
                .Replace("}", "")
                .Replace(",", ", "));
        }

        public event Action<string> UpdateRegionName;

        // Ignoring Borders and Header
        public Rectangle SelectedRegion
        {
            get
            {
                if (_region == null)
                    UpdateRegion();

                return _region.Value;
            }
            set
            {
                if (value == Rectangle.Empty)
                    return;
                
                Dispatcher.Invoke(() =>
                {
                    Region.Width = value.Width / Dpi.X + 2 * LeftOffset;
                    Region.Height = value.Height / Dpi.Y + 2 * TopOffset;

                    Left = value.Left / Dpi.X - LeftOffset;
                    Top = value.Top / Dpi.Y - TopOffset;
                });
            }
        }

        public void Lock()
        {
            Dispatcher.Invoke(() =>
            {
                ResizeMode = ResizeMode.NoResize;
                Snapper.IsEnabled = CloseButton.IsEnabled = false;

                WidthBox.IsEnabled = HeightBox.IsEnabled = false;
            });
        }
        
        public void Release()
        {
            Dispatcher.Invoke(() =>
            {
                ResizeMode = ResizeMode.CanResize;
                Snapper.IsEnabled = CloseButton.IsEnabled = true;

                WidthBox.IsEnabled = HeightBox.IsEnabled = true;

                Show();
            });
        }

        public IntPtr Handle => new WindowInteropHelper(this).Handle;

        void Snapper_OnClick(object Sender, RoutedEventArgs E)
        {
            var win = _videoSourcePicker.PickWindow(new [] { Handle });

            if (win == null)
                return;

            SelectedRegion = win.Rectangle;

            // Prevent going outside
            if (Left < 0)
            {
                // Decrease Width
                try { Width += Left; }
                catch { }
                finally { Left = 0; }
            }

            if (Top < 0)
            {
                // Decrease Height
                try { Height += Top; }
                catch { }
                finally { Top = 0; }
            }
        }

        void UIElement_OnPreviewMouseLeftButtonDown(object Sender, MouseButtonEventArgs E)
        {
            DragMove();
        }

        void Thumb_OnDragDelta(object Sender, DragDeltaEventArgs E)
        {
            if (Sender is FrameworkElement element)
            {
                void DoTop()
                {
                    var oldTop = Top;
                    var oldBottom = Top + Region.Height;
                    var top = Top + E.VerticalChange;

                    if (top > 0)
                        Top = top;
                    else
                    {
                        Top = 0;
                        Region.Width = oldBottom;
                        return;
                    }

                    var height = Region.Height - E.VerticalChange;

                    if (height > Region.MinHeight)
                        Region.Height = height;
                    else Top = oldTop;
                }

                void DoLeft()
                {
                    var oldLeft = Left;
                    var oldRight = Left + Region.Width;
                    var left = Left + E.HorizontalChange;

                    if (left > 0)
                        Left = left;
                    else
                    {
                        Left = 0;
                        Region.Width = oldRight;
                        return;
                    }

                    var width = Region.Width - E.HorizontalChange;

                    if (width > Region.MinWidth)
                        Region.Width = width;
                    else Left = oldLeft;
                }

                void DoBottom()
                {
                    var height = Region.Height + E.VerticalChange;

                    if (height > 0)
                        Region.Height = height;
                }

                void DoRight()
                {
                    var width = Region.Width + E.HorizontalChange;

                    if (width > 0)
                        Region.Width = width;
                }

                void DoMove()
                {
                    Left += E.HorizontalChange;
                    Top += E.VerticalChange;
                }

                switch (element.Tag)
                {
                    case "Top":
                        DoMove();
                        break;

                    case "Bottom":
                        DoBottom();
                        break;

                    case "Left":
                        DoLeft();
                        break;

                    case "Right":
                        DoRight();
                        break;

                    case "TopLeft":
                        DoTop();
                        DoLeft();
                        break;

                    case "TopRight":
                        DoTop();
                        DoRight();
                        break;

                    case "BottomLeft":
                        DoBottom();
                        DoLeft();
                        break;

                    case "BottomRight":
                        DoBottom();
                        DoRight();
                        break;
                }
            }
        }
    }
}
