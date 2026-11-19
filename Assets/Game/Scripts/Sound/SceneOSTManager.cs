using PiGame.Gameplay;
using PiGame.UI;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PiGame.Sound
{
    public class SceneOSTManager : MonoBehaviour
    {
        [SerializeField] private OSTList _sceneOst;

        private void OnEnable() => SceneManager.activeSceneChanged += HandleActiveSceneChanged;
        private void OnDisable() => SceneManager.activeSceneChanged -= HandleActiveSceneChanged;

        private void Start() => PlayIfActive();

        private void HandleActiveSceneChanged(Scene previous, Scene current) => PlayIfActive();

        private void PlayIfActive()
        {
            // No carregamento aditivo, somente a cena exibida escolhe a trilha local.
            if (gameObject.scene != SceneManager.GetActiveScene()
                || (LoadingScreenUI.Instance && LoadingScreenUI.Instance.IsVisible)) return;
            var match = FindFirstObjectByType<MatchController>();
            bool result = match && match.gameObject.scene == gameObject.scene
                && match.Phase == MatchPhase.ShowingResult;
            NetworkSoundManager.Instance?.LocalOST(result ? OSTList.Battle_Vitoria : _sceneOst);
        }

        public static void PlayActiveSceneMusic()
        {
            foreach (var manager in FindObjectsByType<SceneOSTManager>(FindObjectsSortMode.None))
                manager.PlayIfActive();
        }

        public void PlayVictoryOST()
        {
            NetworkSoundManager.Instance?.LocalOST(OSTList.Battle_Vitoria);
        }
    }
}
