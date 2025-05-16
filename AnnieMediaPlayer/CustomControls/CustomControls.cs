using System.Windows.Controls;
using System.Windows.Input;
using System.Windows;

namespace AnnieMediaPlayer
{
    class DraggableSlider : Slider
    {
        // 이전 IsMoveToPointEnabled 값으로 되돌리기 위한 플래그
        private bool defaultIsMoveToPointEnabled;

        public static readonly DependencyProperty AutoMoveProperty =
            DependencyProperty.Register("AutoMove", typeof(bool), typeof(DraggableSlider), new FrameworkPropertyMetadata(false, ChangeAutoMoveProperty));

        public bool AutoMove
        {
            get { return (bool)GetValue(AutoMoveProperty); }
            set { SetValue(AutoMoveProperty, value); }
        }

        public bool WheelScroll = false;    // 휠 스크롤을 지원할지 여부 설정

        public bool isDragging = false;     // 드래그 중인지 여부
        public event EventHandler? DragStateChanged;

        public DraggableSlider()
        {
            Minimum = 0;
            Maximum = 100;
            SmallChange = 1;
            LargeChange = 10;

            PreviewMouseWheel += OnPreviewMouseWheel;
        }

        private static void ChangeAutoMoveProperty(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            DraggableSlider? slider = d as DraggableSlider;
            if (slider != null)
            {
                if ((bool)e.NewValue)
                {
                    slider.defaultIsMoveToPointEnabled = slider.IsMoveToPointEnabled;
                    slider.IsMoveToPointEnabled = true;
                    slider.PreviewMouseMove += CustomSlider_PreviewMouseMove;
                }
                else
                {
                    slider.IsMoveToPointEnabled = slider.defaultIsMoveToPointEnabled;
                    slider.PreviewMouseMove -= CustomSlider_PreviewMouseMove;
                }
            }
        }

        private static void CustomSlider_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                DraggableSlider? slider = sender as DraggableSlider;
                if (slider != null && slider.IsMouseCaptured == true)
                {
                    // 현재 Slider 내 마우스 좌표 값을 Value 값으로 계산.
                    Point position = e.GetPosition(slider);
                    double d = (slider.Orientation == Orientation.Horizontal) ?
                        slider.Minimum + (position.X / slider.ActualWidth) * (slider.Maximum - slider.Minimum) :
                        slider.Minimum + (position.Y / slider.ActualHeight) * (slider.Maximum - slider.Minimum);

                    if (slider.IsSnapToTickEnabled == true)
                    {
                        // 값을 눈금 위치에 맞추어 보정
                        d = Math.Round(d / slider.TickFrequency) * slider.TickFrequency;
                    }

                    d = Math.Min(d, slider.Maximum);
                    d = Math.Max(d, slider.Minimum);

                    // 한 번 하면 안되는 경우가 있어서 ... (oldValue 가 1일 때 변경이 바로 안 됨??)
                    slider.Value = d;
                    if (slider.Value != d)
                    {
                        slider.Value = d;
                    }
                }

                e.Handled = true;
            }
        }

        protected override void OnPreviewMouseDown(MouseButtonEventArgs e)
        {
            // 적용하기 전에 변경 되었다는 알림 먼저
            isDragging = true;
            if (DragStateChanged != null)
            {
                DragStateChanged(this, EventArgs.Empty);
            }

            e.Handled = true;
            base.OnPreviewMouseDown(e);

            Focus();
            CaptureMouse();
        }

        protected override void OnPreviewMouseUp(MouseButtonEventArgs e)
        {
            e.Handled = true;
            base.OnPreviewMouseUp(e);

            ReleaseMouseCapture();

            // 적용 후에 완료되었다는 알림
            isDragging = false;
            if (DragStateChanged != null)
            {
                DragStateChanged(this, EventArgs.Empty);
            }
        }

        private void OnPreviewMouseWheel(object sender, MouseWheelEventArgs args)
        {
            if (WheelScroll == true)
            {
                Value = RangeConstrainedDoubleToDoubleConverter.MathHelper.Clamp(Value + SmallChange * args.Delta / 120, Minimum, Maximum);
                args.Handled = true;
            }
        }
    }
}
