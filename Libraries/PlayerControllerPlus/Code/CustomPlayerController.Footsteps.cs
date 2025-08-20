using Sandbox.Audio;
using Sandbox.Internal;
using Sandbox.Movement;
using System;
using System.Collections.Generic;
using System.Linq;
using Sandbox;

namespace CustomMovement
{
    public partial class PlayerController
    {
	    #region Footstep Methods

	    private void OnFootstepEvent(SceneModel.FootstepEvent e)
	    {
		    if (!IsOnGround || !EnableFootstepSounds || _timeSinceStep < 0.2f) return;
            
		    _timeSinceStep = (TimeSince)0.0f;
		    PlayFootstepSound(e.Transform.Position, e.Volume, e.FootId);
	    }

	    public void PlayFootstepSound(Vector3 worldPosition, float volume, int foot)
	    {
		    volume *= WishVelocity.Length.Remap(0.0f, 400f);
		    if (volume <= 0.1f || GroundSurface == null) return;
            
		    var sound = foot == 0 ? GroundSurface.SoundCollection.FootLeft : GroundSurface.SoundCollection.FootRight;
            
		    if (sound == null)
		    {
			    if (DebugFootsteps)
				    DebugOverlay.Sphere(new Sphere(worldPosition, volume), global::Color.Orange, 10f, overlay: true);
			    return;
		    }
            
		    var soundHandle = GameObject.PlaySound(sound, Vector3.Zero);
		    soundHandle.FollowParent = false;
		    soundHandle.TargetMixer = FootstepMixer.GetOrDefault();
		    soundHandle.Volume *= volume * FootstepVolume;
            
		    if (DebugFootsteps)
		    {
			    DebugOverlay.Sphere(new Sphere(worldPosition, volume), duration: 10f, overlay: true);
			    DebugOverlay.Text(worldPosition, sound.ResourceName ?? "", 14f, TextFlag.LeftTop, duration: 10f, overlay: true);
		    }
	    }

	    #endregion
    }
    
    
}
