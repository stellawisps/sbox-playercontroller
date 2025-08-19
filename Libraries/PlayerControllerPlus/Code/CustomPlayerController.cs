using Sandbox.Audio;
using Sandbox.Internal;
using Sandbox.Movement;
using System;
using System.Collections.Generic;
using System.Linq;
using Sandbox;

namespace CustomMovement
{
    [Icon("directions_walk")]
    [EditorHandle(Icon = "directions_walk")]
    [Title("Player Controller")]
    [Category("Physics")]
    [Alias(new string[] {"PhysicsCharacter", "Sandbox.PhysicsCharacter", "Sandbox.BodyController"})]
    [HelpUrl("https://sbox.game/dev/doc/reference/components/player-controller/")]
    public partial class PlayerController : 
        Component,
        IScenePhysicsEvents,
        ISceneEvent<IScenePhysicsEvents>,
        Component.ExecuteInEditor
    {
        #region Fields
        
        // Animation
        private SkinnedModelRenderer _renderer;
        
        // Camera
        private float _cameraDistance = 100f;
        private float _eyez;
        
        // Physics constants
        private const float _skin = 0.095f;
        
        // Component visibility
        private bool _showRigidBodyComponent;
        private bool _showColliderComponent;
        
        // Ground tracking
        private global::Transform _groundTransform;
        private global::Transform localGroundTransform;
        private int groundHash;
        
        // Synced properties backing fields
        private Vector3 _wishVelocity;
        private Angles _eyeAngles;
        private bool _isDucking;
        
        // Fall tracking
        private bool _wasFalling;
        private float fallDistance;
        private Vector3 prevPosition;
        
        // Footsteps
        private TimeSince _timeSinceStep;
        
        // Ground timing
        private TimeUntil _timeUntilAllowedGround = (TimeUntil)0.0f;
        
        // Input timing
        private TimeSince timeSinceJump = (TimeSince)0.0f;
        
        // Ducking
        private float unduckedHeight = -1f;
        private Vector3 bodyDuckOffset = Vector3.Zero;
        
        // Step system
        private bool _didstep;
        private Vector3 _stepPosition;

        #endregion

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

        

        #region Body Properties

        [Property]
        [Hide]
        [RequireComponent]
        public Rigidbody Body { get; set; }

        public CapsuleCollider BodyCollider { get; private set; }

        public BoxCollider FeetCollider { get; private set; }

        [Property]
        [Hide]
        public GameObject ColliderObject { get; private set; }

        [Property]
        [Group("Body")]
        [Range(1f, 64f)]
        public float BodyRadius { get; set; } = 16f;

        [Property]
        [Group("Body")]
        [Range(1f, 128f)]
        public float BodyHeight { get; set; } = 72f;

        [Property]
        [Group("Body")]
        [Range(1f, 1000f)]
        public float BodyMass { get; set; } = 500f;

        [Property]
        [Group("Body")]
        public TagSet BodyCollisionTags { get; set; }

        #endregion

        #region Physics Properties

        [Property]
        [Group("Physics")]
        [Range(0.0f, 1f)]
        [Description("We will apply extra friction when we're on the ground and our desired velocity is lower than our current velocity, so we will slow down.")]
        public float BrakePower { get; set; } = 1f;

        [Property]
        [Group("Physics")]
        [Range(0.0f, 1f)]
        [Description("How much friction to add when we're in the air. This will slow you down unless you have a wish velocity.")]
        public float AirFriction { get; set; } = 0.1f;

        #endregion

        #region Component Visibility Properties

        [Property]
        [Group("Components")]
        [Title("Show Rigidbody")]
        public bool ShowRigidbodyComponent
        {
            get => _showRigidBodyComponent;
            set
            {
                _showRigidBodyComponent = value;
                if (Body.IsValid())
                    Body.Flags = Body.Flags.WithFlag<ComponentFlags>(ComponentFlags.Hidden, !value);
            }
        }

        [Property]
        [Group("Components")]
        [Title("Show Colliders")]
        public bool ShowColliderComponents
        {
            get => _showColliderComponent;
            set
            {
                _showColliderComponent = value;
                if (ColliderObject.IsValid())
                    ColliderObject.Flags = ColliderObject.Flags.WithFlag<GameObjectFlags>(GameObjectFlags.Hidden, !value);
                if (BodyCollider.IsValid())
                    BodyCollider.Flags = BodyCollider.Flags.WithFlag<ComponentFlags>(ComponentFlags.Hidden, !value);
                if (FeetCollider.IsValid())
                    FeetCollider.Flags = FeetCollider.Flags.WithFlag<ComponentFlags>(ComponentFlags.Hidden, !value);
            }
        }

        #endregion

        #region Movement Properties

        [Sync]
        public Vector3 WishVelocity
        {
            set => _wishVelocity = value;
            get => _wishVelocity;
        }

        public bool IsOnGround => GroundObject.IsValid();

        [Description("Our actual physical velocity minus our ground velocity")]
        public Vector3 Velocity { get; private set; }

        [Description("The velocity that the ground underneath us is moving")]
        public Vector3 GroundVelocity { get; set; }

        [Description("Set to true when entering a climbing MoveMode.")]
        public bool IsClimbing { get; set; }

        [Description("Set to true when entering a swimming MoveMode.")]
        public bool IsSwimming { get; set; }

        #endregion

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
        [ToggleGroup("EnablePressing", Label = "Enable Pressing")]
        [Description("Allows to player to interact with things by \"use\"ing them. Usually by pressing the \"use\" button.")]
        public bool EnablePressing { get; set; } = true;

        [Property]
        [Feature("Input")]
        [Group("EnablePressing")]
        [InputAction]
        [Description("The button that the player will press to use things")]
        public string UseButton { get; set; } = "use";

        [Property]
        [Feature("Input")]
        [Group("EnablePressing")]
        [Description("How far from the eye can the player reach to use things")]
        public float ReachLength { get; set; } = 130f;

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

        #region Pressing Properties

        [Description("The object we're currently looking at")]
        public Component Hovered { get; set; }

        [Description("The object we're currently using by holding down USE")]
        public Component Pressed { get; set; }

        #endregion

        #region Step Properties

        [Description("Enable debug overlays for this character")]
        public bool StepDebug { get; set; }

        #endregion

        #region Movement Mode

        public MoveMode Mode { get; private set; }

        #endregion

        #region Lifecycle Methods

        protected override void OnAwake()
        {
            base.OnAwake();
            
            var boxCollider = GetComponent<BoxCollider>();
            var capsuleCollider = GetComponent<CapsuleCollider>();
            if (boxCollider.IsValid() && capsuleCollider.IsValid() && !boxCollider.IsTrigger && !capsuleCollider.IsTrigger)
            {
                boxCollider.Destroy();
                capsuleCollider.Destroy();
            }
            
            Mode = (MoveMode)GetOrAddComponent<MoveModeWalk>();
            EnsureComponentsCreated();
            UpdateBody();
            Body.Velocity = Vector3.Zero;
        }

        protected override void OnEnabled()
        {
            base.OnEnabled();
            if (Scene.IsEditor) return;
            
            EyeAngles = WorldRotation.Angles() with { pitch = 0.0f, roll = 0.0f };
            WorldRotation = Rotation.Identity;
            
            if (Renderer != null)
                Renderer.WorldRotation = new Angles(0.0f, EyeAngles.yaw, 0.0f).ToRotation();
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
            ColliderObject?.Destroy();
            ColliderObject = null;
            Body?.Destroy();
            Body = null;
        }

        protected override void OnDisabled()
        {
            base.OnDisabled();
            DisableAnimationEvents();
            StopPressing();
        }

        protected override void OnValidate()
        {
            EnsureComponentsCreated();
            UpdateBody();
        }

        protected override void OnUpdate()
        {
            UpdateGroundEyeRotation();
            if (Scene.IsEditor) return;
            
            if (!IsProxy)
            {
                if (UseInputControls && UseLookControls)
                {
                    UpdateEyeAngles();
                    UpdateLookAt();
                }
                if (UseCameraControls)
                    UpdateCameraPosition();
            }
            
            UpdateBodyVisibility();
            
            if (UseAnimatorControls && Renderer.IsValid())
                UpdateAnimation(Renderer);
        }

        protected override void OnFixedUpdate()
        {
            if (Scene.IsEditor) return;
            
            UpdateHeadroom();
            UpdateFalling();
            prevPosition = WorldPosition;
            
            if (IsProxy || !UseInputControls) return;
            InputTick();
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

        #region Pressing Methods

        [Description("Called in Update when Using is enabled")]
        public void UpdateLookAt()
        {
            if (!EnablePressing) return;
            
            if (Pressed.IsValid())
                UpdatePressed();
            else
                UpdateHovered();
        }

        [Description("Called every frame to update our pressed object")]
        private void UpdatePressed()
        {
            if (string.IsNullOrWhiteSpace(UseButton)) return;
            
            bool shouldContinue = Input.Down(UseButton);
            
            if (shouldContinue && Pressed is Component.IPressable pressable)
            {
                var pressEvent = new Component.IPressable.Event
                {
                    Ray = EyeTransform.ForwardRay,
                    Source = this
                };
                shouldContinue = pressable.Pressing(pressEvent);
            }
            
            if (GetDistanceFromGameObject(Pressed.GameObject, EyePosition) > ReachLength)
                shouldContinue = false;
            
            if (!shouldContinue)
                StopPressing();
        }

        private float GetDistanceFromGameObject(GameObject obj, Vector3 point)
        {
            float minDistance = Vector3.DistanceBetween(obj.WorldPosition, EyePosition);
            
            foreach (var collider in Pressed.GetComponentsInChildren<Collider>())
            {
                float distance = Vector3.DistanceBetween(collider.FindClosestPoint(EyePosition), EyePosition);
                if (distance < minDistance)
                    minDistance = distance;
            }
            
            return minDistance;
        }

        [Description("Called every frame to update our hovered status, unless it's being pressed")]
        private void UpdateHovered()
        {
            SwitchHovered(TryGetLookedAt());
            
            if (Hovered is Component.IPressable hovered)
            {
                var lookEvent = new Component.IPressable.Event
                {
                    Ray = EyeTransform.ForwardRay,
                    Source = this
                };
                hovered.Look(lookEvent);
            }
            
            if (Input.Pressed(UseButton))
                StartPressing(Hovered);
        }

        [Description("Stop pressing. Pressed will become null.")]
        public void StopPressing()
        {
            if (!Pressed.IsValid()) return;
            
            ISceneEvent<PlayerController.IEvents>.PostToGameObject(GameObject, x => x.StopPressing(Pressed));
            
            if (Pressed is Component.IPressable pressable)
            {
                var releaseEvent = new Component.IPressable.Event
                {
                    Ray = EyeTransform.ForwardRay,
                    Source = this
                };
                pressable.Release(releaseEvent);
            }
            
            Pressed = null;
        }

        [Description("Start pressing a target component. This is called automatically when Use is pressed.")]
        public void StartPressing(Component obj)
        {
            StopPressing();
            
            if (!obj.IsValid())
            {
                ISceneEvent<PlayerController.IEvents>.PostToGameObject(GameObject, x => x.FailPressing());
                return;
            }
            
            var pressable = obj.GetComponent<Component.IPressable>();
            if (pressable != null)
            {
                var pressEvent = new Component.IPressable.Event
                {
                    Ray = EyeTransform.ForwardRay,
                    Source = this
                };
                
                if (!pressable.CanPress(pressEvent))
                {
                    ISceneEvent<PlayerController.IEvents>.PostToGameObject(GameObject, x => x.FailPressing());
                    return;
                }
                
                pressable.Press(pressEvent);
            }
            
            Pressed = obj;
            if (Pressed.IsValid())
                ISceneEvent<PlayerController.IEvents>.PostToGameObject(GameObject, x => x.StartPressing(Pressed));
        }

        [Description("Called every frame with the component we're looking at - even if it's null")]
        private void SwitchHovered(Component obj)
        {
            var lookEvent = new Component.IPressable.Event
            {
                Ray = EyeTransform.ForwardRay,
                Source = this
            };
            
            if (Hovered == obj)
            {
                if (Hovered is Component.IPressable hovered)
                    hovered.Look(lookEvent);
                return;
            }
            
            if (Hovered is Component.IPressable oldHovered)
            {
                oldHovered.Blur(lookEvent);
                Hovered = null;
            }
            
            Hovered = obj;
            if (Hovered is Component.IPressable newHovered)
            {
                newHovered.Hover(lookEvent);
                newHovered.Look(lookEvent);
            }
        }

        [Description("Get the best component we're looking at. We don't just return any old component, by default we only return components that implement IPressable. Components can implement GetUsableComponent to search and provide better alternatives.")]
        private Component TryGetLookedAt()
        {
            for (float radius = 0.0f; radius <= 4.0; radius += 2f)
            {
                var eyeTrace = Scene.Trace
                    .Ray(EyePosition, EyePosition + EyeAngles.Forward * (ReachLength - radius))
                    .IgnoreGameObjectHierarchy(GameObject)
                    .Radius(radius)
                    .Run();
                
                if (!eyeTrace.Hit || !eyeTrace.GameObject.IsValid()) continue;
                
                Component foundComponent = null;
                ISceneEvent<PlayerController.IEvents>.PostToGameObject(GameObject, 
                    x => foundComponent = x.GetUsableComponent(eyeTrace.GameObject) ?? foundComponent);
                
                if (foundComponent.IsValid())
                    return foundComponent;
                
                foreach (var pressable in eyeTrace.GameObject.GetComponents<Component.IPressable>())
                {
                    var canPressEvent = new Component.IPressable.Event
                    {
                        Ray = EyeTransform.ForwardRay,
                        Source = this
                    };
                    
                    if (pressable.CanPress(canPressEvent))
                        return pressable as Component;
                }
            }
            
            return null;
        }

        #endregion

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

        #region Step Methods

        [Description("Try to step up. Will trace forward, then up, then across, then down.")]
        internal void TryStep(float maxDistance)
        {
            _didstep = false;
            if (!Body.IsValid()) return;
            
            var horizontalVelocity = Body.Velocity.WithZ(0.0f);
            if (horizontalVelocity.IsNearlyZero() || _timeUntilAllowedGround > 0) return;
            
            var worldPosition = WorldPosition;
            var movementDirection = horizontalVelocity * Time.Delta;
            float scale = 1f;
            var from = worldPosition - movementDirection.Normal * 0.095f;
            var to = worldPosition + movementDirection;
            
            // Find obstruction
            SceneTraceResult forwardTrace;
            do
            {
                forwardTrace = TraceBody(from, to, scale);
                if (!forwardTrace.StartedSolid) break;
                
                scale -= 0.1f;
                if (scale < 0.6f) return;
            } while (true);
            
            if (!forwardTrace.Hit) return;
            
            if (StepDebug)
                DebugOverlay.Line(from, to, global::Color.Green, 10f);
            
            movementDirection = movementDirection.Normal * (movementDirection.Length - forwardTrace.Distance);
            if (movementDirection.Length <= 0.0) return;
            
            // Try stepping up
            var obstructionPoint = forwardTrace.EndPosition;
            var upTrace = TraceBody(obstructionPoint, obstructionPoint + Vector3.Up * maxDistance, scale);
            
            if (upTrace.StartedSolid) return;
            
            if (upTrace.Distance < 2.0)
            {
                if (StepDebug)
                    DebugOverlay.Line(obstructionPoint, upTrace.EndPosition, global::Color.Red, 10f);
                return;
            }
            
            if (StepDebug)
                DebugOverlay.Line(obstructionPoint, upTrace.EndPosition, global::Color.Green, 10f);
            
            // Try moving across
            var stepUpPoint = upTrace.EndPosition;
            var acrossTrace = TraceBody(stepUpPoint, stepUpPoint + movementDirection, scale);
            
            if (acrossTrace.StartedSolid) return;
            
            if (StepDebug)
                DebugOverlay.Line(stepUpPoint, stepUpPoint + movementDirection, global::Color.Green, 10f);
            
            // Try stepping down
            var acrossEndPoint = acrossTrace.EndPosition;
            var downTrace = TraceBody(acrossEndPoint, acrossEndPoint + Vector3.Down * maxDistance, scale);
            
            if (!downTrace.Hit)
            {
                if (StepDebug)
                    DebugOverlay.Line(acrossEndPoint, acrossEndPoint + Vector3.Down * maxDistance, global::Color.Red, 10f);
                return;
            }
            
            if (!Mode.IsStandableSurface(downTrace)) return;
            
            _didstep = true;
            _stepPosition = downTrace.EndPosition + Vector3.Up * 0.095f;
            Body.WorldPosition = _stepPosition;
            Body.Velocity = Body.Velocity.WithZ(0.0f) * 0.9f;
            
            if (StepDebug)
                DebugOverlay.Line(acrossEndPoint, _stepPosition, global::Color.Green, 10f);
        }

        [Description("If we stepped up on the previous step, we suck our position back to the previous position after the physics step to avoid adding double velocity. This is technically wrong but doens't seem to cause any harm right now")]
        private void RestoreStep()
        {
            if (!_didstep) return;
            
            _didstep = false;
            Body.WorldPosition = _stepPosition;
        }

        #endregion

        #region Component Management Methods

        [Description("Make sure the body and our components are created")]
        private void EnsureComponentsCreated()
        {
            if (!ColliderObject.IsValid())
            {
                ColliderObject = GameObject.Children.FirstOrDefault(x => x.Name == "Colliders");
                if (!ColliderObject.IsValid())
                    ColliderObject = new GameObject(GameObject, name: "Colliders");
            }
            
            ColliderObject.LocalTransform = global::Transform.Zero;
            ColliderObject.Tags.SetFrom(BodyCollisionTags);
            
            Body.CollisionEventsEnabled = true;
            Body.CollisionUpdateEventsEnabled = true;
            Body.RigidbodyFlags = RigidbodyFlags.DisableCollisionSounds;
            
            BodyCollider = ColliderObject.GetOrAddComponent<CapsuleCollider>();
            FeetCollider = ColliderObject.GetOrAddComponent<BoxCollider>();
            
            Body.Flags = Body.Flags.WithFlag<ComponentFlags>(ComponentFlags.Hidden, !_showRigidBodyComponent);
            ColliderObject.Flags = ColliderObject.Flags.WithFlag<GameObjectFlags>(GameObjectFlags.Hidden, !_showColliderComponent);
            BodyCollider.Flags = BodyCollider.Flags.WithFlag<ComponentFlags>(ComponentFlags.Hidden, !_showColliderComponent);
            FeetCollider.Flags = FeetCollider.Flags.WithFlag<ComponentFlags>(ComponentFlags.Hidden, !_showColliderComponent);
            
            if (Renderer == null && UseAnimatorControls)
                Renderer = GetComponentInChildren<SkinnedModelRenderer>();
        }

        [Description("Update the body dimensions, and change the physical properties based on the current state")]
        private void UpdateBody()
        {
            float halfHeight = BodyHeight * 0.5f;
            float radius = BodyRadius * MathF.Sqrt(2f) / 2.0f;
            
            float friction = 0.0f;
            if (IsOnGround && (WishVelocity.Length < 5.0 || WishVelocity.Length < Velocity.Length * 0.9f))
                friction = 1.0f + 100.0f * BrakePower * GroundFriction;
            
            // Setup body collider
            BodyCollider.Radius = radius;
            BodyCollider.Start = Vector3.Up * (BodyHeight - BodyCollider.Radius);
            BodyCollider.End = Vector3.Up * (BodyCollider.Radius + halfHeight - BodyCollider.Radius * 0.2f);
            BodyCollider.Friction = 0.0f;
            BodyCollider.Enabled = true;
            
            // Setup feet collider
            FeetCollider.Scale = new Vector3(BodyRadius, BodyRadius, halfHeight);
            FeetCollider.Center = new Vector3(0.0f, 0.0f, halfHeight * 0.5f);
            FeetCollider.Friction = friction;
            FeetCollider.Enabled = true;
            
            // Setup rigidbody
            Body.Locking = Body.Locking with { Pitch = true, Yaw = true, Roll = true };
            Body.MassOverride = BodyMass;
            Body.MassCenterOverride = new Vector3(0.0f, 0.0f, 
                IsOnGround ? WishVelocity.Length.Clamp(0.0f, BodyHeight * 0.5f) : BodyHeight * 0.5f);
            Body.OverrideMassCenter = true;
            
            Mode?.UpdateRigidBody(Body);
        }

        private void ChooseBestMoveMode()
        {
            var bestMode = GetComponents<MoveMode>().MaxBy(x => x.Score(this));
            if (Mode == bestMode) return;
            
            Mode?.OnModeEnd(bestMode);
            Mode = bestMode;
            Body.PhysicsBody.Sleeping = false;
            Mode?.OnModeBegin();
        }

        #endregion

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
    }
}
