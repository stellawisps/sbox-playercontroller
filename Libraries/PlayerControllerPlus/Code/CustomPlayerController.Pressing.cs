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
	    
	    #region Pressing Properties

	    [Description("The object we're currently looking at")]
	    public Component Hovered { get; set; }

	    [Description("The object we're currently using by holding down USE")]
	    public Component Pressed { get; set; }

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
    }
    
    
}
