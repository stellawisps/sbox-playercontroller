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
    }
    
    
}
