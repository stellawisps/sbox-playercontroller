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
	    #region Trace Methods

	    [Description("Return an aabb representing the body")]
	    public BBox BodyBox(float scale = 1f, float heightScale = 1f)
	    {
		    float halfRadius = BodyRadius * 0.5f * scale;
		    return new BBox(
			    new Vector3(-halfRadius, -halfRadius, 0.0f), 
			    new Vector3(halfRadius, halfRadius, BodyHeight * heightScale)
		    );
	    }

	    [Description("Trace the aabb body from one position to another and return the result")]
	    public SceneTraceResult TraceBody(Vector3 from, Vector3 to, float scale = 1f, float heightScale = 1f)
	    {
		    return Scene.Trace
			    .Box(BodyBox(scale, heightScale), from, to)
			    .IgnoreGameObjectHierarchy(GameObject)
			    .WithCollisionRules(Tags)
			    .Run();
	    }

	    #endregion
    }
    
    
}
