namespace PiGame.Sound
{
    public class RebeldeSoundManager : GeneralPlayerSoundManager
    {
        public void PlayRebeldeBounce()
        {
            NetworkSoundManager.Instance?.PlaySFX(SFXList.RebeldeBounce);
        }

        public void PlayRebeldeCatch()
        {
            NetworkSoundManager.Instance?.PlaySFX(SFXList.RebeldeCatch);
        }

        public void PlayRebeldeEmpty()
        {
            NetworkSoundManager.Instance?.PlaySFX(SFXList.RebeldeEmpty);
        }

        public void PlayRebeldeSkill()
        {
            NetworkSoundManager.Instance?.PlaySFX(SFXList.RebeldeSkill);
        }

        public void PlayRebeldeShot()
        {
            NetworkSoundManager.Instance?.PlaySFX(SFXList.RebeldeShot);
        }
    }
    
}
