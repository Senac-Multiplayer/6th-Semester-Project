using UnityEngine;

namespace PiGame.Sound
{
    public class UiAudioManager : MonoBehaviour
    {
        private static float _nextVolumeSoundAt;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetFeedback() => _nextVolumeSoundAt = 0f;

        public static void PlayButton() => NetworkSoundManager.Instance?.LocalUISFX(SFXList.UiButton);
        public static void PlayMapSelection() => NetworkSoundManager.Instance?.LocalUISFX(SFXList.LevelSelect);
        public static void PlayCharacterBrowse() => NetworkSoundManager.Instance?.LocalUISFX(SFXList.CharacterBrowse);
        public static void PlayReadyBanner() => NetworkSoundManager.Instance?.LocalUISFX(SFXList.ReadyBanner);
        public static void PlayMatchStart() => NetworkSoundManager.Instance?.LocalUISFX(SFXList.MatchStart);

        public static void PlayVolumeFeedback()
        {
            if (!NetworkSoundManager.Instance || Time.unscaledTime < _nextVolumeSoundAt) return;
            _nextVolumeSoundAt = Time.unscaledTime + 0.08f;
            NetworkSoundManager.Instance.LocalUISFX(SFXList.VolumeScroll);
        }

        public void PlayDrawRoullete() => NetworkSoundManager.Instance?.LocalSFX(SFXList.DrawRoulette);
        public void PlayVolumeScroll() => PlayVolumeFeedback();
        public void PlayLevelSelect() => PlayMapSelection();
        public void PlayLocalButtonSound() => PlayButton();
        public void PlayToggleSound(bool _) => PlayButton();
    }
}
