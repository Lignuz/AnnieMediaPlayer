﻿using Unosquare.FFME.ClosedCaptions;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Controls;

#pragma warning disable SA1649 // File name must match first type name
#pragma warning disable CA1812 // Remove classes that are apparently never instantiated
namespace AnnieMediaPlayer
{
    /// <inheritdoc />
    internal class TimeSpanToSecondsConverter : IValueConverter
    {
        /// <inheritdoc />
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            switch (value)
            {
                case TimeSpan span:
                    return span.TotalSeconds;
                case Duration duration:
                    return duration.HasTimeSpan ? duration.TimeSpan.TotalSeconds : 0d;
                default:
                    return 0d;
            }
        }

        /// <inheritdoc />
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is double == false) return 0d;
            var result = TimeSpan.FromTicks(System.Convert.ToInt64(TimeSpan.TicksPerSecond * (double)value));

            // Do the conversion from visibility to bool
            if (targetType == typeof(TimeSpan)) return result;
            
            return targetType == typeof(Duration) ?
                new Duration(result) :
                Activator.CreateInstance(targetType) ?? throw new InvalidOperationException("Unable to create an instance of the target type.");
        }
    }

    /// <inheritdoc />
    internal class TimeSpanFormatter : IValueConverter
    {
        /// <inheritdoc />
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            TimeSpan? p;

            switch (value)
            {
                case TimeSpan position:
                    p = position;
                    break;
                case Duration duration when duration.HasTimeSpan:
                    p = duration.TimeSpan;
                    break;
                default:
                    return string.Empty;
            }

            if (p.Value == TimeSpan.MinValue)
                return "N/A";

            if (parameter is string param)
            {
                // 초 단위까지만 표시
                if (param == "sec")
                {
                    return $"{(int)p.Value.TotalHours:00}:{p.Value.Minutes:00}:{p.Value.Seconds:00}";
                }
            }
            return $"{(int)p.Value.TotalHours:00}:{p.Value.Minutes:00}:{p.Value.Seconds:00}.{p.Value.Milliseconds:000}";
        }

        /// <inheritdoc />
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
            throw new NotImplementedException();
    }

    /// <inheritdoc />
    internal class ByteFormatter : IValueConverter
    {
        /// <inheritdoc />
        public object Convert(object value, Type targetType, object format, CultureInfo culture)
        {
            const double minKiloByte = 1024;
            const double minMegaByte = 1024 * 1024;
            const double minGigaByte = 1024 * 1024 * 1024;

            var byteCount = System.Convert.ToDouble(value, CultureInfo.InvariantCulture);

            var suffix = "b";
            var output = 0d;

            if (byteCount >= minKiloByte)
            {
                suffix = "kB";
                output = Math.Round(byteCount / minKiloByte, 2);
            }

            if (byteCount >= minMegaByte)
            {
                suffix = "MB";
                output = Math.Round(byteCount / minMegaByte, 2);
            }

            if (byteCount >= minGigaByte)
            {
                suffix = "GB";
                output = Math.Round(byteCount / minGigaByte, 2);
            }

            return suffix == "b" ?
                $"{output:0} {suffix}" :
                $"{output:0.00} {suffix}";
        }

        /// <inheritdoc />
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
            throw new NotImplementedException();
    }

    /// <inheritdoc />
    internal class BitFormatter : IValueConverter
    {
        /// <inheritdoc />
        public object Convert(object value, Type targetType, object format, CultureInfo culture)
        {
            const double minKiloBit = 1000;
            const double minMegaBit = 1000 * 1000;
            const double minGigaBit = 1000 * 1000 * 1000;

            var byteCount = System.Convert.ToDouble(value, CultureInfo.InvariantCulture);

            var suffix = "bits/s";
            var output = 0d;

            if (byteCount >= minKiloBit)
            {
                suffix = "kbits/s";
                output = Math.Round(byteCount / minKiloBit, 2);
            }

            if (byteCount >= minMegaBit)
            {
                suffix = "Mbits/s";
                output = Math.Round(byteCount / minMegaBit, 2);
            }

            if (byteCount >= minGigaBit)
            {
                suffix = "Gbits/s";
                output = Math.Round(byteCount / minGigaBit, 2);
            }

            return suffix == "b" ?
                $"{output:0} {suffix}" :
                $"{output:0.00} {suffix}";
        }

        /// <inheritdoc />
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
            throw new NotImplementedException();
    }

    /// <inheritdoc />
    internal class PercentageFormatter : IValueConverter
    {
        /// <inheritdoc />
        public object Convert(object value, Type targetType, object format, CultureInfo culture)
        {
            var percentage = 0d;
            if (value is double d) percentage = d;

            percentage = Math.Round(percentage * 100d, 0);

            if (format == null || Math.Abs(percentage) <= double.Epsilon)
                return $"{percentage,3:0} %".Trim();

            return $"{(percentage > 0d ? "R " : "L ")} {Math.Abs(percentage),3:0} %".Trim();
        }

        /// <inheritdoc />
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
            throw new NotImplementedException();
    }

    /// <inheritdoc />
    internal class PlaylistDurationFormatter : IValueConverter
    {
        /// <inheritdoc />
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var duration = value is TimeSpan span ? span : TimeSpan.FromSeconds(-1);

            if (duration.TotalSeconds <= 0)
                return "∞";

            return duration.TotalMinutes >= 100 ?
                $"{System.Convert.ToInt64(duration.TotalHours)}h {System.Convert.ToInt64(duration.Minutes)}m" :
                $"{System.Convert.ToInt64(duration.Minutes):00}:{System.Convert.ToInt64(duration.Seconds):00}";
        }

        /// <inheritdoc />
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
            throw new NotSupportedException();
    }

    /// <inheritdoc />
    internal class UtcDateToLocalTimeString : IValueConverter
    {
        /// <inheritdoc />
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value == null) return "unknown";
            var utcDate = (DateTime)value;
            return utcDate.ToLocalTime().ToString("f", CultureInfo.InvariantCulture);
        }

        /// <inheritdoc />
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
            throw new NotSupportedException();
    }

    /// <inheritdoc />
    [ValueConversion(typeof(bool), typeof(bool))]
    internal class InverseBooleanConverter : IValueConverter
    {
        /// <inheritdoc />
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (targetType != typeof(bool) && targetType != typeof(bool?))
                throw new InvalidOperationException("The target must be a boolean or a nullable boolean");

            if (value is bool?)
            {
                var nullableBool = (bool?)value;
                return nullableBool.HasValue ? !nullableBool.Value : true;
            }

            if (value.GetType() == typeof(bool))
            {
                return !((bool)value);
            }

            return true;
        }

        /// <inheritdoc />
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value == null) return true;

            if (value is bool?)
            {
                var nullableBool = (bool?)value;
                return nullableBool.HasValue ? !nullableBool.Value : true;
            }

            if (value.GetType() == typeof(bool))
            {
                return !((bool)value);
            }

            return true;
        }
    }

    /// <inheritdoc />
    [ValueConversion(typeof(bool), typeof(bool))]
    internal class ClosedCaptionsChannelConverter : IValueConverter
    {
        /// <inheritdoc />
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
            value != null && (CaptionsChannel)value != CaptionsChannel.CCP;

        /// <inheritdoc />
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
            value != null && (bool)value ? CaptionsChannel.CC1 : CaptionsChannel.CCP;
    }

    // BooleanToVisibilityConverter 의 반대 동작을 합니다.
    public class BooleanToVisibilityInvertedConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is bool bValue)
                return bValue ? Visibility.Collapsed : Visibility.Visible;
            else
                return Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is Visibility visibility)
                return visibility != Visibility.Visible;
            else
                return false;
        }
    }

    public class GreaterThanToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value == null || parameter == null)
            {
                // 입력 또는 파라미터가 null이면 Hidden 반환 (또는 원하는 기본 동작)
                return Visibility.Hidden;
            }

            try
            {
                // 입력 값 (바인딩된 값)을 double로 변환
                double numericValue = System.Convert.ToDouble(value, culture);

                // 파라미터 값을 double로 변환
                double numericParameter = System.Convert.ToDouble(parameter, culture);

                // 입력 값이 파라미터 값보다 큰 경우 Visible, 아니면 Hidden
                if (numericValue > numericParameter)
                {
                    return Visibility.Visible;
                }
                else
                {
                    return Visibility.Hidden;
                }
            }
            catch (FormatException)
            {
                // 숫자로 변환할 수 없는 경우 예외 처리
                // (예: 문자열이 들어왔을 때)
                return Visibility.Hidden;
            }
            catch (InvalidCastException)
            {
                // 잘못된 타입이 들어왔을 때 예외 처리
                return Visibility.Hidden;
            }
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            // 이 컨버터는 단방향이므로 ConvertBack은 구현하지 않습니다.
            throw new NotImplementedException("GreaterThanToVisibilityConverter는 단방향 컨버터입니다.");
        }
    }

    public class EnumToBoolConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value == null || parameter == null) return false;
            return value.ToString() == parameter.ToString();
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if ((bool)value)
                return Enum.Parse(targetType, parameter.ToString()!);
            return Binding.DoNothing;
        }
    }

    public class EnumToVisiblityConverter : IValueConverter
    {
        private readonly EnumToBoolConverter _enumToBool = new EnumToBoolConverter();
        private readonly BooleanToVisibilityConverter _boolToVisibility = new BooleanToVisibilityConverter();

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var isMatch = _enumToBool.Convert(value, typeof(bool), parameter, culture);
            return _boolToVisibility.Convert(isMatch, typeof(Visibility), null, culture);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }

    public class EnumToVisiblityInvertedConverter : IValueConverter
    {
        private readonly EnumToBoolConverter _enumToBool = new EnumToBoolConverter();
        private readonly BooleanToVisibilityInvertedConverter _boolToVisibilityInverted = new BooleanToVisibilityInvertedConverter();

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var isMatch = _enumToBool.Convert(value, typeof(bool), parameter, culture);
            return _boolToVisibilityInverted.Convert(isMatch, typeof(Visibility), null, culture);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }
}
#pragma warning restore CA1812 // Remove classes that are apparently never instantiated
#pragma warning restore SA1649 // File name must match first type name