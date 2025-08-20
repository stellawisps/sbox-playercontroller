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
	    #region Animation Properties

        [Property]
        [FeatureEnabled("Animator", Icon = "sports_martial_arts", Description = "Automatically derive animations from Velocity/Inputs")]
        public bool UseAnimatorControls { get; set; } = true;

        [Property]
        [Feature("Animator")]
        [Description("The body will usually be a child object with SkinnedModelRenderer")]
        public SkinnedModelRenderer Renderer
        {
            get => _renderer;
            set
            {
                if (_renderer == value) return;
                DisableAnimationEvents();
                _renderer = value;
                EnableAnimationEvents();
            }
        }

        [Description("If true we'll show the \"create body\" button")]
        public bool ShowCreateBodyRenderer => UseAnimatorControls && Renderer == null;

        [Button("", "add")]
        [Property]
        [Feature("Animator")]
        [Tint(EditorTint.Green)]
        [ShowIf("ShowCreateBodyRenderer", true)]
        public void CreateBodyRenderer()
        {
            Renderer = new GameObject(true, "Body") { Parent = GameObject }.AddComponent<SkinnedModelRenderer>();
            Renderer.Model = Model.Load("models/citizen/citizen.vmdl");
        }

        [Property]
        [Feature("Animator")]
        public float RotationAngleLimit { get; set; } = 45f;

        [Property]
        [Feature("Animator")]
        public float RotationSpeed { get; set; } = 1f;

        [Property]
        [Feature("Animator")]
        [Group("Footsteps")]
        public bool EnableFootstepSounds { get; set; } = true;

        [Property]
        [Feature("Animator")]
        [Group("Footsteps")]
        public float FootstepVolume { get; set; } = 1f;

        [Property]
        [Feature("Animator")]
        [Group("Footsteps")]
        public MixerHandle FootstepMixer { get; set; }

        [Property]
        [Feature("Animator")]
        [Group("Aim")]
        [Order(1001)]
        [Range(0.0f, 1f)]
        [Description("How strongly to look in the eye direction with our eyes")]
        public float AimStrengthEyes { get; set; } = 1f;

        [Property]
        [Feature("Animator")]
        [Group("Aim")]
        [Order(1002)]
        [Range(0.0f, 1f)]
        [Description("How strongly to turn in the eye direction with our head")]
        public float AimStrengthHead { get; set; } = 1f;

        [Property]
        [Feature("Animator")]
        [Group("Aim")]
        [Order(1003)]
        [Range(0.0f, 1f)]
        [Description("How strongly to turn in the eye direction with our body")]
        public float AimStrengthBody { get; set; } = 1f;

        /// <summary>Draw debug overlay on footsteps</summary>
        public bool DebugFootsteps;

        #endregion
	    
	    #region Animation Methods

	    private void EnableAnimationEvents()
	    {
		    if (Renderer == null) return;
		    Renderer.OnFootstepEvent += OnFootstepEvent;
	    }

	    private void DisableAnimationEvents()
	    {
		    if (Renderer == null) return;
		    Renderer.OnFootstepEvent -= OnFootstepEvent;
	    }

	    [Description("Update the animation for this renderer. This will update the body rotation etc too.")]
	    public void UpdateAnimation(SkinnedModelRenderer renderer)
	    {
		    if (!renderer.IsValid()) return;
            
		    renderer.LocalPosition = bodyDuckOffset;
		    bodyDuckOffset = bodyDuckOffset.LerpTo(Vector3.Zero, Time.Delta * 5f);
		    Mode?.UpdateAnimator(renderer);
	    }

	    private void UpdateBodyVisibility()
	    {
		    if (!UseCameraControls || Scene.Camera == null) return;
            
		    bool shouldHide = !ThirdPerson && HideBodyInFirstPerson && !IsProxy;
		    if (!IsProxy && _cameraDistance < 20.0) shouldHide = true;
		    if (IsProxy) shouldHide = false;
            
		    var gameObject = Renderer?.GameObject ?? GameObject;
		    if (gameObject.IsValid())
			    gameObject.Tags.Set("viewer", shouldHide);
	    }

	    #endregion
    }
    
    
}
