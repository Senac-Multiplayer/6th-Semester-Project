using Unity.Netcode.Components;

namespace PiGame.Gameplay
{
    public class PlayerNetworkTransform : NetworkTransform
    {
        protected override void OnNetworkTransformStateUpdated(
            ref NetworkTransformState oldState, ref NetworkTransformState newState)
        {
            if (newState.IsTeleportingNextFrame && TryGetComponent(out NetworkPlayerState player))
                player.NotifyPositionTeleported(newState.GetPosition());
        }
    }
}
