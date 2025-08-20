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
	#region Events Interface

        /// <summary>Events from the PlayerController</summary>
        public interface IEvents : ISceneEvent<PlayerController.IEvents>
        {
            /// <summary>
            /// Our eye angles are changing. Allows you to change the sensitivity, or stomp all together.
            /// </summary>
            [Description("Our eye angles are changing. Allows you to change the sensitivity, or stomp all together.")]
            void OnEyeAngles(ref Angles angles) { }

            /// <summary>Called after we've set the camera up</summary>
            [Description("Called after we've set the camera up")]
            void PostCameraSetup(CameraComponent cam) { }

            /// <summary>The player has just jumped</summary>
            [Description("The player has just jumped")]
            void OnJumped() { }

            /// <summary>
            /// The player has landed on the ground, after falling this distance.
            /// </summary>
            [Description("The player has landed on the ground, after falling this distance.")]
            void OnLanded(float distance, Vector3 impactVelocity) { }

            /// <summary>
            /// Used by the Using system to find components we can interact with.
            /// By default we can only interact with IPressable components.
            /// Return a component if we can use it, or else return null.
            /// </summary>
            [Description("Used by the Using system to find components we can interact with. By default we can only interact with IPressable components. Return a component if we can use it, or else return null.")]
            Component GetUsableComponent(GameObject go) => null;

            /// <summary>We have started using something (use was pressed)</summary>
            [Description("We have started using something (use was pressed)")]
            void StartPressing(Component target) { }

            /// <summary>We have stopped using something (use was released)</summary>
            [Description("We have stopped using something (use was released)")]
            void StopPressing(Component target) { }

            /// <summary>We pressed USE but it did nothing</summary>
            [Description("We pressed USE but it did nothing")]
            void FailPressing() { }
        }

        #endregion
        
        #region Physics Events

        void IScenePhysicsEvents.PrePhysicsStep()
        {
	        UpdateBody();
	        if (IsProxy) return;
            
	        Mode.AddVelocity();
	        Mode.PrePhysicsStep();
        }

        void IScenePhysicsEvents.PostPhysicsStep()
        {
	        Velocity = Body.Velocity - GroundVelocity;
	        UpdateGroundVelocity();
	        RestoreStep();
	        Mode?.PostPhysicsStep();
	        CategorizeGround();
	        ChooseBestMoveMode();
        }

        #endregion
    }
    
    
}
