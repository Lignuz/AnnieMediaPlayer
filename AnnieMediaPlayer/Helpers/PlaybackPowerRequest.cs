using System.Runtime.InteropServices;

namespace AnnieMediaPlayer
{
    // 재생 중에는 시스템 절전을 막고, 영상이 화면에 보일 때는 화면 꺼짐도 막습니다.
    // SetThreadExecutionState 는 호출한 스레드 기준으로 유지되므로 UI 스레드에서만 호출합니다.
    internal static class PlaybackPowerRequest
    {
        [Flags]
        private enum ExecutionState : uint
        {
            SystemRequired = 0x00000001,
            DisplayRequired = 0x00000002,
            Continuous = 0x80000000,
        }

        private static ExecutionState? s_currentState;
        private static bool s_failureLogged;

        public static void Update(bool isPlaying, bool keepDisplayOn)
        {
            var state = ExecutionState.Continuous;
            if (isPlaying)
                state |= keepDisplayOn ? ExecutionState.SystemRequired | ExecutionState.DisplayRequired : ExecutionState.SystemRequired;

            if (s_currentState == state)
                return;

            // 실패하면 이전 상태가 유지되므로 기록하지 않고 다음 변경 때 다시 시도합니다.
            if (SetThreadExecutionState(state) == 0)
            {
                s_currentState = null;
                if (!s_failureLogged)
                {
                    s_failureLogged = true;
                    PlayerDiagnostics.Write($"SetThreadExecutionState({state}) failed.");
                }

                return;
            }

            s_currentState = state;
        }

        public static void Clear() => Update(false, false);

        [DllImport("kernel32.dll")]
        private static extern ExecutionState SetThreadExecutionState(ExecutionState flags);
    }
}
