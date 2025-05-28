using Unosquare.FFME;

namespace AnnieMediaPlayer
{
    public class MainViewModel : ViewModelBase
    {
        public MainViewModel()
        {   
        }

        
        // 기본 미디어 엘리먼트
        private MediaElement? m_MediaElement;
        public MediaElement? MediaElement
        {
            get
            {
                if (m_MediaElement == null)
                    m_MediaElement = (App.Current.MainWindow as MainWindow)?.ffmeMediaElement;

                return m_MediaElement;
            }
        }

        // 프로퍼티 패널 열림 여부
        public bool IsPropertiesPanelOpen { get => Get(); set => Set(value); }
    }
}
