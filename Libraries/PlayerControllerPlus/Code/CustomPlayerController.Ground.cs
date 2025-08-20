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
	    #region Ground Properties

	    [Description("The object we're standing on. Null if we're standing on nothing.")]
	    public GameObject GroundObject { get; set; }

	    [Description("The collider component we're standing on. Null if we're standing nothing")]
	    public Component GroundComponent { get; set; }

	    [Description("If we're stnding on a surface this is it")]
	    public Surface GroundSurface { get; set; }

	    [Description("The friction property of the ground we're standing on.")]
	    public float GroundFriction { get; set; }

	    [Description("Are we standing on a surface that is physically dynamic")]
	    public bool GroundIsDynamic { get; set; }

	    [Description("Amount of time since this character was last on the ground")]
	    public TimeSince TimeSinceGrounded { get; private set; } = (TimeSince)0.0f;

	    [Description("Amount of time since this character was last not on the ground")]
	    public TimeSince TimeSinceUngrounded { get; private set; } = (TimeSince)0.0f;

	    #endregion

	    
	  #region Ground Methods

        private void UpdateGroundVelocity()
        {
            if (GroundObject == null)
            {
                GroundVelocity = Vector3.Zero;
                return;
            }
            
            if (GroundComponent is Collider groundCollider)
                GroundVelocity = groundCollider.GetVelocityAtPoint(WorldPosition);
            
            if (GroundComponent is Rigidbody groundRigidbody)
            {
                float ratio = groundRigidbody.Mass / (BodyMass + groundRigidbody.Mass);
                GroundVelocity = groundRigidbody.GetVelocityAtPoint(WorldPosition) * ratio;
            }
        }

        [Description("Adds velocity in a special way. First we subtract any opposite velocity (ie, falling) then we add the velocity, but we clamp it to that direction. This means that if you jump when you're running up a platform, you don't get extra jump power.")]
        public void Jump(Vector3 velocity)
        {
            PreventGrounding(0.2f);
            var currentVelocity = Body.Velocity;
            if (currentVelocity.Dot(velocity) < 0.0)
                currentVelocity = currentVelocity.SubtractDirection(velocity.Normal);
            Body.Velocity = currentVelocity.AddClamped(velocity, velocity.Length);
        }

        [Description("Prevent being grounded for a number of seconds")]
        public void PreventGrounding(float seconds)
        {
            _timeUntilAllowedGround = (TimeUntil)MathF.Max(_timeUntilAllowedGround, seconds);
            UpdateGroundFromTraceResult(new SceneTraceResult());
        }

        [Description("Lift player up and place a skin level above the ground")]
        internal void Reground(float stepSize)
        {
            if (!IsOnGround || Body.Sleeping) return;
            
            var worldPosition = WorldPosition;
            float scale = 1f;
            
            SceneTraceResult result;
            do
            {
                result = TraceBody(worldPosition + Vector3.Up * 1f, worldPosition + Vector3.Down * stepSize, scale, 0.5f);
                if (!result.StartedSolid) break;
                
                scale -= 0.1f;
                if (scale < 0.7f) return;
            } while (true);
            
            if (result.StartedSolid || !result.Hit) return;
            
            var newPosition = result.EndPosition + Vector3.Up * 0.01f;
            var positionDelta = worldPosition - newPosition;
            if (positionDelta == Vector3.Zero) return;
            
            WorldPosition = newPosition;
            if (positionDelta.z > 0.01f)
                Body.Velocity = Body.Velocity with { z = 0.0f };
        }

        private void CategorizeGround()
        {
            if (!Mode.AllowGrounding)
            {
                PreventGrounding(0.1f);
                UpdateGroundFromTraceResult(new SceneTraceResult());
                return;
            }
            
            if (GroundVelocity.z > 250.0)
            {
                PreventGrounding(0.3f);
                UpdateGroundFromTraceResult(new SceneTraceResult());
                return;
            }
            
            if (_timeUntilAllowedGround > 0 || GroundVelocity.z > 300.0)
            {
                UpdateGroundFromTraceResult(new SceneTraceResult());
                return;
            }
            
            var from = WorldPosition + Vector3.Up * 4f;
            var to = WorldPosition + Vector3.Down * 2f;
            float scale = 1f;
            
            SceneTraceResult result;
            do
            {
                result = TraceBody(from, to, scale, 0.5f);
                if (!result.StartedSolid && (!result.Hit || Mode.IsStandableSurface(result))) break;
                
                scale -= 0.1f;
                if (scale < 0.7f)
                {
                    UpdateGroundFromTraceResult(new SceneTraceResult());
                    return;
                }
            } while (true);
            
            UpdateGroundFromTraceResult(result.StartedSolid || !result.Hit || !Mode.IsStandableSurface(result) 
                ? new SceneTraceResult() 
                : result);
        }

        private void UpdateGroundFromTraceResult(SceneTraceResult tr)
        {
            bool wasGrounded = IsOnGround;
            
            GroundObject = tr.Body?.GameObject;
            GroundComponent = tr.Body?.Component;
            GroundSurface = tr.Surface;
            GroundIsDynamic = true;
            
            if (GroundObject != null)
            {
                TimeSinceGrounded = (TimeSince)0.0f;
                _groundTransform = GroundObject.WorldTransform;
                GroundFriction = tr.Surface.Friction;
                
                if (tr.Component is Collider component)
                {
                    if (component.Friction.HasValue)
                        GroundFriction = component.Friction.Value;
                    GroundIsDynamic = component.IsDynamic;
                }
            }
            else
            {
                TimeSinceUngrounded = (TimeSince)0.0f;
                _groundTransform = new global::Transform();
            }
            
            if (wasGrounded != IsOnGround)
                UpdateBody();
        }

        private void UpdateGroundEyeRotation()
        {
            if (GroundObject == null || !RotateWithGround)
            {
                groundHash = 0;
                return;
            }
            
            int currentHash = HashCode.Combine(GroundObject);
            var local = GroundObject.WorldTransform.ToLocal(WorldTransform);
            float yaw = (local.Rotation.Inverse * localGroundTransform.Rotation).Angles().yaw;
            
            if (currentHash == groundHash && yaw != 0.0)
            {
                EyeAngles = EyeAngles.WithYaw(EyeAngles.yaw + yaw);
                if (UseAnimatorControls && Renderer.IsValid())
                    Renderer.WorldRotation *= new Angles(0.0f, yaw, 0.0f).ToRotation();
            }
            
            groundHash = currentHash;
            localGroundTransform = local;
        }

        #endregion
    }
    
    
}
