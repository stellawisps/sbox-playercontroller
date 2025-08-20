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
	    #region Input Properties

        [Sync]
        [Description("The direction we're looking.")]
        public Angles EyeAngles
        {
            set => _eyeAngles = value;
            get => _eyeAngles;
        }

        [Description("The player's eye position, in first person mode")]
        public Vector3 EyePosition => WorldPosition + Vector3.Up * (BodyHeight - EyeDistanceFromTop);

        [Description("The player's eye position, in first person mode")]
        public global::Transform EyeTransform => new global::Transform(EyePosition, EyeAngles.ToRotation());

        [Sync]
        [Description("True if this player is ducking")]
        public bool IsDucking
        {
            set => _isDucking = value;
            get => _isDucking;
        }

        [Description("The distance from the top of the head to to closest ceiling")]
        public float Headroom { get; set; }

        [Property]
        [FeatureEnabled("Input", Icon = "sports_esports", Description = "Default controls using AnalogMove and AnalogLook. Can optionally interact with any IPressable.")]
        public bool UseInputControls { get; set; } = true;

        [Property]
        [Feature("Input")]
        public float WalkSpeed { get; set; } = 110f;

        [Property]
        [Feature("Input")]
        public float RunSpeed { get; set; } = 320f;

        [Property]
        [Feature("Input")]
        public float DuckedSpeed { get; set; } = 70f;

        [Property]
        [Feature("Input")]
        public float JumpSpeed { get; set; } = 300f;

        [Property]
        [Feature("Input")]
        public float DuckedHeight { get; set; } = 36f;

        [Property]
        [Feature("Input")]
        [Description("Amount of seconds it takes to get from your current speed to your requuested speed, if higher")]
        public float AccelerationTime { get; set; }

        [Property]
        [Feature("Input")]
        [Description("Amount of seconds it takes to get from your current speed to your requuested speed, if lower")]
        public float DeaccelerationTime { get; set; }

        [Property]
        [Feature("Input")]
        [InputAction]
        [Category("Running")]
        [Description("The button that the player will press to use to run")]
        public string AltMoveButton { get; set; } = "run";

        [Property]
        [Feature("Input")]
        [Category("Running")]
        [Description("If true then the player will run by default, and holding AltMoveButton will switch to walk")]
        public bool RunByDefault { get; set; }


        [Property]
        [Feature("Input")]
        [Category("Eye Angles")]
        [Description("When true we'll move the camera around using the mouse")]
        public bool UseLookControls { get; set; } = true;

        [Property]
        [Feature("Input")]
        [Category("Eye Angles")]
        public bool RotateWithGround { get; set; } = true;

        [Property]
        [Feature("Input")]
        [Category("Eye Angles")]
        [Range(0.0f, 180f)]
        public float PitchClamp { get; set; } = 90f;

        [Property]
        [Feature("Input")]
        [Category("Eye Angles")]
        [Range(0.0f, 2f)]
        [Description("Allows modifying the eye angle sensitivity. Note that player preference sensitivity is already automatically applied, this is just extra.")]
        public float LookSensitivity { get; set; } = 1f;

        #endregion
	    #region Camera Properties

	    [Property]
	    [FeatureEnabled("Camera", Icon = "videocam", Description = "Built-in camera controls. Remove this to control the Camera yourself.")]
	    public bool UseCameraControls { get; set; } = true;

	    [Property]
	    [Feature("Camera")]
	    public float EyeDistanceFromTop { get; set; } = 8f;

	    [Property]
	    [Feature("Camera")]
	    public bool ThirdPerson { get; set; } = true;

	    [Property]
	    [Feature("Camera")]
	    public bool HideBodyInFirstPerson { get; set; } = true;

	    [Property]
	    [Feature("Camera")]
	    public bool UseFovFromPreferences { get; set; } = true;

	    [Property]
	    [Feature("Camera")]
	    public Vector3 CameraOffset { get; set; } = new Vector3(256f, 0.0f, 12f);

	    [Property]
	    [Feature("Camera")]
	    [InputAction]
	    public string ToggleCameraModeButton { get; set; } = "view";

	    #endregion
	    
	    private void InputTick()
	    {
		    InputMove();
		    UpdateDucking(Input.Down("duck"));
		    InputJump();
	    }
	    
	    #region Camera Methods

        private void UpdateCameraPosition()
        {
            if (!UseCameraControls) return;
            
            var cam = Scene.Camera;
            if (cam == null) return;
            
            if (!string.IsNullOrWhiteSpace(ToggleCameraModeButton) && Input.Pressed(ToggleCameraModeButton))
            {
                ThirdPerson = !ThirdPerson;
                _cameraDistance = 20f;
            }
            
            var rotation = EyeAngles.ToRotation();
            cam.WorldRotation = rotation;
            
            var eyePosition = WorldPosition + Vector3.Up * (BodyHeight - EyeDistanceFromTop);
            if (IsOnGround && _eyez != 0.0)
                eyePosition.z = _eyez.LerpTo(eyePosition.z, Time.Delta * 50f);
            _eyez = eyePosition.z;
            
            if (!cam.RenderExcludeTags.Contains("viewer"))
                cam.RenderExcludeTags.Add("viewer");
            
            if (ThirdPerson)
            {
                var cameraDirection = rotation.Forward * -CameraOffset.x + 
                                    rotation.Up * CameraOffset.z + 
                                    rotation.Right * CameraOffset.y;
                
                var trace = Scene.Trace
                    .FromTo(eyePosition, eyePosition + cameraDirection)
                    .IgnoreGameObjectHierarchy(GameObject)
                    .Radius(8f)
                    .Run();
                
                _cameraDistance = !trace.StartedSolid 
                    ? (trace.Distance >= _cameraDistance 
                        ? _cameraDistance.LerpTo(trace.Distance, Time.Delta * 2f)
                        : _cameraDistance.LerpTo(trace.Distance, Time.Delta * 200f))
                    : _cameraDistance.LerpTo(cameraDirection.Length, Time.Delta * 100f);
                
                eyePosition += cameraDirection.Normal * _cameraDistance;
            }
            
            cam.WorldPosition = eyePosition;
            
            if (UseFovFromPreferences)
                cam.FieldOfView = Preferences.FieldOfView;
            
            ISceneEvent<PlayerController.IEvents>.PostToGameObject(GameObject, x => x.PostCameraSetup(cam));
        }

        #endregion
        
         #region Input Methods

        private void UpdateEyeAngles()
        {
            var input = Input.AnalogLook * LookSensitivity;
            ISceneEvent<PlayerController.IEvents>.PostToGameObject(GameObject, x => x.OnEyeAngles(ref input));
            
            var angles = (EyeAngles + input) with { roll = 0.0f };
            if (PitchClamp > 0.0)
                angles.pitch = angles.pitch.Clamp(-PitchClamp, PitchClamp);
            
            EyeAngles = angles;
        }

        private void InputMove()
        {
            WishVelocity = Mode.UpdateMove(EyeAngles.ToRotation(), Input.AnalogMove);
        }

        private void InputJump()
        {
            if (TimeSinceGrounded > 0.33f || !Input.Pressed("Jump") || timeSinceJump < 0.5f || JumpSpeed <= 0.0)
                return;
            
            timeSinceJump = (TimeSince)0.0f;
            Jump(Vector3.Up * JumpSpeed);
            OnJumped();
            ISceneEvent<PlayerController.IEvents>.PostToGameObject(GameObject, x => x.OnJumped());
        }

        [Rpc.Broadcast(NetFlags.Unreliable | NetFlags.OwnerOnly)]
        public void OnJumped()
        {
            if (UseAnimatorControls && Renderer.IsValid())
                Renderer.Set("b_jump", true);
        }

        [Description("Called during FixedUpdate when UseInputControls is enmabled. Will duck if requested. If not, and we're ducked, will unduck if there is room")]
        public void UpdateDucking(bool wantsDuck)
        {
            if (wantsDuck == IsDucking) return;
            
            unduckedHeight = MathF.Max(unduckedHeight, BodyHeight);
            float heightDifference = unduckedHeight - DuckedHeight;
            
            if (!wantsDuck && (!IsOnGround || Headroom < heightDifference))
                return;
            
            IsDucking = wantsDuck;
            if (wantsDuck)
            {
                BodyHeight = DuckedHeight;
                if (!IsOnGround)
                {
                    WorldPosition += Vector3.Up * heightDifference;
                    Transform.ClearInterpolation();
                    bodyDuckOffset = Vector3.Up * -heightDifference;
                }
            }
            else
            {
                BodyHeight = unduckedHeight;
            }
        }

        private void UpdateHeadroom()
        {
            Headroom = TraceBody(
                WorldPosition + Vector3.Up * BodyHeight * 0.5f, 
                WorldPosition + Vector3.Up * (100.0f + BodyHeight * 0.5f), 
                0.75f, 0.5f
            ).Distance;
        }

        private void UpdateFalling()
        {
            if (Mode == null || !Mode.AllowFalling)
            {
                _wasFalling = false;
                fallDistance = 0.0f;
                return;
            }
            
            if (!IsOnGround || _wasFalling)
            {
                var positionDelta = WorldPosition - prevPosition;
                if (positionDelta.z < 0.0)
                {
                    _wasFalling = true;
                    fallDistance -= positionDelta.z;
                }
            }
            
            if (!IsOnGround) return;
            
            if (_wasFalling && fallDistance > 1.0)
            {
                ISceneEvent<PlayerController.IEvents>.PostToGameObject(GameObject, x => x.OnLanded(fallDistance, Velocity));
                if (EnableFootstepSounds)
                {
                    float volume = Velocity.Length.Remap(50f, 800f, 0.5f, 5f);
                    PlayFootstepSound(WorldPosition, volume, 0);
                    PlayFootstepSound(WorldPosition, volume, 1);
                }
            }
            
            _wasFalling = false;
            fallDistance = 0.0f;
        }

        #endregion
    }
    
    
}
