using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace AnnieMediaPlayer
{
    // OnPropertyChanged() 를 재사용하기 위한 Base Class 입니다.
    // 심플하게 아래와 같이 Backing Field 없이 코드 작성이 가능합니다. (OnPropertyChanged 알림 자동 호출)
    // public int Age
    // {
    //     get => Get();
    //     set => Set(value);
    // }
    public class ViewModelBase : INotifyPropertyChanged
    {
        private readonly Dictionary<string, object?> _propertyValues = new();

        /// <summary>  
        /// Backing Field 없이 프로퍼티 값 가져오기 (값이 없거나 null이면 기본값 반환)  
        /// </summary>  
        protected dynamic Get([CallerMemberName] string? propertyName = null)
        {
            if (propertyName == null)
            {
                throw new ArgumentNullException(nameof(propertyName));
            }

            if (_propertyValues.TryGetValue(propertyName, out var value) && value != null)
            {
                return value; // 값이 존재하면 그대로 반환  
            }

            return GetDefaultValue(propertyName) ?? throw new InvalidOperationException($"Property '{propertyName}' has no default value."); // 값이 없거나 null이면 기본값 반환  
        }

        /// <summary>  
        /// 기본값 반환 (bool → false, int → 0, string → "")  
        /// </summary>  
        private object? GetDefaultValue(string propertyName)
        {
            Type? propertyType = GetType().GetProperty(propertyName)?.PropertyType;
            if (propertyType != null && propertyType.IsValueType)
            {
                return Activator.CreateInstance(propertyType); // 기본값 반환  
            }
            return null; // 참조형(Reference Type)은 null 반환  
        }

        /// <summary>  
        /// Backing Field 없이 프로퍼티 값 설정  
        /// </summary>  
        protected bool Set(object? value, [CallerMemberName] string propertyName = "")
        {
            if (propertyName == null)
            {
                throw new ArgumentNullException(nameof(propertyName));
            }

            if (_propertyValues.TryGetValue(propertyName, out var currentValue))
            {
                // 기존 값과 새 값이 모두 null이면 변경하지 않음  
                if (currentValue == null && value == null)
                {
                    return false;
                }

                // 기존 값과 새 값이 다르면 변경  
                if (EqualityComparer<object>.Default.Equals(currentValue!, value))
                {
                    return false;
                }
            }

            _propertyValues[propertyName] = value; // 새로운 값 저장  
            OnPropertyChanged(propertyName);
            return true;
        }

        // 이벤트  
        public event PropertyChangedEventHandler? PropertyChanged;
        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    };
}
