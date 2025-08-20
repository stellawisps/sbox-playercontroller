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
    [Title("Custom Player Controller")]
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

        #region Step Properties

        [Description("Enable debug overlays for this character")]
        public bool StepDebug { get; set; }

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

       

        #endregion



        

        
    }
}
