﻿/******************************************************************************
 * Copyright (C) Leap Motion, Inc. 2011-2017.                                 *
 * Leap Motion proprietary and  confidential.                                 *
 *                                                                            *
 * Use subject to the terms of the Leap Motion SDK Agreement available at     *
 * https://developer.leapmotion.com/sdk_agreement, or another agreement       *
 * between Leap Motion and you, your company or other organization.           *
 ******************************************************************************/

using InteractionEngineUtility;
using Leap.Unity.Space;
using Leap.Unity.Interaction.Internal;
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Leap.Unity.RuntimeGizmos;

namespace Leap.Unity.Interaction {

  /// <summary>
  /// Specified on a per-object basis to allow Interaction objects
  /// to ignore hover for the left hand, right hand, or both hands.
  /// </summary>
  public enum IgnoreHoverMode { None, Left, Right, Both }

  /// <summary>
  /// The Interaction Engine can be controlled by hands tracked by the
  /// Leap Motion Controller, or by remote-style held controllers
  /// such as the Oculus Touch or Vive controller.
  /// </summary>
  public enum ControllerType { Hand, Remote }

  public abstract class InteractionControllerBase : MonoBehaviour {

    /// <summary>
    /// Gets whether the underlying object (Leap hand or a held controller) is currently
    /// in a tracked state. Objects grasped by a controller that becomes untracked will
    /// become "suspended" and receive specific suspension callbacks. (Implementing any
    /// behaviour during the suspension state is left up to the developer, however.)
    /// </summary>
    public abstract bool isTracked { get; }

    /// <summary>
    /// Gets whether the underlying object (Leap hand or a held controller) represents
    /// or is held by a left hand (true) or a right hand (false).
    /// </summary>
    public abstract bool isLeft { get; }

    /// <summary>
    /// Gets whether the underlying object (Leap hand or a held controller) represents
    /// or is held by a right hand (true) or a left hand (false).
    /// </summary>
    public bool isRight { get { return !isLeft; } }

    /// <summary>
    /// Returns the current velocity of this controller.
    /// </summary>
    public abstract Vector3 velocity { get; }

    /// <summary>
    /// Gets the type of controller this object represents underneath the InteractionControllerBase
    /// abstraction. If the type is ControllerType.Hand, the intHand property will contain the
    /// InteractionHand object this object abstracts from.
    /// </summary>
    public abstract ControllerType controllerType { get; }
    
    /// <summary>
    /// If this InteractionControllerBase's controllerType is ControllerType.Hand,
    /// this gets the InteractionHand, otherwise this returns null.
    /// </summary>
    public abstract InteractionHand intHand { get; }

    #region Events

    /// <summary>
    /// Called when this InteractionControllerBase begins primarily hovering over an InteractionBehaviour.
    /// If the controller transitions to primarily hovering a new object, OnEndPrimaryHoveringObject will
    /// first be called on the old object, then OnBeginPrimaryHoveringObject will be called for
    /// the new object.
    /// </summary>
    public Action<InteractionBehaviour> OnBeginPrimaryHoveringObject = (intObj) => { };

    /// <summary>
    /// Called when this InteractionControllerBase stops primarily hovering over an InteractionBehaviour.
    /// If the controller transitions to primarily-hovering a new object, OnEndPrimaryHoveringObject will
    /// first be called on the old object, then OnBeginPrimaryHoveringObject will be called for
    /// the new object.
    /// </summary>
    public Action<InteractionBehaviour> OnEndPrimaryHoveringObject = (intObj) => { };

    /// <summary>
    /// Called every (fixed) frame this InteractionControllerBase is primarily hovering over an InteractionBehaviour.
    /// </summary>
    public Action<InteractionBehaviour> OnStayPrimaryHoveringObject = (intObj) => { };

    #endregion

    #region Grasping

    // TODO: Move all o' dis down to the Grasping area.

    /// <summary> Gets whether the controller is currently grasping an object. </summary>
    public bool isGraspingObject { get { return _graspedObject != null; } }

    /// <summary> Gets the object the controller is currently grasping, or null if there is no such object. </summary>
    public IInteractionBehaviour graspedObject { get { return _graspedObject; } }

    /// <summary> Gets the set of objects currently considered graspable. </summary>
    public HashSet<IInteractionBehaviour> graspCandidates { get { return graspActivityManager.ActiveObjects; } }

    /// <summary>
    /// Returns approximately where the controller is grasping the currently grasped InteractionBehaviour.
    /// This method will print an error if the hand is not currently grasping an object.
    /// </summary>
    public abstract Vector3 GetGraspPoint();

    private List<InteractionControllerBase> _releasingControllersBuffer = new List<InteractionControllerBase>();
    /// <summary>
    /// Releases the object this hand is holding and returns true if the hand was holding an object,
    /// or false if there was no object to release. The released object will dispatch OnGraspEnd()
    /// immediately. The hand is guaranteed not to be holding an object directly after this method
    /// is called.
    /// </summary>
    public bool ReleaseGrasp() {
      if (_graspedObject == null) {
        return false;
      }
      else {
        _releasingControllersBuffer.Clear();
        _releasingControllersBuffer.Add(this);

        grabClassifier.NotifyGraspReleased(_graspedObject);

        _graspedObject.EndGrasp(_releasingControllersBuffer);
        _graspedObject = null;

        return true;
      }
    }

    /// <summary>
    /// As ReleaseGrasp(), but also outputs the released object into releasedObject if the hand
    /// successfully released an object.
    /// </summary>
    public bool ReleaseGrasp(out IInteractionBehaviour releasedObject) {
      releasedObject = _graspedObject;

      if (ReleaseGrasp()) {
        // releasedObject will be non-null
        return true;
      }

      // releasedObject will be null
      return false;
    }

    /// <summary>
    /// Attempts to release this hand's object, but only if the argument object is the object currently
    /// grasped by this hand. If the hand was holding the argument object, returns true, otherwise returns false.
    /// </summary>
    public bool ReleaseObject(IInteractionBehaviour toRelease) {
      if (_graspedObject == toRelease) {
        ReleaseGrasp();
        return true;
      }
      else {
        return false;
      }
    }

    #endregion

    public InteractionManager manager;

    protected virtual void Start() {
      if (manager == null) manager = InteractionManager.instance;
    }

    // TODO: F this!! make ignoreHover and doHovering properties so that state can
    // be updated immediately instead of having to have this stateful nonsense.
    private bool _didHoveringLastFrame, _didContactLastFrame, _didGraspingLastFrame;
    /// <summary>
    /// Called by the InteractionManager every fixed (physics) frame to populate the
    /// Interaction Hand with state from the Leap hand and perform bookkeeping operations.
    /// </summary>
    public void FixedUpdateControllerBase(bool doHovering, bool doContact, bool doGrasping) {
      using (new ProfilerSample("Fixed Update InteractionControllerBase", contactBoneParent)) {

        if (doHovering || _didHoveringLastFrame) { fixedUpdateHovering(); }
        if (doContact || _didContactLastFrame) { fixedUpdateContact(doContact); }
        if (doGrasping || _didGraspingLastFrame) { FixedUpdateGrasping(); }

        // To support checking for interaction-ends e.g. if grasping is disabled, need to update an extra
        // frame beyond the frame in which the feature was disabled
        _didHoveringLastFrame = doHovering;
        _didContactLastFrame = doContact;
        _didGraspingLastFrame = doGrasping;
      }
    }

    #region Hovering

    /// <summary>
    /// In addition to standard hover validity checks, you can set this filter property
    /// to further filter objects for hover consideration. Only objects for which this
    /// function returns true will be hover candidates (if the filter is not null).
    /// </summary>
    public Func<IInteractionBehaviour, bool> customHoverActivityFilter = null;

    // Hover Activity Filter
    private Func<Collider, IInteractionBehaviour> hoverActivityFilter;
    private IInteractionBehaviour hoverFilterFunc(Collider collider) {
      Rigidbody rigidbody = collider.attachedRigidbody;
      IInteractionBehaviour intObj = null;

      bool objectValidForHover = rigidbody != null
                                && manager.interactionObjectBodies.TryGetValue(rigidbody, out intObj)
                                && !intObj.ShouldIgnoreHover(this)
                                && (customHoverActivityFilter == null || customHoverActivityFilter(intObj));

      if (objectValidForHover) return intObj;
      else return null;
    }

    // Hover Activity Manager
    private ActivityManager<IInteractionBehaviour> _hoverActivityManager;
    public ActivityManager<IInteractionBehaviour> hoverActivityManager {
      get {
        if (_hoverActivityManager == null) {
          if (hoverActivityFilter == null) hoverActivityFilter = hoverFilterFunc;

          _hoverActivityManager = new ActivityManager<IInteractionBehaviour>(manager.hoverActivationRadius,
                                                                             hoverActivityFilter);

          _hoverActivityManager.activationLayerMask = manager.interactionLayer.layerMask
                                                    | manager.interactionNoContactLayer.layerMask;
        }
        return _hoverActivityManager;
      }
    }

    /// <summary>
    /// Disables broadphase checks if an object is currently interacting with this hand.
    /// </summary>
    private bool _hoverLocked = false;
    /// <summary>
    /// When set to true, locks the current primarily hovered object, even if the hand gets closer to
    /// a different object.
    /// </summary>
    public bool hoverLocked {
      get { return _hoverLocked; }
      set { _hoverLocked = value; }
    }

    /// <summary>
    /// Gets the current position to check against nearby objects for hovering.
    /// Position is only used if the controller is currently tracked. For example,
    /// InteractionHand returns the center of the palm of the underlying Leap hand.
    /// </summary>
    protected abstract Transform hoverPoint { get; }

    private HashSet<IInteractionBehaviour> _hoveredObjects;
    /// <summary>
    /// Returns a set of all Interaction objects currently hovered by this
    /// InteractionController.
    /// </summary>
    public ReadonlyHashSet<IInteractionBehaviour> hoveredObjects { get { return _hoveredObjects; } }

    protected abstract List<Transform> _primaryHoverPoints { get; }
    /// <summary>
    /// Gets the list of Transforms to consider against nearby objects to determine
    /// the closest object (primary hover) of this controller.
    /// </summary>
    public ReadonlyList<Transform> primaryHoverPoints  { get { return primaryHoverPoints; } }

    /// <summary>
    /// Gets whether the InteractionControllerBase is currently primarily hovering over any interaction object.
    /// </summary>
    public bool isPrimaryHovering { get { return primaryHoveredObject != null; } }

    private IInteractionBehaviour _primaryHoveredObject;
    /// <summary>
    /// Gets the InteractionBehaviour that is currently this InteractionControllerBase's
    /// primary hovered object, if there is one.
    /// </summary>
    public IInteractionBehaviour primaryHoveredObject { get { return _primaryHoveredObject; } }

    private float _primaryHoverDistance = float.PositiveInfinity;
    /// <summary>
    /// Gets the distance from the closest primary hover point on this controller to its
    /// primarily hovered object, if there are any.
    /// </summary>
    public float primaryHoverDistance { get { return _primaryHoverDistance; } }

    /// <summary> Index of the closest primary hover point in the primaryHoverPoints list. </summary>
    private int                         _primaryHoverPointIdx = -1;
    private List<IInteractionBehaviour> _perPointPrimaryHovered = new List<IInteractionBehaviour>();
    private List<float>                 _perPointPrimaryHoverDistance = new List<float>();

    private void fixedUpdateHovering() {
      using (new ProfilerSample("Fixed Update InteractionControllerBase Hovering")) {
        // Reset hover lock if the controller loses tracking.
        if (!isTracked && hoverLocked) {
          _hoverLocked = false;
        }

        // Update hover state if it's not currently locked.
        if (!hoverLocked) {
          hoverActivityManager.activationRadius = manager.WorldHoverActivationRadius;

          Vector3? queryPosition = isTracked ? (Vector3?)hoverPoint.position : null;
          hoverActivityManager.UpdateActivityQuery(queryPosition, LeapSpace.allEnabled);

          // Add all returned objects as "hovered".
          // Find closest objects per primary hover point to update primary hover state.
          using (new ProfilerSample("Find Closest Objects for Primary Hover")) {
            refreshHoverState(_hoverActivityManager.ActiveObjects);
          }
        }

        // Refresh buffer information from the previous frame to be able to fire
        // the appropriate hover state callbacks.
        refreshHoverStateBuffers();
        refreshPrimaryHoverStateBuffers();

        // Support interactions in curved ("warped") space.
        if (isTracked && isPrimaryHovering) {
          ISpaceComponent space = primaryHoveredObject.space;

          if (space != null) {
            unwarpColliders(primaryHoverPoints[_primaryHoverPointIdx], space);
          }
        }
      }
    }

    /// <summary>
    /// Implementing this method is necessary to support curved spaces as
    /// rendered by a Leap Graphic Renderer. See InteractionHand for an example
    /// implementation. (Implementing this method is optional if you are not
    /// using a curved space as rendered by a Leap Graphic Renderer.)
    /// </summary>
    /// <remarks>
    /// Warps the collider transforms of this controller by the inverse of the
    /// transformation that is applied on the provided warpedSpaceElement, using
    /// the primaryHoverPoint as the pivot transform for the transformation.
    /// 
    /// ITransformer.WorldSpaceUnwarp is a useful method here. (ISpaceComponents
    /// contain references to their transformers via their anchors.)
    /// 
    /// ISpaceComponents denote game objects whose visual positions are warped
    /// from rectilinear (non-warped) space into a curved space (via, for example,
    /// a LeapCylindricalSpace, which can only be rendered correctly by the Leap
    /// Graphic Renderer). This method reverses that transformation for the hand,
    /// bringing it into the object's rectilinear space, allowing objects curved
    /// in this way to correctly collide with the bones in the hand or collider of
    /// a held controller.
    /// 
    /// The provided Transform is the closest primary hover point to any given
    /// primary hover candidate, so it is used as the pivot point for unwarping
    /// the colliders of this InteractionController.
    /// </remarks>
    protected abstract void unwarpColliders(Transform primaryHoverPoint,
                                            ISpaceComponent warpedSpaceElement);

    // Hover history, handled as part of the Interaction Manager's state-check calls.
    private IInteractionBehaviour _primaryHoveredLastFrame = null;
    private HashSet<IInteractionBehaviour> _hoveredLastFrame = new HashSet<IInteractionBehaviour>();

    /// <summary>
    /// Clears the previous hover state data and calculates it anew based on the
    /// latest hover and primary hover point data.
    /// </summary>
    private void refreshHoverState(HashSet<IInteractionBehaviour> hoverCandidates) {
      // Prepare data from last frame for hysteresis later on.
      int primaryHoverPointIdxLastFrame = _primaryHoveredLastFrame != null ? _primaryHoverPointIdx : -1;

      _hoveredObjects.Clear();
      _primaryHoveredObject = null;
      _primaryHoverDistance = float.PositiveInfinity;
      _primaryHoverPointIdx = -1;
      _perPointPrimaryHovered.Clear();
      _perPointPrimaryHoverDistance.Clear();

      // We can only update hover information if there's tracked data.
      if (!isTracked) return;

      // Determine values to apply hysteresis to the primary hover state.
      float maxNewPrimaryHoverDistance = float.PositiveInfinity;
      if (_primaryHoveredLastFrame != null && primaryHoverPointIdxLastFrame != -1) {
        float distanceToLastPrimaryHover = _primaryHoveredLastFrame.GetHoverDistance(
                                              primaryHoverPoints[primaryHoverPointIdxLastFrame].position);

        // The distance to a new object must be even closer than the current primary hover
        // distance in order for that object to become the new primary hover.
        maxNewPrimaryHoverDistance = distanceToLastPrimaryHover
                                    * distanceToLastPrimaryHover.Map(0.009F, 0.018F, 0.4F, 0.95F);

        // If we're very close, prevent the primary hover from changing at all.
        if (maxNewPrimaryHoverDistance < 0.008F) maxNewPrimaryHoverDistance = 0F;
      }

      foreach (IInteractionBehaviour behaviour in hoverCandidates) {
        // All hover candidates automatically count as hovered.
        _hoveredObjects.Add(behaviour);

        // Some objects can ignore consideration for primary hover as an
        // optimization, since it can require a lot of distance checks.
        if (behaviour.ignorePrimaryHover) continue;

        // Do further processing to determine the primary hover.
        else {

          // Check against all positions currently registered as primary hover points,
          // finding the closest one and updating hover data accordingly.
          float shortestPointDistance = float.PositiveInfinity;
          for (int i = 0; i < primaryHoverPoints.Count; i++) {
            var primaryHoverPoint = primaryHoverPoints[i];

            // It's possible to disable primary hover points to ignore them for hover
            // consideration.
            if (!primaryHoverPoint.gameObject.activeInHierarchy) continue;

            // TODO: ADD THIS BACK! To InteractionHand
            // Skip non-index fingers if they aren't extended.
            //if (!hand.Fingers[i].IsExtended && i != 1) { continue; }

            // Check primary hover for the primary hover point.
            float behaviourDistance = GetHoverDistance(primaryHoverPoint.position, behaviour);
            if (behaviourDistance < shortestPointDistance) {

              // This is the closest behaviour to this primary hover point.
              _perPointPrimaryHovered[i] = behaviour;
              _perPointPrimaryHoverDistance[i] = behaviourDistance;
              shortestPointDistance = behaviourDistance;

              if (shortestPointDistance < _primaryHoverDistance
                  && (behaviour == _primaryHoveredLastFrame || behaviourDistance < maxNewPrimaryHoverDistance)) {

                // This is the closest behaviour to ANY primary hover point, and the
                // distance is less than the hysteresis distance to transition away from
                // the previous primary hovered object.
                _primaryHoveredObject = _perPointPrimaryHovered[i];
                _primaryHoverDistance = _perPointPrimaryHoverDistance[i];
                _primaryHoverPointIdx = i;
              }
            }
          }
        }
      }
    }

    #region Hover State Checks

    private HashSet<IInteractionBehaviour> _hoverEndedBuffer = new HashSet<IInteractionBehaviour>();
    private HashSet<IInteractionBehaviour> _hoverBeganBuffer = new HashSet<IInteractionBehaviour>();

    private List<IInteractionBehaviour> _hoverRemovalCache = new List<IInteractionBehaviour>();
    private void refreshHoverStateBuffers() {
      _hoverBeganBuffer.Clear();
      _hoverEndedBuffer.Clear();

      var trackedBehaviours = _hoverActivityManager.ActiveObjects;
      foreach (var hoverable in trackedBehaviours) {
        bool inLastFrame = false, inCurFrame = false;
        if (hoveredObjects.Contains(hoverable)) {
          inCurFrame = true;
        }
        if (_hoveredLastFrame.Contains(hoverable)) {
          inLastFrame = true;
        }

        if (inCurFrame && !inLastFrame) {
          _hoverBeganBuffer.Add(hoverable);
          _hoveredLastFrame.Add(hoverable);
        }
        if (!inCurFrame && inLastFrame) {
          _hoverEndedBuffer.Add(hoverable);
          _hoveredLastFrame.Remove(hoverable);
        }
      }

      foreach (var hoverable in _hoveredLastFrame) {
        if (!trackedBehaviours.Contains(hoverable)) {
          _hoverEndedBuffer.Add(hoverable);
          _hoverRemovalCache.Add(hoverable);
        }
      }
      foreach (var hoverable in _hoverRemovalCache) {
        _hoveredLastFrame.Remove(hoverable);
      }
      _hoverRemovalCache.Clear();
    }

    /// <summary>
    /// Called by the Interaction Manager every fixed frame.
    /// Outputs objects that stopped being hovered by this hand this frame into hoverEndedObjects and returns whether
    /// the output set is empty.
    /// </summary>
    public bool CheckHoverEnd(out HashSet<IInteractionBehaviour> hoverEndedObjects) {
      // Hover checks via the activity manager are robust to destroyed or made-invalid objects,
      // so no additional validity checking is required.
      hoverEndedObjects = _hoverEndedBuffer;
      return _hoverEndedBuffer.Count > 0;
    }

    /// <summary>
    /// Called by the Interaction Manager every fixed frame.
    /// Outputs objects that began being hovered by this hand this frame into hoverBeganObjects and returns whether
    /// the output set is empty.
    /// </summary>
    public bool CheckHoverBegin(out HashSet<IInteractionBehaviour> hoverBeganObjects) {
      hoverBeganObjects = _hoverBeganBuffer;
      return _hoverBeganBuffer.Count > 0;
    }

    /// <summary>
    /// Called by the Interaction Manager every fixed frame.
    /// Outputs objects that are currently hovered by this hand into hoveredObjects and returns whether the
    /// output set is empty.
    /// </summary>
    public bool CheckHoverStay(out HashSet<IInteractionBehaviour> hoveredObjects) {
      hoveredObjects = _hoveredObjects;
      return hoveredObjects.Count > 0;
    }

    #endregion

    #region Primary Hover State Checks

    private IInteractionBehaviour _primaryHoverEndedObject = null;
    private IInteractionBehaviour _primaryHoverBeganObject = null;

    private void refreshPrimaryHoverStateBuffers() {
      if (primaryHoveredObject != _primaryHoveredLastFrame) {
        if (_primaryHoveredLastFrame != null) _primaryHoverEndedObject = _primaryHoveredLastFrame;
        else _primaryHoverEndedObject = null;

        _primaryHoveredLastFrame = primaryHoveredObject;

        if (_primaryHoveredLastFrame != null) _primaryHoverBeganObject = _primaryHoveredLastFrame;
        else _primaryHoverBeganObject = null;
      }
      else {
        _primaryHoverEndedObject = null;
        _primaryHoverBeganObject = null;
      }
    }

    /// <summary>
    /// Called by the Interaction Manager every fixed frame.
    /// Returns whether an object stopped being primarily hovered by this hand this frame and, if so,
    /// outputs the object into primaryHoverEndedObject (it will be null otherwise).
    /// </summary>
    public bool CheckPrimaryHoverEnd(out IInteractionBehaviour primaryHoverEndedObject) {
      primaryHoverEndedObject = _primaryHoverEndedObject;
      bool primaryHoverEnded = primaryHoverEndedObject != null;

      if (primaryHoverEnded && _primaryHoverEndedObject is InteractionBehaviour) {
        OnEndPrimaryHoveringObject(_primaryHoverEndedObject as InteractionBehaviour);
      }

      return primaryHoverEnded;
    }

    /// <summary>
    /// Called by the Interaction Manager every fixed frame.
    /// Returns whether an object began being primarily hovered by this hand this frame and, if so,
    /// outputs the object into primaryHoverBeganObject (it will be null otherwise).
    /// </summary>
    public bool CheckPrimaryHoverBegin(out IInteractionBehaviour primaryHoverBeganObject) {
      primaryHoverBeganObject = _primaryHoverBeganObject;
      bool primaryHoverBegan = primaryHoverBeganObject != null;

      if (primaryHoverBegan && _primaryHoverBeganObject is InteractionBehaviour) {
        OnBeginPrimaryHoveringObject(_primaryHoverBeganObject as InteractionBehaviour);
      }

      return primaryHoverBegan;
    }

    /// <summary>
    /// Called by the Interaction Manager every fixed frame.
    /// Returns whether any object is the primary hover of this hand and, if so, outputs
    /// the object into primaryHoveredObject.
    /// </summary>
    public bool CheckPrimaryHoverStay(out IInteractionBehaviour primaryHoveredObject) {
      primaryHoveredObject = _primaryHoveredObject;
      bool primaryHoverStayed = primaryHoveredObject != null;

      if (primaryHoverStayed && primaryHoveredObject is InteractionBehaviour) {
        OnStayPrimaryHoveringObject(primaryHoveredObject as InteractionBehaviour);
      }

      return primaryHoverStayed;
    }

    #endregion

    /// <summary>
    /// Returns the hover distance from the hoverPoint to the specified object, automatically
    /// accounting for ISpaceComponent warping if necessary.
    /// </summary>
    public static float GetHoverDistance(Vector3 hoverPoint, IInteractionBehaviour behaviour) {
      if (behaviour.space != null) {
        return behaviour.GetHoverDistance(TransformPoint(hoverPoint, behaviour.space));
      }
      else {
        return behaviour.GetHoverDistance(hoverPoint);
      }
    }

    /// <summary>
    /// Applies the spatial warping of the provided ISpaceComponent to a world-space point.
    /// </summary>
    public static Vector3 TransformPoint(Vector3 worldPoint, ISpaceComponent element) {
      if (element.anchor != null && element.anchor.space != null) {
        Vector3 localPos = element.anchor.space.transform.InverseTransformPoint(worldPoint);
        return element.anchor.space.transform.TransformPoint(element.anchor.transformer.InverseTransformPoint(localPos));
      }
      else {
        return worldPoint;
      }
    }

    #endregion

    #region Contact

    #region Brush Bones

    protected const float DEAD_ZONE_FRACTION = 0.1F;
    protected const float DISLOCATION_FRACTION = 3.0F;

    private static PhysicMaterial s_defaultContactBoneMaterial;
    protected static PhysicMaterial defaultContactBoneMaterial {
      get {
        if (s_defaultContactBoneMaterial == null) {
          initDefaultContactBoneMaterial();
        }
        return s_defaultContactBoneMaterial;
      }
    }

    /// <summary>
    /// ContactBones should have PhysicMaterials with a bounciness of
    /// zero and a bounce combine set to minimum.
    /// </summary>
    private static void initDefaultContactBoneMaterial() {
      if (s_defaultContactBoneMaterial == null) {
        s_defaultContactBoneMaterial = new PhysicMaterial();
      }
      s_defaultContactBoneMaterial.hideFlags = HideFlags.HideAndDontSave;
      s_defaultContactBoneMaterial.bounceCombine = PhysicMaterialCombine.Minimum;
      s_defaultContactBoneMaterial.bounciness = 0F;
    }

    private bool _contactInitialized = false;
    protected abstract ContactBone[] contactBones { get; }
    protected abstract GameObject contactBoneParent { get; }

    /// <summary>
    /// Called to initialize contact colliders. See remarks for implementation
    /// requirements.
    /// </summary>
    /// <remarks>
    /// initContact() should:
    /// - Return false at any time if initialization cannot be performed.
    /// - Ensure the "contactBones" property returns all contact colliders.
    ///   - (Construct contact colliders if they don't already exist.)
    /// - Ensure the "contactBoneParent" property returns the common parent of all
    ///   contact colliders.
    ///   - (Construct the contact bone parent if it doesn't already exist.)
    /// - Return true if initialization was successful.
    ///   
    /// Contact will only begin updating after initialization succeeds, otherwise
    /// it will try to initialize again on the next fixed frame.
    /// 
    /// After initialization, the contact bone parent's layer will be set to
    /// the Interaction Manager's contactBoneLayer.
    /// </remarks>
    protected abstract bool initContact();

    private void finishInitContact() {
      contactBoneParent.gameObject.layer = manager.contactBoneLayer;
    }

    private void fixedUpdateContact(bool contactEnabled) {
      // Make sure contact data is initialized.
      if (!_contactInitialized) {
        if (initContact()) {
          finishInitContact();
          _contactInitialized = true;
        }
        else {
          return;
        }
      }

      // Clear contact data if we lose tracking.
      if (!isTracked && _contactBehaviours.Count > 0) {
        _contactBehaviours.Clear();
      }

      // Disable contact bone parent if we lose tracking.
      if (!isTracked) {
        contactBoneParent.gameObject.SetActive(false);
        return;
      }
      else {
        if (!contactBoneParent.gameObject.activeSelf) {
          contactBoneParent.gameObject.SetActive(true);
        }
      }

      using (new ProfilerSample("Update Contact Bone Positions")) {
        for (int idx = 0; idx < contactBones.Length; idx++) {
          updateContactBone(idx);
        }
      }
      using (new ProfilerSample("Update Soft Contact")) {
        fixedUpdateSoftContact();
      }
      using (new ProfilerSample("Update ContactCallbacks")) {
        fixedUpdateContactState(contactEnabled);
      }
    }

    // TODO: UNUSED, DELETEME.
    ///// <summary>
    ///// Should contact bones using the latest data from the tracked controller.
    ///// This is called every fixed frame the Interaction Manager is enabled, even
    ///// if this controller is not tracked. (In this circumstance it's valid to
    ///// simply return immediately.)
    ///// </summary>
    //protected abstract void updateContactBonePositions();

    /// <summary>
    /// If your controller features no moving colliders relative to itself, simply
    /// return the desired position and rotation for the given indexed contact bone
    /// in the contactBones array. (For example, by recording the local position and
    /// local rotation of each contact bone in initContact()). More complex controllers,
    /// such as InteractionHand, uses this method to set ContactBone target positions and
    /// rotations based on the tracked Leap hand.
    /// </summary>
    protected abstract void getBoneTargetPositionRotation(int contactBoneIndex,
                                                          out Vector3 targetPosition,
                                                          out Quaternion targetRotation);

    private void updateContactBone(int contactBoneIndex) {
      ContactBone contactBone = contactBones[contactBoneIndex];
      Rigidbody   body = contactBone.body;

      Vector3    targetPosition;
      Quaternion targetRotation;
      getBoneTargetPositionRotation(contactBoneIndex, out targetPosition, out targetRotation);

      // Set a fixed rotation for bones; otherwise most friction is lost
      // as any capsule or spherical bones will roll on contact.
      body.MoveRotation(targetRotation);

      // Calculate how far off its target the contact bone is.
      float errorFraction = Vector3.Distance(contactBone.lastTarget, body.position) / contactBone.width;

      // Adjust the mass of the contact bone based on the mass of
      // the object it is currently touching.
      float speed = velocity.magnitude;
      float massScale = Mathf.Clamp(1.0F - (errorFraction * 2.0F), 0.1F, 1.0F)
                      * Mathf.Clamp(speed * 10F, 1F, 10F);
      body.mass = massScale * contactBone._lastObjectTouchedAdjustedMass;

      // Potentially enable Soft Contact if our error is too large.
      if (!_softContactEnabled && errorFraction >= DISLOCATION_FRACTION
          && speed < 1.5F
       /* && boneArrayIndex != NUM_FINGERS * BONES_PER_FINGER */) {
         EnableSoftContact();
         return;
      }

      // Attempt to move the contact bone to its target position by setting
      // its target velocity. Include a "deadzone" to avoid tiny vibrations.
      float deadzone = DEAD_ZONE_FRACTION * contactBone.width;
      // TODO: Delete me once deadzone is resolved.
      // deadzone = DEAD_ZONE_FRACTION
      //          * _unwarpedHandData.Fingers[1].Bone((Bone.BoneType)1).Width;
      Vector3 delta = targetPosition - body.position;
      float deltaMag = delta.magnitude;
      if (deltaMag <= deadzone) {
        body.velocity = Vector3.zero;
        contactBone.lastTarget = body.position;
      }
      else {
        delta *= (deltaMag - deadzone) / deltaMag;
        contactBone.lastTarget = body.position + delta;

        Vector3 targetVelocity = delta / Time.fixedDeltaTime;
        float targetVelocityMag = targetVelocity.magnitude;
        body.velocity = (targetVelocity / targetVelocityMag) * Mathf.Clamp(targetVelocityMag, 0F, 100F);
      }
    }

    #endregion

    #region Soft Contact

    private bool _softContactEnabled = false;
    private bool _disableSoftContactEnqueued = false;
    private IEnumerator _delayedDisableSoftContactCoroutine;
    private Collider[] _tempColliderArray = new Collider[2];
    private Vector3[] _previousBoneCenters = new Vector3[32];
    private float _softContactBoneRadius = 0.015f;

    private bool _notTrackedLastFrame = true;

    // Vars removed because Interaction Managers are currently non-optional.
    //private List<PhysicsUtility.SoftContact> softContacts = new List<PhysicsUtility.SoftContact>(40);
    //private Dictionary<Rigidbody, PhysicsUtility.Velocities> originalVelocities = new Dictionary<Rigidbody, PhysicsUtility.Velocities>();

    //private List<int> _softContactIdxRemovalBuffer = new List<int>();

    private void fixedUpdateSoftContact() {
      if (!isTracked) {
        _notTrackedLastFrame = true;
        return;
      }
      else {
        // If the hand was just initialized, initialize with soft contact.
        if (_notTrackedLastFrame) {
          EnableSoftContact();
        }

        _notTrackedLastFrame = false;
      }

      if (_softContactEnabled) {

        // Generate contacts.
        bool softlyContacting = false;
        for (int fingerIndex = 0; fingerIndex < 5; fingerIndex++) {
          for (int jointIndex = 0; jointIndex < 4; jointIndex++) {
            Bone bone = _warpedHandData.Fingers[fingerIndex].Bone((Bone.BoneType)(jointIndex));
            int boneArrayIndex = fingerIndex * 4 + jointIndex;
            Vector3 boneCenter = bone.Center.ToVector3();

            // Generate and fill softContacts with SoftContacts that are intersecting a sphere
            // at boneCenter, with radius softContactBoneRadius.
            bool sphereIntersecting;
            using (new ProfilerSample("Generate Soft Contacts")) {
              sphereIntersecting = PhysicsUtility.generateSphereContacts(boneCenter, _softContactBoneRadius,
                                                                       (boneCenter - _previousBoneCenters[boneArrayIndex])
                                                                         / Time.fixedDeltaTime,
                                                                       1 << manager.interactionLayer,
                                                                       ref manager._softContacts,
                                                                       ref manager._softContactOriginalVelocities,
                                                                       ref _tempColliderArray);
            }

            softlyContacting = sphereIntersecting ? true : softlyContacting;
          }
        }


        if (softlyContacting) {
          _disableSoftContactEnqueued = false;
        }
        else {
          // If there are no detected Contacts, exit soft contact mode.
          DisableSoftContact();
        }
      }

      // Update the last positions of the bones with this frame.
      for (int fingerIndex = 0; fingerIndex < 5; fingerIndex++) {
        for (int jointIndex = 0; jointIndex < 4; jointIndex++) {
          Bone bone = _warpedHandData.Fingers[fingerIndex].Bone((Bone.BoneType)(jointIndex));
          int boneArrayIndex = fingerIndex * 4 + jointIndex;
          _previousBoneCenters[boneArrayIndex] = bone.Center.ToVector3();
        }
      }
    }

    public void EnableSoftContact() {
      if (!isTracked) return;
      using (new ProfilerSample("Enable Soft Contact")) {
        _disableSoftContactEnqueued = false;
        if (!_softContactEnabled) {
          _softContactEnabled = true;
          ResetBrushBoneJoints();
          if (_delayedDisableSoftContactCoroutine != null) {
            manager.StopCoroutine(_delayedDisableSoftContactCoroutine);
          }
          for (int i = _contactBones.Length; i-- != 0; ) {
            _contactBones[i].collider.isTrigger = true;
          }

          // Update the last positions of the bones with this frame.
          // This prevents spurious velocities from freshly initialized hands.
          for (int fingerIndex = 0; fingerIndex < 5; fingerIndex++) {
            for (int jointIndex = 0; jointIndex < 4; jointIndex++) {
              Bone bone = _warpedHandData.Fingers[fingerIndex].Bone((Bone.BoneType)(jointIndex));
              int boneArrayIndex = fingerIndex * 4 + jointIndex;
              _previousBoneCenters[boneArrayIndex] = bone.Center.ToVector3();
            }
          }
        }
      }
    }

    public void DisableSoftContact() {
      using (new ProfilerSample("Enqueue Disable Soft Contact")) {
        if (!_disableSoftContactEnqueued) {
          _delayedDisableSoftContactCoroutine = DelayedDisableSoftContact();
          manager.StartCoroutine(_delayedDisableSoftContactCoroutine);
          _disableSoftContactEnqueued = true;
        }
      }
    }

    private IEnumerator DelayedDisableSoftContact() {
      if (_disableSoftContactEnqueued) { yield break; }
      yield return new WaitForSecondsRealtime(0.3f);
      if (_disableSoftContactEnqueued) {
        using (new ProfilerSample("Disable Soft Contact")) {
          _softContactEnabled = false;
          for (int i = _contactBones.Length; i-- != 0; ) {
            _contactBones[i].collider.isTrigger = false;
          }
          if (_hand != null) ResetBrushBoneJoints();
        }
      }
    }

    /// <summary> Constructs a Hand object from this InteractionControllerBase. Optional utility/debug function.
    /// Can be used to display a graphical hand that matches the physical one. </summary>
    public void FillBones(Hand inHand) {
      if (_softContactEnabled) { return; }
      if (Application.isPlaying && _contactBones.Length == NUM_FINGERS * BONES_PER_FINGER + 1) {
        Vector elbowPos = inHand.Arm.ElbowPosition;
        inHand.SetTransform(_contactBones[NUM_FINGERS * BONES_PER_FINGER].body.position, _contactBones[NUM_FINGERS * BONES_PER_FINGER].body.rotation);

        for (int fingerIndex = 0; fingerIndex < NUM_FINGERS; fingerIndex++) {
          for (int jointIndex = 0; jointIndex < BONES_PER_FINGER; jointIndex++) {
            Bone bone = inHand.Fingers[fingerIndex].Bone((Bone.BoneType)(jointIndex) + 1);
            int boneArrayIndex = fingerIndex * BONES_PER_FINGER + jointIndex;
            Vector displacement = _contactBones[boneArrayIndex].body.position.ToVector() - bone.Center;
            bone.Center += displacement;
            bone.PrevJoint += displacement;
            bone.NextJoint += displacement;
            bone.Rotation = _contactBones[boneArrayIndex].body.rotation.ToLeapQuaternion();
          }
        }

        inHand.Arm.PrevJoint = elbowPos;
        inHand.Arm.Direction = (inHand.Arm.PrevJoint - inHand.Arm.NextJoint).Normalized;
        inHand.Arm.Center = (inHand.Arm.PrevJoint + inHand.Arm.NextJoint) * 0.5f;
      }
    }

    #endregion

    #region Contact Callbacks

    private Dictionary<IInteractionBehaviour, int> _contactBehaviours = new Dictionary<IInteractionBehaviour, int>();
    private HashSet<IInteractionBehaviour> _contactBehavioursLastFrame = new HashSet<IInteractionBehaviour>();
    private List<IInteractionBehaviour> _contactBehaviourRemovalCache = new List<IInteractionBehaviour>();

    private HashSet<IInteractionBehaviour> _contactEndedBuffer = new HashSet<IInteractionBehaviour>();
    private HashSet<IInteractionBehaviour> _contactBeganBuffer = new HashSet<IInteractionBehaviour>();

    internal void ContactBoneCollisionEnter(ContactBone contactBone, IInteractionBehaviour interactionObj, bool wasTrigger) {
      int count;
      if (_contactBehaviours.TryGetValue(interactionObj, out count)) {
        _contactBehaviours[interactionObj] = count + 1;
      }
      else {
        _contactBehaviours[interactionObj] = 1;
      }
    }

    internal void ContactBoneCollisionExit(ContactBone contactBone, IInteractionBehaviour interactionObj, bool wasTrigger) {
      if (interactionObj.ignoreContact) {
        if (_contactBehaviours.ContainsKey(interactionObj)) _contactBehaviours.Remove(interactionObj);
        return;
      }

      int count = _contactBehaviours[interactionObj];
      if (count == 1) {
        _contactBehaviours.Remove(interactionObj);
      }
      else {
        _contactBehaviours[interactionObj] = count - 1;
      }
    }

    /// <summary>
    /// Called as a part of the Interaction Hand's general fixed frame update,
    /// before any specific-callback-related updates.
    /// </summary>
    private void fixedUpdateContactState(bool contactEnabled) {
      _contactEndedBuffer.Clear();
      _contactBeganBuffer.Clear();

      foreach (var interactionObj in _contactBehavioursLastFrame) {
        if (!_contactBehaviours.ContainsKey(interactionObj)
         || !_contactBoneParent.gameObject.activeInHierarchy
         || !contactEnabled) {
          _contactEndedBuffer.Add(interactionObj);
          _contactBehaviourRemovalCache.Add(interactionObj);
        }
      }
      foreach (var interactionObj in _contactBehaviourRemovalCache) {
        _contactBehavioursLastFrame.Remove(interactionObj);
      }
      _contactBehaviourRemovalCache.Clear();
      if (_contactBoneParent.gameObject.activeInHierarchy && contactEnabled) {
        foreach (var intObjCountPair in _contactBehaviours) {
          var interactionObj = intObjCountPair.Key;
          if (!_contactBehavioursLastFrame.Contains(interactionObj)) {
            _contactBeganBuffer.Add(interactionObj);
            _contactBehavioursLastFrame.Add(interactionObj);
          }
        }
      }
    }

    private List<IInteractionBehaviour> _removeContactObjsBuffer = new List<IInteractionBehaviour>();
    /// <summary>
    /// Called by the Interaction Manager every fixed frame.
    /// Outputs interaction objects that stopped being touched by this hand this frame into contactEndedObjects
    /// and returns whether the output set is empty.
    /// </summary>
    public bool CheckContactEnd(out HashSet<IInteractionBehaviour> contactEndedObjects) {
      // Ensure contact objects haven't been destroyed or set to ignore contact
      _removeContactObjsBuffer.Clear();
      foreach (var objTouchCountPair in _contactBehaviours) {
        if (objTouchCountPair.Key.gameObject == null
            || objTouchCountPair.Key.rigidbody == null
            || objTouchCountPair.Key.ignoreContact
            || _hand == null) {
          _removeContactObjsBuffer.Add(objTouchCountPair.Key);
        }
      }

      // Clean out removed, invalid, or ignoring-contact objects
      foreach (var intObj in _removeContactObjsBuffer) {
        _contactBehaviours.Remove(intObj);
        _contactEndedBuffer.Add(intObj);
      }

      contactEndedObjects = _contactEndedBuffer;
      return _contactEndedBuffer.Count > 0;
    }

    /// <summary>
    /// Called by the Interaction Manager every fixed frame.
    /// Outputs interaction objects that started being touched by this hand this frame into contactBeganObjects
    /// and returns whether the output set is empty.
    /// </summary>
    public bool CheckContactBegin(out HashSet<IInteractionBehaviour> contactBeganObjects) {
      contactBeganObjects = _contactBeganBuffer;
      return _contactBeganBuffer.Count > 0;
    }

    private HashSet<IInteractionBehaviour> _contactedObjects = new HashSet<IInteractionBehaviour>();
    /// <summary>
    /// Called by the Interaction Manager every fixed frame.
    /// Outputs interaction objects that are currently being touched by the hand into contactedObjects
    /// and returns whether the output set is empty.
    /// </summary>
    public bool CheckContactStay(out HashSet<IInteractionBehaviour> contactedObjects) {
      _contactedObjects.Clear();
      foreach (var objCountPair in _contactBehaviours) {
        _contactedObjects.Add(objCountPair.Key);
      }

      contactedObjects = _contactedObjects;
      return contactedObjects.Count > 0;
    }

    #endregion

    #endregion

    #region Grasping

    // Grasp Activity Manager
    private ActivityManager<IInteractionBehaviour> _graspActivityManager;
    /// <summary> Determines which objects are graspable any given frame. </summary>
    private ActivityManager<IInteractionBehaviour> graspActivityManager {
      get {
        if (_graspActivityManager == null) {
          if (graspActivityFilter == null) graspActivityFilter = graspFilterFunc;

          _graspActivityManager = new ActivityManager<IInteractionBehaviour>(1F, graspActivityFilter);

          _graspActivityManager.activationLayerMask = manager.interactionLayer.layerMask
                                                    | manager.interactionNoContactLayer.layerMask;
        }
        return _graspActivityManager;
      }
    }

    private Func<Collider, IInteractionBehaviour> graspActivityFilter;
    private IInteractionBehaviour graspFilterFunc(Collider collider) {
      if (collider.attachedRigidbody != null) {
        Rigidbody body = collider.attachedRigidbody;
        IInteractionBehaviour intObj;
        if (body != null && manager.interactionObjectBodies.TryGetValue(body, out intObj)
            && !intObj.ignoreGrasping) {
          return intObj;
        }
      }

      return null;
    }

    private HeuristicGrabClassifier _grabClassifier;
    /// <summary> Handles logic determining whether a hand has grabbed or released an interaction object. </summary>
    public HeuristicGrabClassifier grabClassifier {
      get {
        if (_grabClassifier == null) _grabClassifier = new HeuristicGrabClassifier(this);
        return _grabClassifier;
      }
    }

    private IInteractionBehaviour _graspedObject = null;

    private void FixedUpdateGrasping() {
      using (new ProfilerSample("Fixed Update InteractionControllerBase Grasping")) {
        graspActivityManager.UpdateActivityQuery(GetLastTrackedLeapHand().PalmPosition.ToVector3(), LeapSpace.allEnabled);
        grabClassifier.FixedUpdateClassifierHandState();
      }
    }

    /// <summary>
    /// Called by the Interaction Manager every fixed frame.
    /// Returns true if the hand just released an object and outputs the released object into releasedObject.
    /// </summary>
    public bool CheckGraspEnd(out IInteractionBehaviour releasedObject) {
      releasedObject = null;

      bool shouldReleaseObject = false;

      // Check releasing against interaction state.
      if (_graspedObject == null) {
        return false;
      }
      else if (_graspedObject.ignoreGrasping) {
        grabClassifier.NotifyGraspReleased(_graspedObject);
        releasedObject = _graspedObject;

        shouldReleaseObject = true;
      }

      // Update the grab classifier to determine if we should release the grasped object.
      if (!shouldReleaseObject) shouldReleaseObject = grabClassifier.FixedUpdateClassifierRelease(out releasedObject);

      if (shouldReleaseObject) {
        _graspedObject = null;
        EnableSoftContact(); // prevent objects popping out of the hand on release
        return true;
      }

      return false;
    }

    /// <summary>
    /// Called by the Interaction Manager every fixed frame.
    /// Returns true if the hand just grasped an object and outputs the grasped object into graspedObject.
    /// </summary>
    public bool CheckGraspBegin(out IInteractionBehaviour newlyGraspedObject) {
      newlyGraspedObject = null;

      // Check grasping against interaction state.
      if (_graspedObject != null) {
        // Can't grasp any object if we're already grasping one
        return false;
      }

      // Update the grab classifier to determine if we should grasp an object.
      bool shouldGraspObject = grabClassifier.FixedUpdateClassifierGrasp(out newlyGraspedObject);
      if (shouldGraspObject) {
        _graspedObject = newlyGraspedObject;

        return true;
      }

      return false;
    }

    /// <summary>
    /// Called by the Interaction Manager every fixed frame.
    /// Returns whether there the hand is currently grasping an object and, if it is, outputs that
    /// object into graspedObject.
    /// </summary>
    public bool CheckGraspHold(out IInteractionBehaviour graspedObject) {
      graspedObject = _graspedObject;
      return graspedObject != null;
    }

    /// <summary>
    /// Called by the Interaction Manager every fixed frame.
    /// Returns whether the hand began suspending an object this frame and, if it did, outputs that
    /// object into suspendedObject.
    /// </summary>
    public bool CheckSuspensionBegin(out IInteractionBehaviour suspendedObject) {
      suspendedObject = null;

      if (_graspedObject != null && _hand == null && !_graspedObject.isSuspended) {
        suspendedObject = _graspedObject;
      }

      return suspendedObject != null;
    }

    /// <summary>
    /// Called by the Interaction Manager every fixed frame.
    /// Returns whether the hand stopped suspending an object this frame and, if it did, outputs that
    /// object into resumedObject.
    /// </summary>
    public bool CheckSuspensionEnd(out IInteractionBehaviour resumedObject) {
      resumedObject = null;

      if (_graspedObject != null && _hand != null && _graspedObject.isSuspended) {
        resumedObject = _graspedObject;
      }

      return resumedObject != null;
    }

    #endregion

    #region Gizmos

    /// <summary>
    /// By default, this method will draw all of the colliders found in the
    /// contactBoneParent hierarchy. You may override this method to modify
    /// its behavior.
    /// </summary>
    public virtual void OnDrawRuntimeGizmos(RuntimeGizmoDrawer drawer) {
      if (contactBoneParent != null) {
        drawer.DrawColliders(contactBoneParent.gameObject, true, true);
      }
    }

    public void OnDrawRuntimeGizmos(RuntimeGizmoDrawer drawer) {
      if (_contactBoneParent != null) {
        drawer.DrawColliders(_contactBoneParent, true, true);
      }
    }

    #endregion

  }

}