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
	    #region Utility Methods

	    [Description("Create a ragdoll gameobject version of our render body.")]
	    public GameObject CreateRagdoll(string name = "Ragdoll")
	    {
		    var ragdoll = new GameObject(true, name);
		    ragdoll.Tags.Add("ragdoll");
		    ragdoll.WorldTransform = WorldTransform;
            
		    var sourceRenderer = Renderer.Components.Get<SkinnedModelRenderer>();
		    if (!sourceRenderer.IsValid()) return ragdoll;
            
		    var ragdollRenderer = ragdoll.Components.Create<SkinnedModelRenderer>();
		    ragdollRenderer.CopyFrom(sourceRenderer);
		    ragdollRenderer.UseAnimGraph = false;
            
		    foreach (var childRenderer in sourceRenderer.GameObject.Children
			             .SelectMany(x => x.Components.GetAll<SkinnedModelRenderer>())
			             .Where(x => x.IsValid()))
		    {
			    var childRagdollRenderer = new GameObject(true, childRenderer.GameObject.Name)
			    {
				    Parent = ragdoll
			    }.Components.Create<SkinnedModelRenderer>();
                
			    childRagdollRenderer.CopyFrom(childRenderer);
			    childRagdollRenderer.BoneMergeTarget = ragdollRenderer;
		    }
            
		    var modelPhysics = ragdoll.Components.Create<ModelPhysics>();
		    modelPhysics.Model = ragdollRenderer.Model;
		    modelPhysics.Renderer = ragdollRenderer;
		    modelPhysics.CopyBonesFrom(sourceRenderer, true);
            
		    return ragdoll;
	    }

	    #endregion
    }
    
    
}
