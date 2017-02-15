﻿using UnityEngine;
using UnityEngine.UI;
using UnityEngine.VR;
using System.Collections;
using System.Collections.Generic;

namespace Leap.Unity {
  public class LocomotionAvatar : MonoBehaviour {
    protected Animator animator;

    private float speed = 0;
    private float averageSpeed = 0;
    private Queue<float> speedList = new Queue<float>(10);
    private float direction = 0;
    private Locomotion locomotion = null;

    private Vector3 moveDirection;
    
    private Vector3 distanceToRoot;
    private Vector3 rootDirection;
    public Transform LMRig;
    public Transform Target;

    public Text standWalkStateText;
    public Text m_AnimatorStateText;
    public Text CenteringText;
    public Text DistanceText;
    public Text SpeedText;
    private bool isDisplayState = false;

    //For debugging - - - - - - - - - - - - - - - - 
    public Transform AnimatorRoot;
    public Transform CameraOnGround;
    [HideInInspector]
    public bool IsCentering = false;

    public bool WalkingEnabled = true;
    public bool crouchEnabled = false;
    private float userHeight = 1.63241f;

    void Awake() {
      LMRig = GameObject.FindObjectOfType<LeapHandController>().transform.root;
    }

    void Start() {
      //CenteringText.text = "";
      InputTracking.Recenter();
      animator = GetComponent<Animator>();
      locomotion = new Locomotion(animator);
      rootDirection = transform.forward;

      //Creating runtime gizmo target similar to ShoulderTurnBehavior.cs
      GameObject markerPrefab = Resources.Load("RuntimeGizmoMarker") as GameObject;
      Target = GameObject.Instantiate(markerPrefab).transform;
      Target.name = transform.name + "_ChestReferenceMarker";
      Target.parent = GameObject.FindObjectOfType<LeapVRCameraControl>().transform;
      Target.localPosition = new Vector3(0, 0, 2);
    }

    void Update() {
      AnimatorLocomotion();
      
      if (isDisplayState) {
        StateDisplay();
      }
      //if (Input.GetKeyUp(KeyCode.Space)) {
      //  CenterUnderCamera();
      //  userHeight = Camera.main.transform.position.y;
      //}
    }
   
   
    Vector3 MoveDirectionCameraDirection() {
      // Get camera rotation.
      rootDirection = transform.forward;// +transform.position;
      Vector3 CameraDirection = Camera.main.transform.forward;
      CameraDirection.y = 0.0f;
      return CameraDirection;
    }
    Vector3 MoveDirectionTowardCamera() {
      // Get camera rotation.
      rootDirection = transform.forward;// +transform.position;
      Vector3 DirectionToCamera = Camera.main.transform.position - transform.position;
      DirectionToCamera.y = 0.0f;
      Quaternion referentialShift = Quaternion.FromToRotation(Vector3.forward, DirectionToCamera);
      // Convert joystick input in Worldspace coordinates
      return DirectionToCamera;
    }
    bool standing = true;

    public void CenterUnderCamera() {
      animator.transform.position = new Vector3(Camera.main.transform.position.x, transform.position.y, Camera.main.transform.position.z);
    }

    void AnimatorLocomotion() {
      float reverse = 1;
      Vector3 flatCamPosition = Camera.main.transform.position;
      flatCamPosition.y = 0;
      Vector3 flatRootPosition = transform.position;
      flatRootPosition.y = 0;
      distanceToRoot = flatCamPosition - flatRootPosition;
      speed = distanceToRoot.magnitude;

      if (speedList.Count >= 10) {
        speedList.Dequeue();
      }
      if (speedList.Count < 10) {
        speedList.Enqueue(speed);
      }
      averageSpeed = 0;
      foreach (float s in speedList) {
        averageSpeed += s;
      }
      averageSpeed = (averageSpeed / 10);



      if (!standing && averageSpeed < .15f) {
        standing = true;
      }
      if (standing && averageSpeed > .30) {
        standing = false;
      }
      if (standing) { //Dead "stick" and matching LMRigLococmotion method for turning in place
        averageSpeed = 0.0f;
        moveDirection = MoveDirectionCameraDirection();
      }
      else {
        moveDirection = MoveDirectionTowardCamera();
        //if (transform.InverseTransformPoint(Camera.main.transform.position).z < -.1f
        //  && direction > -20 && direction < 20) {
        //  Debug.Log("Reversing");
        //  moveDirection = MoveDirectionCameraDirection();
        //  reverse = -1;
        //}
      }

      //Vector3 moveDirection = referentialShift * CameraDirection;
      Vector3 axis = Vector3.Cross(rootDirection, moveDirection);
      direction = Vector3.Angle(rootDirection, moveDirection) / 180f * (axis.y < 0 ? -1 : 1);
      if (animator && Camera.main && WalkingEnabled) {
        locomotion.Do(averageSpeed * 1.25f, (direction * 180), reverse);
        Debug.DrawLine(transform.position, moveDirection * 2, Color.red);
      }
    }
    void OnAnimatorIK() {
      //Floating crouch
      if (crouchEnabled && !WalkingEnabled) {
        float heightOffset = 0;
        if (Camera.main.transform.position.y < userHeight) {
          heightOffset = Camera.main.transform.position.y - userHeight;
          animator.transform.position = new Vector3(animator.transform.position.x, heightOffset, animator.transform.position.z);
        }
      }
      Vector3 placeAnimatorUnderCam = new Vector3(Camera.main.transform.position.x, transform.position.y, Camera.main.transform.position.z);

      if (IsCentering || !WalkingEnabled) {
        animator.transform.position = Vector3.Lerp(animator.rootPosition, placeAnimatorUnderCam, .05f);
        var lookPos = Target.position - transform.position;
        lookPos.y = 0;
        var rotation = Quaternion.LookRotation(lookPos);
        animator.transform.rotation = Quaternion.Slerp(transform.rotation, rotation, Time.deltaTime * 1.5f);

        //Optional: lock animator.transform rotation to camera y
        //float CameraRotationY = Camera.main.transform.rotation.y;
        //animator.transform.rotation = new Quaternion(animator.transform.rotation.x, CameraRotationY, animator.transform.rotation.z, animator.transform.rotation.w); 
      }
    }

    void StateDisplay() {
      if (standing) {
        standWalkStateText.text = "Idle/Turning";
      }
      else standWalkStateText.text = "WalkRun";
      AnimatorStateInfo state = animator.GetCurrentAnimatorStateInfo(0);
      if (state.IsName("Locomotion.Idle")) {
        m_AnimatorStateText.text = "State: Idle";
      }
      if (state.IsName("Locomotion.TurnOnSpot")) {
        m_AnimatorStateText.text = "State: TurnOnSpot";
      }
      if (state.IsName("Locomotion.WalkRun")) {
        m_AnimatorStateText.text = "State: WalkRun";
      }
      DistanceText.text = distanceToRoot.magnitude.ToString("F2");
      SpeedText.text = animator.GetFloat("Speed").ToString("F2");
    }
  }
}