using AnnieMediaPlayer.Options;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Data;

namespace AnnieMediaPlayer
{
    public class FileNameConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is string path && !string.IsNullOrEmpty(path))
            {
                return Path.GetFileName(path);
            }
            return string.Empty;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    [ValueConversion(typeof(double), typeof(string))]
    internal class RangeConstrainedDoubleToDoubleConverter : DependencyObject, IValueConverter
    {
        // MathHelper.cs
        internal static class MathHelper
        {
            public static double Clamp(double value, double min, double max)
            {
                if (value < min)
                    return min;
                if (value > max)
                    return max;
                return value;
            }

            public static double Mod(double value, double m)
            {
                return (value % m + m) % m;
            }
        }

        public static DependencyProperty MinProperty =
            DependencyProperty.Register(nameof(Min), typeof(double), typeof(RangeConstrainedDoubleToDoubleConverter),
                new PropertyMetadata(0.0));

        public static DependencyProperty MaxProperty =
            DependencyProperty.Register(nameof(Max), typeof(double), typeof(RangeConstrainedDoubleToDoubleConverter),
                new PropertyMetadata(1.0));

        public double Min
        {
            get => (double)GetValue(MinProperty);
            set => SetValue(MinProperty, value);
        }

        public double Max
        {
            get => (double)GetValue(MaxProperty);
            set => SetValue(MaxProperty, value);
        }

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (!double.TryParse(((string)value).Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double result))
                return DependencyProperty.UnsetValue;
            return MathHelper.Clamp(result, Min, Max);
        }
    }

    // 슬라이더에서 진행된 부분까지의 너비를 계산합니다.
    class DraggableSliderValueConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            try
            {
                double val = (double)values[0];
                double width = (double)values[1];
                double min_val = (double)values[2];
                double max_val = (double)values[3];

                return (val - min_val) * width / (max_val - min_val);
            }
            catch
            {
                return 0;
            }
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, System.Globalization.CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class UseOpenPlayStringConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is true)
                return "Play";
            return "Pause";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class RotateAngleValueConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            double angle = 0.0;
            if (value is RotateAngle)
            {
                switch ((RotateAngle)value)
                {
                    case RotateAngle.Rotate_0:
                        angle = 0.0;
                        break;
                    case RotateAngle.Rotate_90:
                        angle = 90.0;
                        break;
                    case RotateAngle.Rotate_180:
                        angle = 180.0;
                        break;
                    case RotateAngle.Rotate_270:
                        angle = 270.0;
                        break;
                }
            }
            return angle;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    // flip 이면 -1 로 반환합니다.
    public class FlipScaleValueConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            double scale = 1.0;
            if (value is true)
                scale = -1.0;
            return scale;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
