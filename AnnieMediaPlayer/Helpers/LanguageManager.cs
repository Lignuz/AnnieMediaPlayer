using AnnieMediaPlayer.Options;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Documents;

namespace AnnieMediaPlayer
{
    public static class LanguageManager
    {
        static private Languages _language = Languages.ko;
        static public Languages language
        {
            get { return _language; }
            private set 
            {
                if (_language != value)
                {
                    _language = value; 
                }
            }
        }

        public static event EventHandler? LanguageChanged;

        // 언어 변경 (ko, en)
        public static void ChangeLanguage(Languages newLanguage)
        {
            if (language == newLanguage)
                return;

            language = newLanguage;
            string culture = language.ToString();

            var dictPath = $"Languages/StringResources.{culture}.xaml";
            var newDict = new ResourceDictionary() { Source = new Uri(dictPath, UriKind.Relative) };

            // 기존 언어 리소스 제거
            var oldDict = Application.Current.Resources.MergedDictionaries
                .FirstOrDefault(d => d.Source != null && d.Source.OriginalString.Contains("StringResources."));
            if (oldDict != null)
                Application.Current.Resources.MergedDictionaries.Remove(oldDict);

            Application.Current.Resources.MergedDictionaries.Add(newDict);

            // 리소스 변경 후 이벤트 발생
            LanguageChanged?.Invoke(null, EventArgs.Empty);
        }

        // 리소스의 문자열을 찾아서 값을 반환합니다. 
        // 다국어 자동 연동을 위해서 값 입력 대신 리소스 참조로 프로퍼티를 연결하는 방법을 사용하는 것이 더 좋을 수 있습니다. 
        // e.g. => con.SetResourceReference(ToolTipProperty, "Text.Open");
        public static string GetResourceString(string str)
        {
            string? resourceValue = Application.Current.TryFindResource(str) as string;
            if (resourceValue != null)
            {
                // ResourceDictionary 에 \n 를 입력하면 \\n 문자로 변환되어 가져오므로, 
                // 이를 줄바꿈으로 처리하기 위해 다시 맞춰주도록 합니다.
                resourceValue = resourceValue.Replace("\\n", "\n");
            }
            else
            {
                resourceValue = $"{str}"; // fallback
            }
            return resourceValue;
        }

        // 텍스트 조합을 위한 다국어 연결 run 생성
        public static Run GetLocalizedRun(string key)
        {
            var run = new Run();
            run.SetResourceReference(Run.TextProperty, key);
            return run;
        }
    }
}