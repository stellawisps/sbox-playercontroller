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
	    #region Movement Mode

	    public MoveMode Mode { get; private set; }

	    #endregion
	    
	    private void ChooseBestMoveMode()
	    {
		    var bestMode = GetComponents<MoveMode>().MaxBy(x => x.Score(this));
		    if (Mode == bestMode) return;
            
		    Mode?.OnModeEnd(bestMode);
		    Mode = bestMode;
		    Body.PhysicsBody.Sleeping = false;
		    Mode?.OnModeBegin();
	    }
    }
    
    
}
