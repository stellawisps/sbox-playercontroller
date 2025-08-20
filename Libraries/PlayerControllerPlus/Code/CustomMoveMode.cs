using Sandbox.Internal;
using System;
using System.Linq;
using Sandbox;

namespace CustomMovement
{
    /// <summary>A move mode for this character</summary>
    [Description("A move mode for this character")]
    public abstract class MoveMode : Component
    {
        #region Animation Fields
        private Vector3.SmoothDamped smoothedMove = new Vector3.SmoothDamped(Vector3.Zero, Vector3.Zero, 0.5f);
        private Vector3.SmoothDamped smoothedWish = new Vector3.SmoothDamped(Vector3.Zero, Vector3.Zero, 0.5f);
        private Vector3.SmoothDamped smoothedSkid = new Vector3.SmoothDamped(Vector3.Zero, Vector3.Zero, 0.5f);
        private const float RotationSpeedUpdatePeriod = 0.1f;
        private float _animRotationSpeed;
        private TimeSince _timeSinceRotationSpeedUpdate;
        #endregion

        #region Movement Fields
        private Vector3.SmoothDamped smoothedMovement;
        #endregion

        #region Properties

        public virtual bool AllowGrounding => false;

        public virtual bool AllowFalling => false;

        [RequireComponent]
        public PlayerController Controller { get; set; }

        #endregion

        #region Abstract and Virtual Methods

        /// <summary>Highest number becomes the new control mode</summary>
        [Description("Highest number becomes the new control mode")]
        public virtual int Score(PlayerController controller) => 0;

        /// <summary>Called before the physics step is run</summary>
        [Description("Called before the physics step is run")]
        public virtual void PrePhysicsStep() { }

        /// <summary>Called after the physics step is run</summary>
        [Description("Called after the physics step is run")]
        public virtual void PostPhysicsStep() { }

        /// <summary>This mode has just started</summary>
        [Description("This mode has just started")]
        public virtual void OnModeBegin() { }

        /// <summary>
        /// This mode has stopped. We're swapping to another move mode.
        /// </summary>
        [Description("This mode has stopped. We're swapping to another move mode.")]
        public virtual void OnModeEnd(MoveMode next) { }

        public virtual bool IsStandableSurace(in SceneTraceResult result) => false;

        public virtual bool IsStandableSurface(in SceneTraceResult result) => IsStandableSurace(in result);

        #endregion

        #region Animation Methods

        /// <summary>
        /// Update the animator which is available at Controller.Renderer.
        /// </summary>
        [Description("Update the animator which is available at Controller.Renderer.")]
        public virtual void UpdateAnimator(SkinnedModelRenderer renderer)
        {
            OnUpdateAnimatorVelocity(renderer);
            OnUpdateAnimatorState(renderer);
            OnUpdateAnimatorLookDirection(renderer);
            UpdateRotationSpeed(renderer);
            OnRotateRenderBody(renderer);
        }

        /// <summary>
        /// Sets animation parameters on renderer based on the current
        /// PlayerController.Velocity and PlayerController.WishVelocity.
        /// </summary>
        [Description("Sets animation parameters on renderer based on the current PlayerController.Velocity and PlayerController.WishVelocity.")]
        protected virtual void OnUpdateAnimatorVelocity(SkinnedModelRenderer renderer)
        {
            var worldRotation = renderer.WorldRotation;
            var velocity = Controller.Velocity;
            var wishVelocity = Controller.WishVelocity;
            
            // Update smoothed movement
            smoothedMove.Target = velocity;
            smoothedMove.SmoothTime = 0.2f;
            smoothedMove.Update(Time.Delta);
            
            // Calculate skid effect
            var skidTarget = Vector3.Lerp(
                (smoothedMove.Current - velocity) * 0.5f, 
                velocity, 
                wishVelocity.Length.Remap(100f, 0.0f)
            );
            smoothedSkid.Target = skidTarget;
            smoothedSkid.SmoothTime = 0.5f;
            smoothedSkid.Update(Time.Delta);
            
            var skidLocal = GetLocalVelocity(worldRotation, smoothedSkid.Current);
            renderer.Set("skid_x", skidLocal.x / 400f);
            renderer.Set("skid_y", skidLocal.y / 400f);
            
            // Update smoothed wish velocity
            smoothedWish.Target = wishVelocity;
            smoothedWish.SmoothTime = 0.6f;
            smoothedWish.Update(Time.Delta);
            
            var moveLocal = GetLocalVelocity(worldRotation, ApplyDeadZone(smoothedWish.Current, 10f));
            renderer.Set("move_direction", GetAngle(moveLocal));
            renderer.Set("move_speed", moveLocal.Length);
            renderer.Set("move_groundspeed", moveLocal.WithZ(0.0f).Length);
            renderer.Set("move_x", moveLocal.x);
            renderer.Set("move_y", moveLocal.y);
            renderer.Set("move_z", moveLocal.z);
            
            var wishLocal = GetLocalVelocity(worldRotation, wishVelocity);
            renderer.Set("wish_direction", GetAngle(wishLocal));
            renderer.Set("wish_speed", wishVelocity.Length);
            renderer.Set("wish_groundspeed", wishVelocity.WithZ(0.0f).Length);
            renderer.Set("wish_x", wishLocal.x);
            renderer.Set("wish_y", wishLocal.y);
            renderer.Set("wish_z", wishLocal.z);
        }

        /// <summary>
        /// Sets animation parameters on renderer describing the movement style, like
        /// swimming, falling, or ducking.
        /// </summary>
        [Description("Sets animation parameters on renderer describing the movement style, like swimming, falling, or ducking.")]
        protected virtual void OnUpdateAnimatorState(SkinnedModelRenderer renderer)
        {
            renderer.Set("b_swim", Controller.IsSwimming);
            renderer.Set("b_climbing", Controller.IsClimbing);
            renderer.Set("b_grounded", Controller.IsOnGround || Controller.IsClimbing);
            
            float duck = Controller.Headroom.Remap(25f, 0.0f, 0.0f, 0.5f, true);
            if (Controller.IsDucking)
                duck = duck * 3f + 1f;
            
            renderer.Set("duck", duck);
        }

        /// <summary>
        /// Set animation parameters on renderer to look towards PlayerController.EyeAngles.
        /// </summary>
        [Description("Set animation parameters on renderer to look towards PlayerController.EyeAngles.")]
        protected virtual void OnUpdateAnimatorLookDirection(SkinnedModelRenderer renderer)
        {
            var eyeAngles = Controller.EyeAngles;
            renderer.SetLookDirection("aim_eyes", eyeAngles.Forward, Controller.AimStrengthEyes);
            renderer.SetLookDirection("aim_head", eyeAngles.Forward, Controller.AimStrengthHead);
            renderer.SetLookDirection("aim_body", eyeAngles.Forward, Controller.AimStrengthBody);
        }

        /// <summary>
        /// Updates the Component.WorldRotation of renderer.
        /// </summary>
        [Description("Updates the Component.WorldRotation of renderer.")]
        protected virtual void OnRotateRenderBody(SkinnedModelRenderer renderer)
        {
            var targetRotation = Rotation.FromYaw(Controller.EyeAngles.yaw);
            var wishVelocity = Controller.WishVelocity.WithZ(0.0f);
            float rotationDistance = renderer.WorldRotation.Distance(targetRotation);
            var oldRotation = renderer.WorldRotation;
            
            if (rotationDistance > Controller.RotationAngleLimit)
            {
                float fraction = 0.999f - Controller.RotationAngleLimit / rotationDistance;
                renderer.WorldRotation = Rotation.Lerp(renderer.WorldRotation, targetRotation, fraction);
            }
            
            if (wishVelocity.Length > 10.0)
            {
                float rotationSpeed = Time.Delta * 2f * Controller.RotationSpeed * wishVelocity.Length.Remap(0.0f, 100f);
                renderer.WorldRotation = Rotation.Slerp(renderer.WorldRotation, targetRotation, rotationSpeed);
            }
            
            AddRotationSpeed(oldRotation, renderer.WorldRotation);
        }

        private void AddRotationSpeed(Rotation oldRotation, Rotation newRotation)
        {
            float oldYaw = oldRotation.Angles().yaw;
            float newYaw = newRotation.Angles().yaw;
            _animRotationSpeed = (_animRotationSpeed + MathX.DeltaDegrees(newYaw, oldYaw)).Clamp(-90f, 90f);
        }

        private void UpdateRotationSpeed(SkinnedModelRenderer renderer)
        {
            if (_timeSinceRotationSpeedUpdate < 0.1f) return;
            
            renderer.Set("move_rotationspeed", _animRotationSpeed * 5f);
            _timeSinceRotationSpeedUpdate = (TimeSince)0.0f;
            _animRotationSpeed = 0.0f;
        }

        #endregion

        #region Animation Helper Methods

        private static Vector3 ApplyDeadZone(Vector3 velocity, float minimum)
        {
            return !velocity.IsNearlyZero(minimum) ? velocity : Vector3.Zero;
        }

        private static Vector3 GetLocalVelocity(Rotation rotation, Vector3 worldVelocity)
        {
            return new Vector3(
                rotation.Forward.Dot(worldVelocity),
                rotation.Right.Dot(worldVelocity),
                worldVelocity.z
            );
        }

        private static float GetAngle(Vector3 localVelocity)
        {
            return MathF.Atan2(localVelocity.y, localVelocity.x).RadianToDegree().NormalizeDegrees();
        }

        #endregion

        #region Physics Methods

        public virtual void UpdateRigidBody(Rigidbody body)
        {
            bool shouldHaveGravity = !Controller.IsOnGround || 
                                   Controller.Velocity.Length > 1.0 || 
                                   Controller.GroundVelocity.Length > 1.0 || 
                                   Controller.GroundIsDynamic;
            
            body.Gravity = shouldHaveGravity;
            
            bool isStationary = Controller.IsOnGround && 
                              Controller.WishVelocity.Length < 1.0 && 
                              Controller.GroundVelocity.Length < 1.0;
            
            body.LinearDamping = isStationary ? 10f * Controller.BrakePower : Controller.AirFriction;
            body.AngularDamping = 1f;
        }

        public virtual void AddVelocity()
        {
            var body = Controller.Body;
            var wishVelocity = Controller.WishVelocity;
            if (wishVelocity.IsNearZeroLength) return;
            
            float groundMultiplier = 0.25f + Controller.GroundFriction * 10.0f;
            var groundVelocity = Controller.GroundVelocity;
            float verticalVelocity = body.Velocity.z;
            var relativeVelocity = body.Velocity - Controller.GroundVelocity;
            float maxSpeed = MathF.Max(wishVelocity.Length, relativeVelocity.Length);
            
            Vector3 newVelocity;
            if (Controller.IsOnGround)
            {
                float acceleration = groundMultiplier;
                newVelocity = relativeVelocity.AddClamped(wishVelocity * acceleration, wishVelocity.Length * acceleration);
            }
            else
            {
                float airAcceleration = 0.05f;
                newVelocity = relativeVelocity.AddClamped(wishVelocity * airAcceleration, wishVelocity.Length);
            }
            
            if (newVelocity.Length > maxSpeed)
                newVelocity = newVelocity.Normal * maxSpeed;
            
            var finalVelocity = newVelocity + groundVelocity;
            if (Controller.IsOnGround)
                finalVelocity.z = verticalVelocity;
            
            body.Velocity = finalVelocity;
        }

        #endregion

        #region Movement Helper Methods

        /// <summary>If we're approaching a step, step up if possible</summary>
        [Description("If we're approaching a step, step up if possible")]
        protected void TrySteppingUp(float maxDistance) => Controller.TryStep(maxDistance);

        /// <summary>
        /// If we're on the ground, make sure we stay there by falling to the ground
        /// </summary>
        [Description("If we're on the ground, make sure we stay there by falling to the ground")]
        protected void StickToGround(float maxDistance) => Controller.Reground(maxDistance);

        #endregion

        #region Input Methods

        /// <summary>Read inputs, return WishVelocity</summary>
        [Description("Read inputs, return WishVelocity")]
        public virtual Vector3 UpdateMove(Rotation eyes, Vector3 input)
        {
            input = input.ClampLength(1f);
            var worldInput = eyes * input;
            
            bool isRunning = Input.Down(Controller.AltMoveButton);
            if (Controller.RunByDefault)
                isRunning = !isRunning;
            
            float targetSpeed = isRunning ? Controller.RunSpeed : Controller.WalkSpeed;
            if (Controller.IsDucking)
                targetSpeed = Controller.DuckedSpeed;
            
            if (worldInput.IsNearlyZero(0.1f))
            {
                worldInput = Vector3.Zero;
            }
            else
            {
                // Maintain direction when changing magnitude
                var currentDirection = worldInput.Normal;
                var currentMagnitude = smoothedMovement.Current.Length;
                smoothedMovement.Current = currentDirection * currentMagnitude;
            }
            
            smoothedMovement.Target = worldInput * targetSpeed;
            
            // Use different smooth times for acceleration vs deceleration
            bool isAccelerating = smoothedMovement.Target.Length > smoothedMovement.Current.Length;
            smoothedMovement.SmoothTime = isAccelerating ? Controller.AccelerationTime : Controller.DeaccelerationTime;
            smoothedMovement.Update(Time.Delta);
            
            if (smoothedMovement.Current.IsNearlyZero(0.01f))
                smoothedMovement.Current = Vector3.Zero;
            
            return smoothedMovement.Current;
        }

        #endregion
    }

    /// <summary>The character is walking</summary>
    [Icon("transfer_within_a_station")]
    [Group("Movement")]
    [Title("CustomMoveMode - Walk")]
    [Alias(new string[] {"Sandbox.PhysicsCharacterMode.PhysicsCharacterWalkMode"})]
    [Description("The character is walking")]
    public class MoveModeWalk : MoveMode
    {
        #region Properties

        [Property]
        public int Priority { get; set; }

        [Property]
        public float GroundAngle { get; set; } = 45f;

        [Property]
        public float StepUpHeight { get; set; } = 18f;

        [Property]
        public float StepDownHeight { get; set; } = 18f;

        #endregion

        #region Overrides

        public override bool AllowGrounding => true;

        public override bool AllowFalling => true;

        public override int Score(PlayerController controller) => Priority;

        public override void AddVelocity()
        {
            Controller.WishVelocity = Controller.WishVelocity.WithZ(0.0f);
            base.AddVelocity();
        }

        public override void PrePhysicsStep()
        {
            base.PrePhysicsStep();
            if (StepUpHeight > 0.0)
                TrySteppingUp(StepUpHeight);
        }

        public override void PostPhysicsStep()
        {
            base.PostPhysicsStep();
            StickToGround(StepDownHeight);
        }

        public override bool IsStandableSurface(in SceneTraceResult result)
        {
            return Vector3.GetAngle(Vector3.Up, result.Normal) <= GroundAngle;
        }

        public override Vector3 UpdateMove(Rotation eyes, Vector3 input)
        {
            var angles = eyes.Angles() with { pitch = 0.0f };
            return base.UpdateMove(angles.ToRotation(), input);
        }

        #endregion
    }

    /// <summary>The character is swimming</summary>
    [Icon("scuba_diving")]
    [Group("Movement")]
    [Title("CustomMoveMode - Swim")]
    [Description("The character is swimming")]
    public class MoveModeSwim : MoveMode
    {
        #region Properties

        [Property]
        public int Priority { get; set; } = 10;

        [Property]
        [Range(0.0f, 1f)]
        public float SwimLevel { get; set; } = 0.7f;

        /// <summary>
        /// Will will update this based on how much you're in a "water" tagged trigger
        /// </summary>
        [Description("Will will update this based on how much you're in a \"water\" tagged trigger")]
        public float WaterLevel { get; private set; }

        #endregion

        #region Overrides

        public override void UpdateRigidBody(Rigidbody body)
        {
            body.Gravity = false;
            body.LinearDamping = 3.3f;
            body.AngularDamping = 1f;
        }

        public override int Score(PlayerController controller)
        {
            return WaterLevel > SwimLevel ? Priority : -100;
        }

        public override void OnModeBegin()
        {
            Controller.IsSwimming = true;
        }

        public override void OnModeEnd(MoveMode next)
        {
            Controller.IsSwimming = false;
            if (Input.Down("Jump"))
                Controller.Jump(Vector3.Up * 300f);
        }

        public override Vector3 UpdateMove(Rotation eyes, Vector3 input)
        {
            if (Input.Down("jump"))
                input += Vector3.Up;
            return base.UpdateMove(eyes, input);
        }

        protected override void OnFixedUpdate()
        {
            UpdateWaterLevel();
        }

        #endregion

        #region Private Methods

        private void UpdateWaterLevel()
        {
            var worldTransform = WorldTransform;
            var headPosition = worldTransform.PointToWorld(new Vector3(0.0f, 0.0f, Controller.BodyHeight));
            var feetPosition = worldTransform.Position;
            
            float maxWaterLevel = 0.0f;
            
            foreach (var collider in Controller.Body.Touching)
            {
                if (!collider.Tags.Contains("water")) continue;
                
                var closestPoint = collider.FindClosestPoint(headPosition);
                float waterLevel = Vector3.InverseLerp(closestPoint, feetPosition, headPosition);
                waterLevel = MathF.Ceiling(waterLevel * 100f) / 100f;
                
                if (waterLevel > maxWaterLevel)
                    maxWaterLevel = waterLevel;
            }
            
            WaterLevel = maxWaterLevel;
        }

        #endregion
    }

    /// <summary>The character is climbing up a ladder</summary>
    [Icon("hiking")]
    [Group("Movement")]
    [Title("CustomMoveMode - Ladder")]
    [Description("The character is climbing up a ladder")]
    public class MoveModeLadder : MoveMode
    {
        #region Properties

        [Property]
        public int Priority { get; set; } = 5;

        [Property]
        [Range(0.0f, 2f)]
        public float Speed { get; set; } = 1f;

        /// <summary>
        /// A list of tags we can climb up - when they're on triggers
        /// </summary>
        [Property]
        [Description("A list of tags we can climb up - when they're on triggers")]
        public TagSet ClimbableTags { get; set; }

        /// <summary>
        /// The GameObject we're climbing. This will usually be a ladder trigger.
        /// </summary>
        [Description("The GameObject we're climbing. This will usually be a ladder trigger.")]
        public GameObject ClimbingObject { get; set; }

        /// <summary>
        /// When climbing, this is the rotation of the wall/ladder you're climbing, where
        /// Forward is the direction to look at the ladder, and Up is the direction to climb.
        /// </summary>
        [Description("When climbing, this is the rotation of the wall/ladder you're climbing, where Forward is the direction to look at the ladder, and Up is the direction to climb.")]
        public Rotation ClimbingRotation { get; set; }

        #endregion

        #region Constructor

        public MoveModeLadder()
        {
            ClimbableTags = new TagSet();
            ClimbableTags.Add("ladder");
        }

        #endregion

        #region Overrides

        public override void UpdateRigidBody(Rigidbody body)
        {
            body.Gravity = false;
            body.LinearDamping = 20f;
            body.AngularDamping = 1f;
        }

        public override int Score(PlayerController controller)
        {
            return ClimbingObject.IsValid() ? Priority : -100;
        }

        public override void OnModeBegin()
        {
            Controller.IsClimbing = true;
            Controller.Body.Velocity = Vector3.Zero;
        }

        public override void OnModeEnd(MoveMode next)
        {
            Controller.IsClimbing = false;
            Controller.Body.Velocity = Controller.Body.Velocity.ClampLength(Controller.RunSpeed);
        }

        public override void PostPhysicsStep()
        {
            UpdatePositionOnLadder();
        }

        public override Vector3 UpdateMove(Rotation eyes, Vector3 input)
        {
            var climbInput = new Vector3(0.0f, 0.0f, Input.AnalogMove.x);
            
            // Reverse direction if looking down
            if (eyes.Pitch() > 50.0)
                climbInput *= -1f;
            
            var climbVelocity = climbInput * (1500f * Speed);
            
            // Jump off ladder
            if (Input.Down("jump"))
                Controller.Jump(ClimbingRotation.Backward * 200f);
            
            return climbVelocity;
        }

        protected override void OnRotateRenderBody(SkinnedModelRenderer renderer)
        {
            renderer.WorldRotation = Rotation.Lerp(renderer.WorldRotation, ClimbingRotation, Time.Delta * 5f);
        }

        protected override void OnFixedUpdate()
        {
            ScanForLadders();
        }

        #endregion

        #region Private Methods

        private void UpdatePositionOnLadder()
        {
            if (!ClimbingObject.IsValid()) return;
            
            var playerPosition = Controller.WorldPosition;
            var ladderPosition = ClimbingObject.WorldPosition;
            var ladderRotation = ClimbingObject.WorldRotation;
            var ladderUp = ladderRotation.Up;
            
            // Find closest point on ladder's vertical line
            var ladderLine = new Line(ladderPosition - ladderUp * 1000f, ladderPosition + ladderUp * 1000f);
            var offsetFromLadder = ladderLine.ClosestPoint(playerPosition) - playerPosition;
            
            // Remove the forward component (don't pull player through the ladder)
            var correctionVector = offsetFromLadder.SubtractDirection(ladderRotation.Forward);
            
            if (correctionVector.Length > 0.01f)
            {
                var correctionForce = correctionVector * 5f;
                Controller.Body.Velocity = Controller.Body.Velocity.AddClamped(correctionForce, correctionVector.Length * 10f);
            }
        }

        private void ScanForLadders()
        {
            var worldTransform = WorldTransform;
            var headPosition = worldTransform.PointToWorld(new Vector3(0.0f, 0.0f, Controller.BodyHeight));
            var feetPosition = worldTransform.Position;
            
            GameObject foundLadder = null;
            
            foreach (var collider in Controller.Body.Touching)
            {
                if (!collider.Tags.HasAny(ClimbableTags)) continue;
                
                if (ClimbingObject == collider.GameObject)
                {
                    foundLadder = collider.GameObject;
                    continue;
                }
                
                // Check if we're more than halfway up the ladder trigger
                var closestPoint = collider.FindClosestPoint(headPosition);
                float ladderProgress = Vector3.InverseLerp(closestPoint, feetPosition, headPosition);
                
                if (ClimbingObject == collider.GameObject || ladderProgress >= 0.5f)
                {
                    foundLadder = collider.GameObject;
                    break;
                }
            }
            
            if (foundLadder == ClimbingObject) return;
            
            ClimbingObject = foundLadder;
            if (!ClimbingObject.IsValid()) return;
            
            // Set climbing rotation
            var directionToLadder = ClimbingObject.WorldPosition - WorldPosition;
            ClimbingRotation = ClimbingObject.WorldRotation;
            
            // If we're behind the ladder, flip the rotation
            if (directionToLadder.Dot(ClimbingRotation.Forward) < 0.0)
                ClimbingRotation *= new Angles(0.0f, 180f, 0.0f).ToRotation();
        }

        #endregion
    }
}
